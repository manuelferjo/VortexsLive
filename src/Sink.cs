// Un destino (Twitch, Kick, YouTube u Otro) mientras el servicio está en marcha.
//
// El hilo de salida del motor llama a Offer con cada paquete; el Sink decide si lo quiere (Twitch
// recibe todas las calidades; el resto, solo la mejor), recalcula su marca de tiempo para que su
// emisión empiece en 0 y lo deja en su propia cola. Un hilo "enviar-…" por destino vacía esa cola
// hacia la plataforma, así un destino lento no retrasa a los demás.
// Si la conexión se cae, se reintenta cada 3, 6, 12, 24 y 30 s.
using System;
using System.Collections.Generic;
using System.Threading;

namespace StreamDelay
{
	internal class Sink
	{
		private struct Item
		{
			public byte Type;

			public long Ts;

			public byte[] Data;

			// Cuándo entró en la cola (para saber si la subida se queda atrás).
			public long At;
		}

		// Si la cola pasa de 48 MB o de 8 s de retraso, la conexión no da para tanto: se reconecta.
		private const long MaxQueueBytes = 50331648L;

		private const long MaxLagMs = 8000L;

		public readonly Destination D;

		private readonly Engine e;

		private readonly object lk = new object();

		private readonly Queue<Item> items = new Queue<Item>();

		private long qBytes;

		private IConn conn;

		// Conexión RTMP que se está abriendo (para poder cancelarla).
		private RtmpClient pending;

		private bool connecting;

		// Conexión recién abierta: hay que mandar primero las cabeceras.
		private bool fresh;

		private long nextStartAt;

		private long startedAt;

		private int fails;

		// Clave con la que se conectó (si OBS cambia de clave, se reconecta).
		private string outKey;

		private string lastNote;

		// Hay que esperar a un fotograma clave antes de mandar vídeo.
		private bool needKey = true;

		// Marca de tiempo (del motor) que corresponde al 0 de este destino.
		private long tsBase = -1L;

		// Audio anterior al primer fotograma clave: se tira para que empiecen juntos.
		private long dropAudioBelow = -1L;

		private long lastRel;

		// Pistas de vídeo que ya han recibido su primer fotograma clave.
		private readonly HashSet<int> started = new HashSet<int>();

		private readonly HashSet<int> tracksOut = new HashSet<int>();

		public volatile SinkState State;

		public volatile string Note = "";

		public volatile int Tracks;

		// Calidad que recibe este destino (p. ej. "1080p60"). Kick, YouTube y Otro reciben la pista de vídeo
		// de más resolución que mande OBS, sea cual sea su posición en la Transmisión mejorada.
		public volatile string Quality = "";

		// Pista de vídeo que recibe este destino (si no recibe todas).
		private int vTrack;

		private volatile bool stopping;

		// Twitch recibe todas las calidades (salvo en el modo clásico con ffmpeg).
		public bool AllTracks
		{
			get
			{
				if (D.IsTwitch)
				{
					return !e.UseFfmpeg;
				}
				return false;
			}
		}

		public bool Live
		{
			get
			{
				return State == SinkState.Live;
			}
		}

		// ---------- Estadísticas para la ficha de destinos ----------

		// Bytes enviados a la plataforma (lo suma el hilo "enviar-…").
		private long sentBytes;

		// Veces que se ha caído la conexión desde que se inició el servicio.
		public volatile int Reconnects;

		// Velocidad de subida a este destino (kbps, media de unos segundos) y desde cuándo está en directo.
		public volatile int RateKbps;

		private long rateLastBytes;

		private long rateLastAt = -1L;

		private double rateAvg;

		public long LiveForMs
		{
			get
			{
				return (State == SinkState.Live) ? Math.Max(0L, e.Now - startedAt) : 0L;
			}
		}

		// La llama la ventana cada medio segundo para calcular la velocidad.
		public void SampleRate()
		{
			long now = e.Now;
			long bytes = Interlocked.Read(ref sentBytes);
			if (rateLastAt >= 0L && now > rateLastAt)
			{
				double kbps = (double)(bytes - rateLastBytes) * 8.0 / (double)(now - rateLastAt);
				rateAvg = (rateAvg <= 0.0) ? kbps : (rateAvg * 0.75 + kbps * 0.25);
			}
			rateLastBytes = bytes;
			rateLastAt = now;
			RateKbps = (State == SinkState.Live) ? (int)Math.Round(rateAvg) : 0;
			if (State != SinkState.Live)
			{
				rateAvg = 0.0;
			}
		}

		public long QueueBytes
		{
			get
			{
				lock (lk)
				{
					return qBytes;
				}
			}
		}

		// Antigüedad del paquete más viejo pendiente de enviar: si crece, la subida no da para todo.
		public long QueueAgeMs
		{
			get
			{
				lock (lk)
				{
					return (items.Count == 0) ? 0L : Math.Max(0L, e.Now - items.Peek().At);
				}
			}
		}

		public Sink(Destination d, Engine e)
		{
			D = d;
			this.e = e;
		}

		private void Log(string m)
		{
			e.Log(D.Name + ": " + m);
		}

		// Un paquete que sale del delay (outTs = su marca de tiempo de salida). seqs son las últimas
		// cabeceras, que se mandan al principio de cada conexión.
		public void Offer(FlvPacket p, long outTs, Dictionary<int, FlvPacket> seqs, long now)
		{
			bool switched = false;
			if (!AllTracks)
			{
				int m = e.MainTrack;
				if (m != vTrack)
				{
					vTrack = m;
					switched = true;
				}
				Quality = e.LabelFor(vTrack);
			}
			else
			{
				Quality = "";
			}
			if (!Wants(p))
			{
				return;
			}
			IConn conn;
			bool isFresh;
			lock (lk)
			{
				conn = this.conn;
				isFresh = fresh;
				fresh = false;
			}
			if (conn == null)
			{
				TryConnect(p.Src, now);
				return;
			}
			string key = e.KeyFor(D, p.Src);
			if (key != outKey)
			{
				Log("OBS usa otra clave: reconectando.");
				Disconnect(false);
				lock (lk)
				{
					nextStartAt = 0L;
				}
				TryConnect(p.Src, now);
				return;
			}
			if (isFresh)
			{
				// Conexión nueva: primero los metadatos y después las cabeceras de audio y vídeo.
				needKey = true;
				tsBase = -1L;
				started.Clear();
				tracksOut.Clear();
				foreach (KeyValuePair<int, FlvPacket> seq in seqs)
				{
					if (seq.Value.Type == 18)
					{
						Enqueue(18, 0L, seq.Value.Data, now);
					}
				}
				foreach (KeyValuePair<int, FlvPacket> seq in seqs)
				{
					if (seq.Value.Type != 18 && Wants(seq.Value))
					{
						byte[] d = DataFor(seq.Value);
						if (d == null)
						{
							continue;
						}
						Enqueue(seq.Value.Type, 0L, d, now);
						if (seq.Value.Type == 9 && tracksOut.Add(seq.Value.Track))
						{
							Tracks = tracksOut.Count;
						}
					}
				}
			}
			else if (switched)
			{
				// Cambió la pista de más resolución (nueva sesión de OBS): enviar su cabecera y arrancar en su fotograma clave.
				Log("ahora recibe " + Quality + ".");
				foreach (KeyValuePair<int, FlvPacket> seq in seqs)
				{
					if (seq.Value.Type == 9 && Wants(seq.Value))
					{
						byte[] d = DataFor(seq.Value);
						if (d != null)
						{
							Enqueue(9, Math.Max(0L, lastRel), d, now);
						}
					}
				}
				started.Clear();
			}
			long ts;
			if (p.Seq)
			{
				ts = Math.Max(0L, lastRel);
				if (p.Type == 9 && p.SeqStart && tracksOut.Add(p.Track))
				{
					Tracks = tracksOut.Count;
				}
			}
			else
			{
				if (needKey)
				{
					if (p.Type != 9 || !p.Key)
					{
						return;
					}
					needKey = false;
					if (tsBase < 0)
					{
						tsBase = outTs;
					}
					dropAudioBelow = outTs;
					started.Clear();
				}
				if (p.Type == 8 && outTs < dropAudioBelow)
				{
					return;
				}
				// Cada pista de vídeo empieza en su propio fotograma clave.
				if (p.Type == 9 && !started.Contains(p.Track))
				{
					if (!p.Key)
					{
						return;
					}
					started.Add(p.Track);
				}
				ts = Math.Max(0L, outTs - tsBase);
				lastRel = Math.Max(lastRel, ts);
			}
			byte[] data = DataFor(p);
			if (data == null)
			{
				return;
			}
			if (!Enqueue(p.Type, ts, data, now))
			{
				Fail(conn, "la conexión con " + D.Name + " va demasiado lenta");
			}
		}

		// Twitch recibe todas las pistas; el resto, solo el vídeo de más resolución y el audio principal.
		private bool Wants(FlvPacket p)
		{
			if (AllTracks || p.Type == 18)
			{
				return true;
			}
			if (p.Type == 9)
			{
				return p.Track == vTrack;
			}
			return p.Track == 0;
		}

		// Si la mejor calidad llega en una pista extra (formato multipista), se pasa al formato normal.
		private byte[] DataFor(FlvPacket p)
		{
			if (AllTracks || p.Type != 9 || p.Track == 0)
			{
				return p.Data;
			}
			if (!p.SingleDone)
			{
				p.Single = VideoInfo.ToSingleTrack(p.Data);
				p.SingleDone = true;
			}
			return p.Single;
		}

		// Mete un paquete en la cola de envío. false = la cola va demasiado atrasada.
		private bool Enqueue(byte type, long ts, byte[] data, long now)
		{
			lock (lk)
			{
				if (conn == null)
				{
					return true;
				}
				if (qBytes > MaxQueueBytes || (items.Count > 0 && now - items.Peek().At > MaxLagMs))
				{
					return false;
				}
				items.Enqueue(new Item
				{
					Type = type,
					Ts = ts,
					Data = data,
					At = now
				});
				qBytes += data.Length;
				if (items.Count == 1)
				{
					Monitor.PulseAll(lk);
				}
				return true;
			}
		}

		// Faltan pocos segundos para que salga el primer paquete: conectar ya (o reconectar si cambió la clave).
		public void Prepare(RtmpSession src, long now)
		{
			bool connect;
			bool keyChanged;
			lock (lk)
			{
				connect = conn == null && !connecting;
				keyChanged = conn != null && outKey != null && e.KeyFor(D, src) != outKey;
			}
			if (keyChanged)
			{
				Log("OBS usa otra clave: reconectando.");
				Disconnect(true);
				connect = true;
			}
			if (connect)
			{
				TryConnect(src, now);
			}
		}

		// Empieza a conectar en otro hilo (si no se está ya y no toca esperar al siguiente reintento).
		private void TryConnect(RtmpSession src, long now)
		{
			if (!e.Running)
			{
				return;
			}
			string key = e.KeyFor(D, src);
			lock (lk)
			{
				if (conn != null || connecting || now < nextStartAt)
				{
					return;
				}
				if (D.Server.Trim().Length == 0 || key.Length == 0)
				{
					State = SinkState.NoKey;
					Note = ((D.Server.Trim().Length == 0) ? "Falta el servidor" : "Falta la clave");
					string message = ((D.Server.Trim().Length == 0) ? "falta el servidor (Ajustes → Destinos)." : (D.IsTwitch ? "falta la clave: ponla en Ajustes → Destinos o usa el servicio Twitch en OBS con tu clave." : "falta la clave: escríbela en Ajustes → Destinos."));
					// El mismo aviso solo se escribe una vez.
					if (message != lastNote)
					{
						Log(message);
					}
					lastNote = message;
					nextStartAt = now + 3000;
					return;
				}
				lastNote = null;
				connecting = true;
				State = SinkState.Connecting;
				Note = "Conectando…";
			}
			string url = D.Server.Trim().TrimEnd('/');
			Thread worker = new Thread((ThreadStart)delegate
			{
				ConnectProc(url, key);
			})
			{
				IsBackground = true,
				Name = "conectar-" + D.Id
			};
			worker.Start();
		}

		// Hilo "conectar-…": abre la conexión y, si va bien, arranca el hilo que envía.
		private void ConnectProc(string url, string key)
		{
			IConn c = null;
			RtmpClient rc = null;
			string error = null;
			try
			{
				if (e.UseFfmpeg)
				{
					c = new FfmpegConn(e.FfmpegPath, url + "/" + key, e.Log, (string m) => Engine.Mask(m, key));
				}
				else
				{
					rc = new RtmpClient
					{
						Log = e.Log
					};
					lock (lk)
					{
						pending = rc;
					}
					rc.Connect(url, key, 10000);
					c = new RtmpConn(rc);
				}
			}
			catch (Exception ex)
			{
				error = Engine.Mask(ex.Message, key);
			}
			lock (lk)
			{
				pending = null;
			}
			if (c != null && (!e.Running || stopping))
			{
				// Se paró el servicio mientras conectaba.
				c.Close(false);
				lock (lk)
				{
					connecting = false;
					State = SinkState.Ready;
					Note = "";
					return;
				}
			}
			if (c == null)
			{
				int waitMs;
				lock (lk)
				{
					connecting = false;
					fails++;
					// 3, 6, 12, 24 y como mucho 30 s.
					waitMs = Math.Min(30000, 3000 << Math.Min(fails - 1, 4));
					nextStartAt = e.Now + waitMs;
					State = SinkState.Retrying;
					Note = "Reintento en " + waitMs / 1000 + " s";
				}
				Log("no se pudo conectar (" + error + "). Reintento en " + waitMs / 1000 + " s.");
				if (fails == 2)
				{
					Log(D.IsTwitch ? "revisa el servidor y la clave. Con Transmisión mejorada la clave la pone OBS (servicio Twitch): vuelve a iniciar la transmisión en OBS." : "revisa el servidor y la clave (sin espacios) y que no haya otro programa emitiendo con esa clave.");
				}
				return;
			}
			lock (lk)
			{
				connecting = false;
				conn = c;
				fresh = true;
				outKey = key;
				startedAt = e.Now;
				items.Clear();
				qBytes = 0L;
				State = SinkState.Live;
				Note = "En directo";
			}
			Log(e.UseFfmpeg ? "enviando (ffmpeg, modo clásico)." : ("enviando" + (Engine.IsEnhancedKey(key) ? " con Transmisión mejorada (todas las calidades)." : (AllTracks ? "." : (" " + ((e.LabelFor(e.MainTrack).Length > 0) ? e.LabelFor(e.MainTrack) : "la calidad más alta") + " (" + D.Name + " crea el resto a partir de ella).")))));
			Thread worker = new Thread((ThreadStart)delegate
			{
				Writer(c, rc);
			})
			{
				IsBackground = true,
				Name = "enviar-" + D.Id
			};
			worker.Start();
		}

		// Hilo "enviar-…": vacía la cola hacia la plataforma hasta que se cierra o falla la conexión.
		private void Writer(IConn c, RtmpClient rc)
		{
			while (true)
			{
				Item item;
				lock (lk)
				{
					while (items.Count == 0 && conn == c)
					{
						Monitor.Wait(lk, 300);
						// La plataforma cerró la conexión mientras no había nada que enviar.
						if (rc != null && !rc.Alive && conn == c)
						{
							break;
						}
					}
					if (conn != c)
					{
						break;
					}
					if (items.Count == 0)
					{
						Monitor.Exit(lk);
						try
						{
							Fail(c, rc.Error ?? "conexión cerrada");
							break;
						}
						finally
						{
							Monitor.Enter(lk);
						}
					}
					item = items.Dequeue();
					qBytes -= item.Data.Length;
				}
				try
				{
					c.Write(item.Type, item.Ts, item.Data);
					Interlocked.Add(ref sentBytes, item.Data.Length);
				}
				catch (Exception ex)
				{
					Fail(c, ex.Message);
					break;
				}
			}
		}

		// La conexión c ha fallado: se suelta y se programa el reintento.
		private void Fail(IConn c, string reason)
		{
			int waitMs;
			lock (lk)
			{
				if (conn != c)
				{
					return;
				}
				conn = null;
				items.Clear();
				qBytes = 0L;
				Reconnects++;
				// Si aguantó más de 15 s, se vuelve a empezar la cuenta de fallos.
				bool quickFail = e.Now - startedAt < 15000;
				fails = ((!quickFail) ? 1 : (fails + 1));
				waitMs = Math.Min(30000, 3000 << Math.Min(fails - 1, 4));
				nextStartAt = e.Now + waitMs;
				State = SinkState.Retrying;
				Note = "Reintento en " + waitMs / 1000 + " s";
				Tracks = 0;
				Monitor.PulseAll(lk);
			}
			Log("conexión perdida (" + Engine.Mask(reason, outKey) + "). Reintento en " + waitMs / 1000 + " s.");
			if (fails == 2)
			{
				Log("la plataforma rechaza la conexión repetidamente: revisa el servidor y la clave y que no haya otro programa emitiendo con la misma clave.");
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				c.Close(false);
			});
		}

		// Cierra la conexión. polite: espera hasta 2 s a que se envíe lo pendiente y cierra la emisión bien.
		public void Disconnect(bool polite)
		{
			IConn conn;
			RtmpClient connectingClient;
			lock (lk)
			{
				conn = this.conn;
				connectingClient = pending;
				if (polite && conn != null)
				{
					long deadline = e.Now + 2000;
					while (items.Count > 0 && this.conn == conn && e.Now < deadline)
					{
						Monitor.Wait(lk, 50);
					}
				}
				this.conn = null;
				items.Clear();
				qBytes = 0L;
				fresh = false;
				Tracks = 0;
				nextStartAt = 0L;
				fails = 0;
				if (!connecting)
				{
					State = (e.Running ? SinkState.Waiting : SinkState.Ready);
					Note = (e.Running ? "Esperando" : "");
				}
				Monitor.PulseAll(lk);
			}
			needKey = true;
			tsBase = -1L;
			lastRel = 0L;
			if (connectingClient != null)
			{
				connectingClient.Close();
			}
			if (conn != null)
			{
				conn.Close(polite);
			}
		}

		public void Stop()
		{
			stopping = true;
			Disconnect(true);
			lock (lk)
			{
				State = SinkState.Ready;
				Note = "";
			}
		}
	}
}
