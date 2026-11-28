// El motor del delay.
//
//   OBS ──RTMP──▶ RtmpServer/RtmpSession ──OnPacket──▶ cola (q) ──OutputLoop──▶ Sink (uno por destino)
//
// Cada paquete que llega de OBS se apunta con la hora de llegada y se guarda en la cola. El hilo
// "salida" (OutputLoop) saca cada paquete cuando han pasado los segundos de delay y lo reparte a
// todos los destinos. Las marcas de tiempo de salida se recalculan para que el público vea un
// vídeo continuo aunque cambie el delay:
//   - Al BAJAR el delay, los paquetes que ya "tocaban" salen de golpe: se salta hasta el último
//     fotograma clave para no mandar segundos de vídeo de una vez ("Saltados X s…").
//   - Al SUBIR el delay, la cola tarda más en vaciarse. Mientras tanto, si ReplayOnIncrease está
//     activado, se repiten los últimos segundos ya emitidos (historial); si no, la imagen se congela.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace StreamDelay
{
	internal class Engine
	{
		// Cuánto de lo emitido se guarda para poder repetirlo al subir el delay.
		private const long HistoryMs = 35000L;

		public string FfmpegPath = "ffmpeg.exe";

		public int InputPort = 1935;

		public bool AllowLan;

		public List<Destination> Destinations = new List<Destination>();

		public bool UseFfmpeg;

		public Action<string> Log = delegate
		{
		};

		private List<Sink> sinks = new List<Sink>();

		private volatile int targetMs = 30000;

		private volatile bool enabled = true;

		private volatile bool running;

		private volatile bool inputActive;

		private volatile bool cutRequested;

		// Delay real del último paquete enviado (lo que ve el público ahora mismo).
		private volatile int currentDelayMs;

		private volatile int inputTracks;

		private long queuedBytes;

		// Delay largo: de la cola, como mucho esto en memoria; lo demás va a un archivo temporal (Spool.cs).
		// Con 64 MB caben unos 80 s a 6000 kbps; un delay de 5 minutos ya no gasta 220 MB (o más, con
		// varias calidades) de memoria.
		public long MemoryBudget = 64L * 1024 * 1024;

		private long memBytes;

		private readonly Spool spool = new Spool();

		// Cola de paquetes esperando su hora de salida (protegida por sync).
		private readonly LinkedList<FlvPacket> q = new LinkedList<FlvPacket>();

		private readonly object sync = new object();

		private readonly object sessionLock = new object();

		private readonly Stopwatch clock = Stopwatch.StartNew();

		private RtmpServer server;

		// Conexión de OBS que se está usando (si OBS se reconecta, la nueva sustituye a la vieja).
		private RtmpSession current;

		// Al reconectarse OBS sus marcas de tiempo vuelven a 0: se les suma esto para que sigan creciendo.
		private long sessionOffset;

		private Thread outThread;

		private long lastInTs = -1L;

		// ---------- Calidades que llegan de OBS ----------

		private readonly HashSet<int> inVideoTracks = new HashSet<int>();

		// Altura (720, 1080…) de cada pista de vídeo.
		private readonly SortedDictionary<int, int> trackHeight = new SortedDictionary<int, int>();

		// Altura y fps que OBS dice en sus metadatos (onMetaData).
		private int metaHeight;

		private int metaFps;

		private volatile string qualities = "";

		private volatile int topHeight;

		private string lastQualityLog;

		public volatile bool LowQualityAlert;

		// Pista de vídeo de más resolución: la que reciben Kick, YouTube y Otro.
		public volatile int MainTrack;

		// Ya llegó el primer fotograma (antes, OBS solo ha mandado cabeceras).
		private bool framesSeen;

		// ---------- Salida ----------

		public volatile bool ReplayOnIncrease = true;

		public int Skips;

		// Lo que se resta a la marca de tiempo de entrada para obtener la de salida.
		private long shift;

		// Última marca de tiempo enviada a los destinos.
		private long lastOut = -1L;

		private long idleSince = -1L;

		// Tras un salto o un corte hay que esperar a un fotograma clave antes de enviar vídeo.
		private bool needKey = true;

		// Últimas cabeceras (secuencias de inicio) enviadas, por tipo y pista: un destino que se
		// conecta tarde las necesita antes del primer fotograma.
		private readonly Dictionary<int, FlvPacket> sentSeq = new Dictionary<int, FlvPacket>();

		private RtmpSession seqSession;

		private bool streaming;

		// ---------- Repetición al subir el delay ----------

		// Lo último emitido (HistoryMs), con su marca de tiempo de salida.
		private readonly LinkedList<FlvPacket> history = new LinkedList<FlvPacket>();

		private volatile bool replayRequested;

		private volatile bool abortReplay;

		// Tramo que se está repitiendo, por dónde va y cuándo empezó.
		private List<FlvPacket> seg;

		private int segIdx;

		private long segWall;

		private long segBaseOut;

		private long segOutStart;

		// lastOut cuando empezó la repetición (-1 si no se está repitiendo).
		private long replayL0 = -1L;

		// Los últimos 65 s emitidos, para los clips.
		public readonly ClipBuffer Clips = new ClipBuffer();

		public List<Sink> Sinks
		{
			get
			{
				return sinks;
			}
		}

		public long Now
		{
			get
			{
				return clock.ElapsedMilliseconds;
			}
		}

		// "1080p60 · 720p30" (vacío si OBS no está conectado).
		public string Qualities
		{
			get
			{
				if (!inputActive)
				{
					return "";
				}
				return qualities;
			}
		}

		public int TopHeight
		{
			get
			{
				if (!inputActive)
				{
					return 0;
				}
				return topHeight;
			}
		}

		// Delay elegido, en milisegundos.
		public int TargetMs
		{
			get
			{
				return targetMs;
			}
			set
			{
				int before = Effective;
				targetMs = Math.Max(0, value);
				OnDelayChanged(before);
			}
		}

		public bool Enabled
		{
			get
			{
				return enabled;
			}
			set
			{
				int before = Effective;
				enabled = value;
				OnDelayChanged(before);
			}
		}

		public bool Replaying
		{
			get
			{
				return replayL0 >= 0;
			}
		}

		// Delay que se aplica de verdad (0 si está desactivado).
		private int Effective
		{
			get
			{
				if (!enabled)
				{
					return 0;
				}
				return targetMs;
			}
		}

		public bool Running
		{
			get
			{
				return running;
			}
		}

		public bool InputActive
		{
			get
			{
				return inputActive;
			}
		}

		// Algún destino está en directo.
		public bool OutputActive
		{
			get
			{
				foreach (Sink sink in sinks)
				{
					if (sink.Live)
					{
						return true;
					}
				}
				return false;
			}
		}

		public int LiveCount
		{
			get
			{
				int count = 0;
				foreach (Sink sink in sinks)
				{
					if (sink.Live)
					{
						count++;
					}
				}
				return count;
			}
		}

		public long QueuedBytes
		{
			get
			{
				return Interlocked.Read(ref queuedBytes);
			}
		}

		// Bytes recibidos de OBS desde que se abrió el programa (el panel de rendimiento saca la velocidad).
		private long inBytes;

		public long InputBytes
		{
			get
			{
				return Interlocked.Read(ref inBytes);
			}
		}

		// Lo que de la cola está en el archivo temporal (no en memoria).
		public long SpooledBytes
		{
			get
			{
				return spool.Bytes;
			}
		}

		public int CurrentDelayMs
		{
			get
			{
				if (!OutputActive)
				{
					return 0;
				}
				return currentDelayMs;
			}
		}

		public int InputTracks
		{
			get
			{
				if (!inputActive)
				{
					return 0;
				}
				return inputTracks;
			}
		}

		public int OutputTracks
		{
			get
			{
				int max = 0;
				foreach (Sink sink in sinks)
				{
					if (sink.Live)
					{
						max = Math.Max(max, sink.Tracks);
					}
				}
				return max;
			}
		}

		// Al subir el delay se pide una repetición; al bajarlo se corta la que haya.
		private void OnDelayChanged(int old)
		{
			int now = Effective;
			if (now > old)
			{
				replayRequested = true;
			}
			else if (now < old)
			{
				abortReplay = true;
			}
			Wake();
		}

		// ---------- ffmpeg (modo clásico) ----------

		// Sitios donde puede estar ffmpeg.exe: junto al programa, en el PATH, donde lo deja winget o en C:\ffmpeg.
		public static List<string> FfmpegCandidates()
		{
			List<string> found = new List<string>();
			List<string> places = new List<string>();
			places.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"));
			foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
			{
				if (dir.Trim().Length > 0)
				{
					places.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe"));
				}
			}
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
			places.Add(Path.Combine(localAppData, "Microsoft\\WinGet\\Links\\ffmpeg.exe"));
			places.Add("C:\\ffmpeg\\bin\\ffmpeg.exe");
			try
			{
				string packages = Path.Combine(localAppData, "Microsoft\\WinGet\\Packages");
				if (Directory.Exists(packages))
				{
					foreach (string package in Directory.GetDirectories(packages, "Gyan.FFmpeg*"))
					{
						places.AddRange(Directory.GetFiles(package, "ffmpeg.exe", SearchOption.AllDirectories));
					}
				}
			}
			catch
			{
			}
			foreach (string c in places)
			{
				try
				{
					if (File.Exists(c) && !found.Exists((string f) => string.Equals(f, c, StringComparison.OrdinalIgnoreCase)))
					{
						found.Add(c);
					}
				}
				catch
				{
				}
			}
			return found;
		}

		// null si ese ffmpeg sirve; si no, el motivo.
		public static string CheckFfmpeg(string path, bool needTls)
		{
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo(path, "-hide_banner -protocols")
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardOutput = true,
					RedirectStandardError = true
				};
				using (Process process = Process.Start(startInfo))
				{
					process.ErrorDataReceived += delegate
					{
					};
					process.BeginErrorReadLine();
					string output = process.StandardOutput.ReadToEnd();
					process.WaitForExit(5000);
					// Solo interesan los protocolos de salida.
					int outputs = output.IndexOf("Output:", StringComparison.OrdinalIgnoreCase);
					string list = ((outputs >= 0) ? output.Substring(outputs) : output);
					HashSet<string> protocols = new HashSet<string>();
					foreach (string line in list.Split('\n'))
					{
						protocols.Add(line.Trim());
					}
					if (!protocols.Contains("rtmp"))
					{
						return "este ffmpeg no incluye RTMP";
					}
					if (needTls && !protocols.Contains("rtmps"))
					{
						return "este ffmpeg no incluye RTMPS (necesario para este servidor)";
					}
					return null;
				}
			}
			catch (Exception ex)
			{
				return "no se pudo ejecutar (" + ex.Message + ")";
			}
		}

		// ---------- Arrancar y parar ----------

		public void Start()
		{
			if (running)
			{
				return;
			}
			spool.Log = delegate(string m)
			{
				Log(m);
			};
			server = new RtmpServer
			{
				Log = delegate(string m)
				{
					Log(m);
				},
				PublishStart = OnPublishStart,
				Packet = OnPacket,
				PublishEnd = OnPublishEnd
			};
			server.Start(InputPort, AllowLan);
			List<Sink> list = new List<Sink>();
			foreach (Destination d in Destinations)
			{
				if (d.Enabled)
				{
					list.Add(new Sink(d, this)
					{
						State = SinkState.Waiting,
						Note = "Esperando"
					});
				}
			}
			sinks = list;
			running = true;
			ResetTimeline();
			outThread = new Thread(OutputLoop)
			{
				IsBackground = true,
				Name = "salida"
			};
			outThread.Start();
		}

		public void Stop()
		{
			running = false;
			if (server != null)
			{
				server.Stop();
			}
			Wake();
			if (outThread != null)
			{
				outThread.Join(4000);
			}
			CloseOutputs(true);
			foreach (Sink sink in sinks)
			{
				sink.Stop();
			}
			lock (sync)
			{
				q.Clear();
				queuedBytes = 0L;
				memBytes = 0L;
				spool.Reset();
			}
			inputActive = false;
			current = null;
		}

		// "Cortar ya": vacía la cola y desconecta los destinos.
		public void CutNow()
		{
			cutRequested = true;
			Wake();
		}

		// Despierta al hilo de salida (que espera en Monitor.Wait).
		private void Wake()
		{
			lock (sync)
			{
				Monitor.PulseAll(sync);
			}
		}

		// ---------- Entrada (OBS) ----------

		private void OnPublishStart(RtmpSession s)
		{
			RtmpSession previous;
			lock (sessionLock)
			{
				previous = current;
				current = s;
				// Continúa la línea de tiempo justo después del último paquete (un fotograma a 30 fps = 33 ms).
				sessionOffset = ((lastInTs >= 0) ? (lastInTs + 33) : 0);
				inVideoTracks.Clear();
				inputTracks = 0;
				lock (trackHeight)
				{
					trackHeight.Clear();
				}
				MainTrack = 0;
				framesSeen = false;
				metaHeight = (metaFps = 0);
				qualities = "";
				topHeight = 0;
				lastQualityLog = null;
			}
			if (previous != null && previous != s)
			{
				Log("Nueva conexión de OBS: sustituye a la anterior.");
				previous.Close();
			}
			inputActive = true;
			Log("OBS conectado (" + s.Remote + ")." + (IsObsKey(s.Name) ? " Usaré la clave que envía OBS." : ""));
		}

		private void OnPublishEnd(RtmpSession s)
		{
			lock (sessionLock)
			{
				if (current != s)
				{
					return;
				}
				current = null;
			}
			inputActive = false;
			Log("OBS desconectado.");
			Wake();
		}

		// Lee la cabecera FLV del paquete: si es cabecera de secuencia (Seq/SeqStart), fotograma clave
		// (Key) y, en la "Transmisión mejorada" (Enhanced RTMP), su subtipo y pista. false = paquete roto.
		public static bool Classify(FlvPacket pk)
		{
			byte[] data = pk.Data;
			int length = data.Length;
			pk.SubType = -1;
			if (pk.Type == 9)
			{
				// Vídeo.
				if (length < 2)
				{
					return false;
				}
				int header = data[0];
				int frameType = (header >> 4) & 7;
				if ((header & 0x80) != 0)
				{
					// Enhanced RTMP: los 4 bits bajos son el tipo de paquete.
					int packetType = header & 0xF;
					if (packetType == 6)
					{
						// Multipista: el tipo real va en el siguiente byte y la pista en el byte 6.
						if (length < 7)
						{
							return false;
						}
						int multitrackType = data[1] & 0xF0;
						packetType = data[1] & 0xF;
						pk.Track = ((multitrackType == 0) ? data[6] : 0);
					}
					pk.SubType = packetType;
					// 0 = cabecera de secuencia, 5 = metadatos, 4 = fin de secuencia.
					pk.SeqStart = packetType == 0 || packetType == 5;
					pk.Seq = pk.SeqStart || packetType == 4;
				}
				else
				{
					// FLV clásico: códec 7 (H.264), 12 (HEVC) o 13 (AV1) con byte 1 = 0 es cabecera.
					int codec = header & 0xF;
					pk.SeqStart = (pk.Seq = (codec == 7 || codec == 12 || codec == 13) && data[1] == 0);
					if (pk.Seq)
					{
						pk.SubType = 0;
					}
				}
				pk.Key = !pk.Seq && frameType == 1;
			}
			else if (pk.Type == 8)
			{
				// Audio.
				if (length < 1)
				{
					return false;
				}
				switch (data[0] >> 4)
				{
				case 10:
					// AAC: byte 1 = 0 es la cabecera.
					pk.SeqStart = (pk.Seq = length > 1 && data[1] == 0);
					break;
				case 9:
				{
					// Enhanced RTMP (audio).
					int packetType = data[0] & 0xF;
					if (packetType == 5)
					{
						// Multipista.
						if (length < 7)
						{
							return false;
						}
						int multitrackType = data[1] & 0xF0;
						packetType = data[1] & 0xF;
						pk.Track = ((multitrackType == 0) ? data[6] : 0);
					}
					pk.SubType = packetType;
					pk.SeqStart = packetType == 0 || packetType == 4;
					pk.Seq = pk.SeqStart;
					break;
				}
				}
			}
			else
			{
				// Datos (onMetaData…): se tratan como cabecera.
				pk.SeqStart = (pk.Seq = true);
			}
			return true;
		}

		// Llega un paquete de OBS: se clasifica, se apuntan las calidades y se mete en la cola.
		private void OnPacket(RtmpSession s, byte type, uint ts, byte[] data)
		{
			long offset;
			lock (sessionLock)
			{
				if (current != s)
				{
					return;
				}
				offset = sessionOffset;
			}
			FlvPacket pk = new FlvPacket
			{
				Type = type,
				Ts = ts + offset,
				Data = data,
				Arrival = clock.ElapsedMilliseconds,
				Src = s
			};
			Interlocked.Add(ref inBytes, data.Length);
			if (!Classify(pk))
			{
				return;
			}
			if (type == 9 && pk.SubType == 0)
			{
				// Cabecera de vídeo: una pista (calidad) nueva o su resolución.
				if (inVideoTracks.Add(pk.Track))
				{
					inputTracks = inVideoTracks.Count;
					if (inVideoTracks.Count == 2)
					{
						Log("Transmisión mejorada detectada: llegan varias calidades de OBS.");
					}
				}
				int height = VideoInfo.Height(pk);
				if (height > 0)
				{
					lock (trackHeight)
					{
						trackHeight[pk.Track] = height;
					}
				}
				if (framesSeen)
				{
					UpdateQualities();
				}
			}
			else if (type == 18)
			{
				ReadMeta(data);
				if (framesSeen)
				{
					UpdateQualities();
				}
			}
			else if (type == 9 && !pk.Seq && !framesSeen)
			{
				// OBS manda todas las cabeceras antes del primer fotograma: ahora ya se conocen todas las calidades.
				framesSeen = true;
				UpdateQualities();
			}
			if (pk.Ts > lastInTs)
			{
				lastInTs = pk.Ts;
			}
			lock (sync)
			{
				bool wasEmpty = q.Count == 0;
				q.AddLast(pk);
				queuedBytes += data.Length;
				memBytes += data.Length;
				// Por encima del límite, lo nuevo (lo que más tardará en salir) va al disco.
				if (memBytes > MemoryBudget && !pk.Seq && spool.TrySpill(pk))
				{
					memBytes -= pk.SpoolLen;
				}
				if (wasEmpty)
				{
					Monitor.PulseAll(sync);
				}
			}
		}

		// Altura y fps de onMetaData.
		private void ReadMeta(byte[] data)
		{
			try
			{
				AmfReader reader = new AmfReader(data);
				while (reader.More)
				{
					Dictionary<string, object> meta = reader.Read() as Dictionary<string, object>;
					if (meta != null)
					{
						object value;
						if (meta.TryGetValue("height", out value) && value is double)
						{
							metaHeight = (int)(double)value;
						}
						if (meta.TryGetValue("framerate", out value) && value is double)
						{
							metaFps = (int)Math.Round((double)value);
						}
					}
				}
			}
			catch
			{
			}
		}

		// "1080p60" de una pista (vacío si no se sabe).
		public string LabelFor(int track)
		{
			lock (trackHeight)
			{
				int h;
				if (!trackHeight.TryGetValue(track, out h))
				{
					h = ((track == 0) ? metaHeight : 0);
				}
				return (h > 0) ? VideoInfo.Label(h, (track == 0 || trackHeight.Count <= 1) ? metaFps : 0) : "";
			}
		}

		// Recalcula las calidades, elige la pista principal y avisa si no llega 1080p.
		private void UpdateQualities()
		{
			List<int> heights = new List<int>();
			int best = -1;
			int bestH = 0;
			lock (trackHeight)
			{
				foreach (KeyValuePair<int, int> item in trackHeight)
				{
					heights.Add(item.Value);
					if (item.Value > bestH)
					{
						bestH = item.Value;
						best = item.Key;
					}
				}
				foreach (int t in inVideoTracks)
				{
					if (!trackHeight.ContainsKey(t))
					{
						Log("Aviso: no se pudo leer la resolución de la pista de vídeo " + t + " (¿códec distinto de H.264?). Kick y YouTube reciben la mejor pista H.264.");
					}
				}
			}
			MainTrack = ((best < 0) ? 0 : best);
			if (heights.Count == 0 && metaHeight > 0)
			{
				heights.Add(metaHeight);
			}
			if (heights.Count == 0)
			{
				return;
			}
			heights.Sort();
			heights.Reverse();
			List<string> labels = new List<string>();
			for (int i = 0; i < heights.Count; i++)
			{
				labels.Add(VideoInfo.Label(heights[i], (i == 0) ? metaFps : 0));
			}
			qualities = string.Join(" · ", labels.ToArray());
			topHeight = heights[0];
			if (!(qualities == lastQualityLog))
			{
				lastQualityLog = qualities;
				Log("Calidad" + ((heights.Count > 1) ? "es" : "") + " que envía OBS: " + qualities + ((heights.Count > 1) ? (" (Twitch las recibe todas; Kick, YouTube y Otro, la de " + heights[0] + "p).") : "."));
				if (topHeight < 1080)
				{
					LowQualityAlert = true;
					Log("ATENCIÓN: OBS envía como máximo " + topHeight + "p, así que el público no tendrá 1080p. Arréglalo en OBS → Ajustes → Vídeo → Resolución de salida (escalada): 1920x1080" + ((inVideoTracks.Count > 1) ? ", y en Emisión pon \"Ancho de banda máximo\" en Auto." : "."));
				}
			}
		}

		// ---------- Claves ----------

		// ¿OBS mandó una clave de verdad? (con el servidor personalizado suele mandar "stream" o nada).
		public static bool IsObsKey(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			string key = name.Trim();
			if (IsEnhancedKey(key))
			{
				return true;
			}
			if (key.Length >= 8)
			{
				return !string.Equals(key, "stream", StringComparison.OrdinalIgnoreCase);
			}
			return false;
		}

		// Clave de la Transmisión mejorada de Twitch.
		public static bool IsEnhancedKey(string k)
		{
			if (!k.StartsWith("v1_", StringComparison.Ordinal))
			{
				return k.IndexOf("clientConfigId", StringComparison.OrdinalIgnoreCase) >= 0;
			}
			return true;
		}

		private static bool LooksTwitch(string k)
		{
			if (!IsEnhancedKey(k))
			{
				return k.StartsWith("live_", StringComparison.Ordinal);
			}
			return true;
		}

		// Clave para un destino: la guardada o, si conviene, la que envía OBS
		// (una clave de Twitch de OBS solo se usa para Twitch).
		public string KeyFor(Destination d, RtmpSession s)
		{
			string obsKey = ((s != null && s.Name != null) ? s.Name.Trim() : "");
			string savedKey = (d.Key ?? "").Trim();
			if (d.IsTwitch)
			{
				if (!IsObsKey(obsKey) || (!LooksTwitch(obsKey) && savedKey.Length != 0))
				{
					return savedKey;
				}
				return obsKey;
			}
			if (savedKey.Length > 0)
			{
				return savedKey;
			}
			if (!IsObsKey(obsKey) || LooksTwitch(obsKey))
			{
				return "";
			}
			return obsKey;
		}

		// ---------- Salida ----------

		private void ResetTimeline()
		{
			needKey = true;
			lastOut = -1L;
			shift = 0L;
			history.Clear();
			EndReplay();
			streaming = false;
		}

		// Hilo "salida": saca de la cola cada paquete cuando le toca y lo envía.
		private void OutputLoop()
		{
			while (running)
			{
				if (abortReplay)
				{
					abortReplay = false;
					if (replayL0 >= 0)
					{
						EndReplay();
						needKey = true;
						Log("Repetición interrumpida.");
					}
				}
				if (replayRequested)
				{
					replayRequested = false;
					if (ReplayOnIncrease && OutputActive && replayL0 < 0 && StartSegment())
					{
						Log("Repitiendo los últimos " + ((double)(history.Last.Value.OutTs - segBaseOut) / 1000.0).ToString("0.0") + " s mientras sube el delay.");
					}
				}
				// Mientras se repite, la cola espera (por eso el delay sube).
				if (replayL0 >= 0 && StepReplay())
				{
					continue;
				}
				FlvPacket next = null;
				List<FlvPacket> skippedHeaders = null;
				lock (sync)
				{
					if (cutRequested)
					{
						cutRequested = false;
						q.Clear();
						queuedBytes = 0L;
						memBytes = 0L;
						spool.Reset();
						// Desconectar puede tardar: se suelta el candado mientras tanto.
						Monitor.Exit(sync);
						try
						{
							CloseOutputs(false);
							Log("Búfer vaciado y salida cortada.");
						}
						finally
						{
							Monitor.Enter(sync);
						}
						continue;
					}
					long now = clock.ElapsedMilliseconds;
					if (q.Count == 0)
					{
						// Cola vacía: si OBS ya no está y han pasado 3 s, se termina la emisión.
						if (streaming && !inputActive)
						{
							if (idleSince < 0)
							{
								idleSince = now;
							}
							else if (now - idleSince > 3000)
							{
								Monitor.Exit(sync);
								try
								{
									CloseOutputs(true);
									Log("Transmisión terminada.");
								}
								finally
								{
									Monitor.Enter(sync);
								}
								idleSince = -1L;
								continue;
							}
						}
						Monitor.Wait(sync, 200);
						continue;
					}
					idleSince = -1L;
					long delay = Effective;
					long dueAt = q.First.Value.Arrival + delay;
					if (dueAt > now)
					{
						// Aún no le toca. Si falta poco (4 s), los destinos van conectándose.
						if (dueAt - now < 4000)
						{
							foreach (Sink sink in sinks)
							{
								sink.Prepare(q.First.Value.Src, now);
							}
						}
						Monitor.Wait(sync, (int)Math.Min(dueAt - now, 100L));
						continue;
					}
					if (now - dueAt > 1000)
					{
						// Vamos más de 1 s tarde (se ha bajado el delay): se salta hasta el último fotograma
						// clave que ya tocaba (si hay varios casi juntos, el primero de ellos).
						LinkedListNode<FlvPacket> jumpTo = null;
						long jumpTs = 0L;
						LinkedListNode<FlvPacket> node = q.First;
						while (node != null && node.Value.Arrival + delay <= now)
						{
							if (node.Value.Type == 9 && node.Value.Key && (jumpTo == null || node.Value.Ts - jumpTs > 100))
							{
								jumpTo = node;
								jumpTs = node.Value.Ts;
							}
							node = node.Next;
						}
						if (jumpTo != null && jumpTo != q.First)
						{
							// Se tiran los paquetes saltados, pero las cabeceras se guardan para enviarlas.
							skippedHeaders = new List<FlvPacket>();
							long fromTs = q.First.Value.Ts;
							while (q.First != jumpTo)
							{
								FlvPacket dropped = q.First.Value;
								q.RemoveFirst();
								queuedBytes -= dropped.Size;
								if (dropped.SpoolPos >= 0)
								{
									spool.Release(dropped);
								}
								else
								{
									memBytes -= dropped.Size;
								}
								if (dropped.Seq)
								{
									skippedHeaders.Add(dropped);
								}
							}
							needKey = true;
							Skips++;
							if (streaming)
							{
								Log("Saltados " + ((double)(jumpTo.Value.Ts - fromTs) / 1000.0).ToString("0.0") + " s para volver al delay elegido.");
							}
						}
					}
					next = q.First.Value;
					q.RemoveFirst();
					queuedBytes -= next.Size;
					if (next.SpoolPos < 0)
					{
						memBytes -= next.Size;
					}
				}
				// Si estaba en el disco, vuelve a la memoria justo antes de salir (fuera del candado: la
				// entrada de OBS no espera a la lectura).
				if (next.SpoolPos >= 0)
				{
					spool.Load(next);
				}
				if (skippedHeaders != null)
				{
					foreach (FlvPacket header in skippedHeaders)
					{
						Send(header);
					}
				}
				Send(next);
			}
		}

		// Calcula la marca de tiempo de salida del paquete y lo reparte.
		private void Send(FlvPacket p)
		{
			if (p.Src != seqSession)
			{
				// Otra conexión de OBS: sus cabeceras sustituyen a las anteriores.
				seqSession = p.Src;
				sentSeq.Clear();
			}
			long outTs;
			if (p.Seq)
			{
				outTs = Math.Max(lastOut, 0L);
				if (p.SeqStart)
				{
					// Clave: tipo (audio/vídeo), subtipo y pista.
					sentSeq[(p.Type << 16) | ((p.SubType & 0xFF) << 8) | p.Track] = p;
				}
			}
			else
			{
				if (needKey)
				{
					// Tras un salto, el vídeo empieza en un fotograma clave, pegado al último enviado.
					if (p.Type != 9 || !p.Key)
					{
						return;
					}
					shift = p.Ts - ((lastOut < 0) ? 0 : (lastOut + 33));
					needKey = false;
				}
				outTs = Math.Max(0L, p.Ts - shift);
			}
			Fanout(p, outTs);
			if (!p.Seq)
			{
				lastOut = Math.Max(lastOut, outTs);
				streaming = true;
				if (ReplayOnIncrease)
				{
					p.OutTs = outTs;
					history.AddLast(p);
					while (history.First.Value.OutTs < outTs - HistoryMs)
					{
						history.RemoveFirst();
					}
				}
				else if (history.Count > 0)
				{
					history.Clear();
				}
			}
			currentDelayMs = (int)(clock.ElapsedMilliseconds - p.Arrival);
			// Para Vortexs Clips: delay real en cada momento (ver DelayLog.cs).
			DelayLog.Record(currentDelayMs);
		}

		private void Fanout(FlvPacket p, long outTs)
		{
			long now = clock.ElapsedMilliseconds;
			foreach (Sink sink in sinks)
			{
				sink.Offer(p, outTs, sentSeq, now);
			}
			// La versión de una pista ya está en la cola de cada destino: no se guarda en el historial.
			p.Single = null;
			p.SingleDone = false;
			// Lo emitido también se guarda un rato para poder sacar clips.
			Clips.Add(p, outTs, MainTrack);
		}

		// ---------- Repetición ----------

		// Prepara un tramo para repetir: desde un fotograma clave del historial hasta el final, como
		// mucho tan largo como el tiempo que falta para que salga el siguiente paquete de la cola.
		private bool StartSegment()
		{
			long wait;
			lock (sync)
			{
				wait = ((q.Count == 0) ? Effective : (q.First.Value.Arrival + Effective - clock.ElapsedMilliseconds));
			}
			if (wait < 1500 || history.Count == 0)
			{
				return false;
			}
			long lastTs = history.Last.Value.OutTs;
			LinkedListNode<FlvPacket> node = history.First;
			while (node != null && (node.Value.Type != 9 || !node.Value.Key || node.Value.OutTs < lastTs - wait))
			{
				node = node.Next;
			}
			if (node == null || lastTs - node.Value.OutTs < 1000)
			{
				return false;
			}
			List<FlvPacket> list = new List<FlvPacket>();
			while (node != null)
			{
				list.Add(node.Value);
				node = node.Next;
			}
			if (replayL0 < 0)
			{
				replayL0 = lastOut;
			}
			seg = list;
			segIdx = 0;
			segBaseOut = list[0].OutTs;
			segOutStart = lastOut + 33;
			segWall = clock.ElapsedMilliseconds;
			return true;
		}

		// Envía el siguiente paquete de la repetición cuando le toca. Al acabar el tramo empieza otro
		// si aún queda espera; si no, termina y ajusta shift para seguir donde acabó la repetición.
		private bool StepReplay()
		{
			if (!OutputActive || cutRequested)
			{
				EndReplay();
				return false;
			}
			if (segIdx >= seg.Count)
			{
				if (StartSegment())
				{
					return true;
				}
				shift -= lastOut - replayL0;
				EndReplay();
				return false;
			}
			FlvPacket p = seg[segIdx];
			long wait = segWall + (p.OutTs - segBaseOut) - clock.ElapsedMilliseconds;
			if (wait > 0)
			{
				lock (sync)
				{
					Monitor.Wait(sync, (int)Math.Min(wait, 50L));
				}
				return true;
			}
			long outTs = segOutStart + (p.OutTs - segBaseOut);
			Fanout(p, outTs);
			lastOut = Math.Max(lastOut, outTs);
			segIdx++;
			return true;
		}

		private void EndReplay()
		{
			seg = null;
			replayL0 = -1L;
		}

		// Desconecta todos los destinos (a la vez si son varios). polite = cierre normal de la emisión.
		private void CloseOutputs(bool polite)
		{
			List<Sink> list = sinks;
			if (list.Count > 1)
			{
				List<Thread> threads = new List<Thread>();
				foreach (Sink item in list)
				{
					Sink kk = item;
					Thread worker = new Thread((ThreadStart)delegate
					{
						kk.Disconnect(polite);
					})
					{
						IsBackground = true
					};
					worker.Start();
					threads.Add(worker);
				}
				foreach (Thread worker in threads)
				{
					worker.Join(5000);
				}
			}
			else
			{
				foreach (Sink sink in list)
				{
					sink.Disconnect(polite);
				}
			}
			ResetTimeline();
		}

		// Quita la clave de un mensaje para el registro.
		public static string Mask(string m, string key)
		{
			if (string.IsNullOrEmpty(m) || string.IsNullOrEmpty(key))
			{
				return m;
			}
			string bare = key;
			int query = bare.IndexOf('?');
			if (query > 0)
			{
				bare = bare.Substring(0, query);
			}
			if (bare.Length < 4)
			{
				return m;
			}
			return m.Replace(key, "(clave)").Replace(bare, "(clave)");
		}
	}
}
