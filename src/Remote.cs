using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace StreamDelay
{
	// Mando a distancia (móvil, Stream Deck, panel de OBS), delays guardados,
	// avisos cuando algo falla y transición del overlay de delay.
	internal partial class MainForm
	{
		// ================= Delays guardados =================

		private readonly string[] presetName = new string[6] { "Sin delay", "", "", "", "", "" };

		private readonly int[] presetSec = new int[6] { 0, 15, 30, 60, 120, 180 };

		private string PresetLabel(int i)
		{
			if (presetName[i].Length > 0)
			{
				return presetName[i];
			}
			int s = presetSec[i];
			if (s <= 0)
			{
				return "Sin delay";
			}
			return (s >= 60 && s % 60 == 0) ? (s / 60 + " min") : (s + " s");
		}

		private void ApplyPreset(int i)
		{
			if (i < 0 || i >= presetSec.Length)
			{
				return;
			}
			int s = presetSec[i];
			if (s <= 0)
			{
				if (engine.Enabled)
				{
					SetEnabled(false);
				}
				return;
			}
			ApplyDelay(s);
			if (!engine.Enabled)
			{
				SetEnabled(true);
			}
		}

		private bool PresetActive(int i)
		{
			int s = presetSec[i];
			if (s <= 0)
			{
				return !engine.Enabled || engine.TargetMs == 0;
			}
			return engine.Enabled && engine.TargetMs / 1000 == s;
		}

		private void RefreshPresetButtons()
		{
			if (presets == null)
			{
				return;
			}
			for (int i = 0; i < presets.Length && i < presetSec.Length; i++)
			{
				string t = PresetLabel(i);
				bool sel = PresetActive(i);
				if (presets[i].Text != t || presets[i].Selected != sel)
				{
					presets[i].Text = t;
					presets[i].Selected = sel;
					presets[i].Invalidate();
				}
			}
			if (mandoPresetBox != null)
			{
				mandoPresetBox.Invalidate();
			}
		}

		private void PresetsDialog()
		{
			Form f = DarkForm("Delays guardados", 460, 470);
			Card c = new Card();
			c.Title = "Delays guardados";
			c.Subtitle = "Un clic los aplica (en Directo, en el móvil y en el Stream Deck).";
			Place(f, c, 20f, 18f, 420f, 380f);
			Label(c, "NOMBRE (OPCIONAL)", 20f, 56f, 240f);
			Label(c, "SEGUNDOS (0 = SIN DELAY)", 236f, 56f, 170f);
			Field[] fn = new Field[6];
			Field[] fs = new Field[6];
			for (int i = 0; i < 6; i++)
			{
				fn[i] = new Field();
				fn[i].Box.MaxLength = 12;
				fn[i].Box.Text = presetName[i];
				Place(c, fn[i], 20f, 78f + (float)i * 48f, 204f, 38f);
				fs[i] = new Field();
				fs[i].Box.MaxLength = 3;
				fs[i].Box.Text = presetSec[i].ToString(CultureInfo.InvariantCulture);
				Place(c, fs[i], 236f, 78f + (float)i * 48f, 164f, 38f);
			}
			VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "Guardar", FontSize = 10f };
			Place(f, ok, 20f, 410f, 420f, 44f);
			ok.Click += delegate
			{
				for (int i = 0; i < 6; i++)
				{
					presetName[i] = fn[i].Box.Text.Trim().Replace("|", " ").Replace(":", " ");
					int v;
					if (int.TryParse(fs[i].Box.Text.Trim(), out v))
					{
						presetSec[i] = Math.Max(0, Math.Min(MaxDelay, v));
					}
				}
				SaveConfig();
				RefreshPresetButtons();
				f.Close();
			};
			f.ShowDialog(this);
		}

		// ================= Transición del delay (para el overlay) =================

		private int trLastEff = -1;

		private string trKind = "";

		private DateTime trStart;

		private double trDur;

		private int trFrom;

		private int trTo;

		private int EffectiveSeconds()
		{
			return engine.Enabled ? (engine.TargetMs / 1000) : 0;
		}

		private void TrackTransition()
		{
			int eff = EffectiveSeconds();
			if (trLastEff < 0)
			{
				trLastEff = eff;
				return;
			}
			if (eff == trLastEff)
			{
				return;
			}
			if (engine.OutputActive)
			{
				trFrom = trLastEff;
				trTo = eff;
				trStart = DateTime.UtcNow;
				if (eff > trLastEff)
				{
					// Al subir, el público ve una repetición (o pausa) de lo que falta: dura esos segundos.
					trKind = (trLastEff == 0) ? "on" : "up";
					trDur = eff - trLastEff;
				}
				else if (eff == 0)
				{
					trKind = "off";
					trDur = 6.0;
				}
				else
				{
					trKind = "down";
					trDur = 4.0;
				}
			}
			trLastEff = eff;
		}

		private string TransitionJson()
		{
			if (trKind.Length == 0)
			{
				return null;
			}
			double el = (DateTime.UtcNow - trStart).TotalSeconds;
			if (el >= trDur || !engine.OutputActive)
			{
				trKind = "";
				return null;
			}
			double p = (trKind == "off" || trKind == "down") ? 1.0 : Math.Max(0.0, Math.Min(1.0, el / Math.Max(1.0, trDur)));
			return "{\"k\":\"" + trKind + "\",\"p\":" + p.ToString("0.###", CultureInfo.InvariantCulture) + ",\"from\":" + trFrom + ",\"to\":" + trTo + "}";
		}

		// ================= Avisos si algo falla =================

		private bool hlSound = true;

		private bool hlFlash = true;

		private bool hlSlow = true;

		private readonly Dictionary<string, SinkState> hPrev = new Dictionary<string, SinkState>();

		private readonly HashSet<string> hDown = new HashSet<string>();

		private readonly Dictionary<string, DateTime> hSlowSince = new Dictionary<string, DateTime>();

		private readonly HashSet<string> hSlow = new HashSet<string>();

		private bool hInput;

		private bool hInputLost;

		private readonly List<string> problems = new List<string>();

		private readonly List<string[]> hEvents = new List<string[]>();

		private int alertSeq;

		private DateTime lastBeep = DateTime.MinValue;

		private byte[] beepWav;

		private void Alert(string text, string level)
		{
			AppendLog((level == "bad") ? ("⚠ " + text) : text);
			lock (hEvents)
			{
				hEvents.Add(new string[3] { DateTime.Now.ToString("HH:mm:ss"), text, level });
				while (hEvents.Count > 12)
				{
					hEvents.RemoveAt(0);
				}
			}
			if (level != "bad")
			{
				return;
			}
			alertSeq++;
			if (hlSound && DateTime.UtcNow - lastBeep > TimeSpan.FromSeconds(4.0))
			{
				lastBeep = DateTime.UtcNow;
				Beep();
			}
			if (hlFlash)
			{
				FlashTaskbar();
			}
		}

		private void Beep()
		{
			try
			{
				if (beepWav == null)
				{
					beepWav = MakeBeep();
				}
				SoundPlayer sp = new SoundPlayer(new MemoryStream(beepWav));
				sp.Play();
			}
			catch
			{
				SystemSounds.Exclamation.Play();
			}
		}

		// Dos pitidos (agudo-grave) generados aquí mismo: no hace falta ningún archivo de sonido.
		private static byte[] MakeBeep()
		{
			int rate = 22050;
			double[][] notes = new double[4][]
			{
				new double[2] { 988.0, 0.16 },
				new double[2] { 0.0, 0.06 },
				new double[2] { 740.0, 0.22 },
				new double[2] { 0.0, 0.05 }
			};
			List<short> samples = new List<short>();
			foreach (double[] n in notes)
			{
				int count = (int)(rate * n[1]);
				for (int i = 0; i < count; i++)
				{
					double env = Math.Min(1.0, Math.Min(i / (rate * 0.01), (count - i) / (rate * 0.02)));
					double v = (n[0] > 0.0) ? (Math.Sin(2.0 * Math.PI * n[0] * i / rate) * 0.45 * env) : 0.0;
					samples.Add((short)(v * 32767.0));
				}
			}
			MemoryStream ms = new MemoryStream();
			BinaryWriter w = new BinaryWriter(ms);
			int dataLen = samples.Count * 2;
			w.Write(Encoding.ASCII.GetBytes("RIFF"));
			w.Write(36 + dataLen);
			w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
			w.Write(16);
			w.Write((short)1);
			w.Write((short)1);
			w.Write(rate);
			w.Write(rate * 2);
			w.Write((short)2);
			w.Write((short)16);
			w.Write(Encoding.ASCII.GetBytes("data"));
			w.Write(dataLen);
			foreach (short s in samples)
			{
				w.Write(s);
			}
			w.Flush();
			return ms.ToArray();
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct FLASHWINFO
		{
			public uint cbSize;

			public IntPtr hwnd;

			public uint dwFlags;

			public uint uCount;

			public uint dwTimeout;
		}

		[DllImport("user32.dll")]
		private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

		private void FlashTaskbar()
		{
			try
			{
				FLASHWINFO fi = default(FLASHWINFO);
				fi.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
				fi.hwnd = base.Handle;
				fi.dwFlags = 15u;
				fi.uCount = 0u;
				fi.dwTimeout = 0u;
				FlashWindowEx(ref fi);
			}
			catch
			{
			}
		}

		// Se llama cada medio segundo desde el temporizador de la ventana.
		private void CheckHealth()
		{
			TrackTransition();
			List<string> now = new List<string>();
			if (!engine.Running)
			{
				hPrev.Clear();
				hDown.Clear();
				hSlowSince.Clear();
				hSlow.Clear();
				hInput = false;
				hInputLost = false;
				SetProblems(now);
				return;
			}
			bool input = engine.InputActive;
			if (hInput && !input && engine.OutputActive)
			{
				hInputLost = true;
				Alert("OBS ha dejado de enviar vídeo (si has terminado el directo, no pasa nada).", "info");
			}
			else if (!hInput && input && hInputLost)
			{
				hInputLost = false;
				Alert("OBS vuelve a enviar vídeo.", "ok");
			}
			hInput = input;
			Sink[] sinks;
			try
			{
				sinks = AllSinks();
			}
			catch
			{
				sinks = new Sink[0];
			}
			foreach (Sink s in sinks)
			{
				string name = s.D.Name;
				SinkState st = s.State;
				SinkState prev;
				bool had = hPrev.TryGetValue(name, out prev);
				hPrev[name] = st;
				if (had && prev == SinkState.Live && st == SinkState.Retrying)
				{
					hDown.Add(name);
					Alert(name + " se ha caído. Reintentando solo…", "bad");
				}
				else if (st == SinkState.Live && hDown.Contains(name))
				{
					hDown.Remove(name);
					Alert(name + " vuelve a estar en directo.", "ok");
				}
				if (hDown.Contains(name))
				{
					now.Add(name + ": caído, reintentando (" + s.Note + ")");
				}
				// Subida lenta: la cola de envío no para de crecer.
				if (hlSlow && st == SinkState.Live && s.QueueAgeMs > 2500)
				{
					DateTime since;
					if (!hSlowSince.TryGetValue(name, out since))
					{
						hSlowSince[name] = DateTime.UtcNow;
					}
					else if (DateTime.UtcNow - since > TimeSpan.FromSeconds(5.0) && hSlow.Add(name))
					{
						Alert(name + " va con retraso: tu conexión no da para enviar todo (baja el bitrate o quita un destino).", "bad");
					}
				}
				else if (s.QueueAgeMs < 800)
				{
					hSlowSince.Remove(name);
					if (hSlow.Remove(name))
					{
						Alert(name + " ya va bien otra vez.", "ok");
					}
				}
				if (hSlow.Contains(name))
				{
					now.Add(name + ": la subida no da para todo");
				}
			}
			SetProblems(now);
		}

		private void SetProblems(List<string> now)
		{
			bool changed;
			lock (problems)
			{
				changed = now.Count != problems.Count || (now.Count > 0 && now[0] != problems[0]);
				problems.Clear();
				problems.AddRange(now);
			}
			if (changed && footer != null)
			{
				footer.Invalidate();
			}
		}

		private string FirstProblem()
		{
			lock (problems)
			{
				if (problems.Count == 0)
				{
					return null;
				}
				return problems[0] + ((problems.Count > 1) ? (" (+" + (problems.Count - 1) + ")") : "");
			}
		}

		// ================= Mando a distancia =================

		private string remoteKey = "";

		private bool remoteLan;

		private Field fldMandoUrl;

		private PaintBox mandoPresetBox;

		private PaintBox mandoHint;

		private static string NewRemoteKey()
		{
			const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
			byte[] b = new byte[10];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(b);
			}
			StringBuilder sb = new StringBuilder();
			foreach (byte x in b)
			{
				sb.Append(chars[x % chars.Length]);
			}
			return sb.ToString();
		}

		private void WireServer()
		{
			chatServer.Log = delegate(string m)
			{
				LogLater(m);
			};
			chatServer.OnInfo = ChatSendInfo;
			chatServer.OnSend = ChatSendAll;
			chatServer.OnEmotes = ChatEmotes;
			chatServer.OnDelayState = DelayStateJson;
			chatServer.OnViewersState = ViewersStateJson;
			chatServer.OnStyleState = delegate { return ovStyle.ToJson(); };
			chatServer.OnChatStyle = delegate { return chatStyle.ToJson(); };
			chatServer.OnRemote = RemoteCall;
			chatServer.RemoteKey = () => remoteKey;
			chatServer.OnMod = ModAction;
			chatServer.OnLicense = LicenseFromWeb;
		}

		// Vuelve a abrir el servidor (al permitir o quitar el acceso desde el móvil).
		private void RestartWeb()
		{
			int keep = chatServer.Running ? chatServer.Port : 8935;
			chatServer.Stop();
			WireServer();
			List<int> ports = new List<int> { keep };
			for (int i = 8935; i < 8945; i++)
			{
				if (i != keep)
				{
					ports.Add(i);
				}
			}
			foreach (int p in ports)
			{
				try
				{
					chatServer.Start(p, remoteLan);
					if (remoteLan)
					{
						AppendLog("Mando: acceso desde la red local activado (http://" + LanIp() + ":" + chatServer.Port + "/mando). Si Windows pregunta, permite el acceso en redes privadas.");
					}
					return;
				}
				catch
				{
				}
			}
			AppendLog("No se pudo abrir el servidor local: puertos 8935–8944 ocupados.");
		}

		private static string LanIp()
		{
			try
			{
				foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
				{
					if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
					{
						continue;
					}
					IPInterfaceProperties props = ni.GetIPProperties();
					if (props.GatewayAddresses.Count == 0)
					{
						continue;
					}
					foreach (UnicastIPAddressInformation a in props.UnicastAddresses)
					{
						if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
						{
							return a.Address.ToString();
						}
					}
				}
			}
			catch
			{
			}
			try
			{
				foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
				{
					if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && !ip.ToString().StartsWith("169.254."))
					{
						return ip.ToString();
					}
				}
			}
			catch
			{
			}
			return "IP-DE-ESTE-PC";
		}

		private string LocalMandoUrl()
		{
			return "http://127.0.0.1:" + chatServer.Port + "/mando?k=" + remoteKey;
		}

		private string LanUrl()
		{
			return "http://" + LanIp() + ":" + chatServer.Port + "/mando?k=" + remoteKey;
		}

		private string ActionUrl(string a)
		{
			return "http://127.0.0.1:" + chatServer.Port + "/mando/api?k=" + remoteKey + "&a=" + a;
		}

		private static string Js(string s)
		{
			return Web.ToJson(s ?? "");
		}

		private static string StateId(SinkState s)
		{
			switch (s)
			{
			case SinkState.Live:
				return "live";
			case SinkState.Retrying:
				return "retry";
			case SinkState.Connecting:
				return "connecting";
			case SinkState.Waiting:
				return "wait";
			case SinkState.NoKey:
				return "nokey";
			default:
				return "ready";
			}
		}

		private string RemoteStateJson(string msg)
		{
			StringBuilder sb = new StringBuilder();
			bool on = engine.Enabled && engine.TargetMs > 0;
			sb.Append("{\"ver\":").Append(Js(Version));
			sb.Append(",\"on\":").Append(on ? "true" : "false");
			sb.Append(",\"s\":").Append(engine.TargetMs / 1000);
			sb.Append(",\"cur\":").Append(engine.CurrentDelayMs / 1000);
			sb.Append(",\"running\":").Append(engine.Running ? "true" : "false");
			sb.Append(",\"input\":").Append(engine.InputActive ? "true" : "false");
			sb.Append(",\"live\":").Append(engine.OutputActive ? "true" : "false");
			sb.Append(",\"replay\":").Append(engine.Replaying ? "true" : "false");
			string tr = TransitionJson();
			if (tr != null)
			{
				sb.Append(",\"tr\":").Append(tr);
			}
			sb.Append(",\"presets\":[");
			for (int i = 0; i < presetSec.Length; i++)
			{
				if (i > 0)
				{
					sb.Append(',');
				}
				sb.Append("{\"n\":").Append(Js(I18n.Tr(PresetLabel(i)))).Append(",\"s\":").Append(presetSec[i]).Append(",\"sel\":").Append(PresetActive(i) ? "true" : "false").Append('}');
			}
			sb.Append("],\"dests\":[");
			bool first = true;
			if (engine.Running)
			{
				Sink[] sinks;
				try
				{
					sinks = AllSinks();
				}
				catch
				{
					sinks = new Sink[0];
				}
				foreach (Sink s in sinks)
				{
					if (!first)
					{
						sb.Append(',');
					}
					first = false;
					sb.Append("{\"name\":").Append(Js(s.D.Name)).Append(",\"st\":\"").Append(StateId(s.State)).Append("\",\"note\":").Append(Js(I18n.Tr(s.Note))).Append(",\"q\":").Append(Js(s.Quality)).Append('}');
				}
			}
			else
			{
				foreach (Destination d in EnabledDestinations())
				{
					if (!first)
					{
						sb.Append(',');
					}
					first = false;
					sb.Append("{\"name\":").Append(Js(d.Name)).Append(",\"st\":\"off\",\"note\":\"\",\"q\":\"\"}");
				}
			}
			sb.Append("],\"problems\":[");
			lock (problems)
			{
				for (int j = 0; j < problems.Count; j++)
				{
					if (j > 0)
					{
						sb.Append(',');
					}
					sb.Append(Js(I18n.Tr(problems[j])));
				}
			}
			sb.Append("],\"events\":[");
			lock (hEvents)
			{
				for (int k = hEvents.Count - 1; k >= 0; k--)
				{
					if (k < hEvents.Count - 1)
					{
						sb.Append(',');
					}
					sb.Append("{\"t\":").Append(Js(hEvents[k][0])).Append(",\"m\":").Append(Js(I18n.Tr(hEvents[k][1]))).Append(",\"lv\":").Append(Js(hEvents[k][2])).Append('}');
				}
			}
			sb.Append("],\"seq\":").Append(alertSeq);
			sb.Append(",\"lan\":").Append(remoteLan ? Js(LanUrl()) : "null");
			if (msg != null)
			{
				sb.Append(",\"msg\":").Append(Js(I18n.Tr(msg)));
			}
			sb.Append('}');
			return sb.ToString();
		}

		// Lo llama el servidor web (otro hilo): las acciones se hacen en el hilo de la ventana.
		private string RemoteCall(string a, Dictionary<string, string> q)
		{
			string result = null;
			Invoke((Action)delegate
			{
				result = RemoteAction(a, q);
			});
			return result;
		}

		private string RemoteAction(string a, Dictionary<string, string> q)
		{
			// El Mando es del plan Completo: sin él solo se puede ver el estado.
			if (plan != Plan.Full && a != "estado" && a != "state")
			{
				return RemoteStateJson("El Mando es del plan Completo de Vortexs Live");
			}
			string msg = null;
			string sv;
			int n;
			q.TryGetValue("s", out sv);
			bool hasS = int.TryParse(sv, out n);
			switch (a)
			{
			case "estado":
			case "state":
				break;
			case "toggle":
			case "alternar":
				SetEnabled(!engine.Enabled);
				msg = engine.Enabled ? "Delay activado" : "Delay quitado";
				break;
			case "on":
			case "activar":
				if (!engine.Enabled)
				{
					SetEnabled(true);
				}
				msg = "Delay activado";
				break;
			case "off":
			case "quitar":
			case "desactivar":
				if (engine.Enabled)
				{
					SetEnabled(false);
				}
				msg = "Delay quitado";
				break;
			case "mas":
			case "subir":
				ApplyDelay(engine.TargetMs / 1000 + (hasS ? Math.Abs(n) : 5));
				msg = "Delay: " + engine.TargetMs / 1000 + " s";
				break;
			case "menos":
			case "bajar":
				ApplyDelay(engine.TargetMs / 1000 - (hasS ? Math.Abs(n) : 5));
				msg = "Delay: " + engine.TargetMs / 1000 + " s";
				break;
			case "set":
			case "poner":
				if (!hasS)
				{
					return "{\"error\":\"falta s=segundos\"}";
				}
				ApplyDelay(n);
				msg = "Delay: " + engine.TargetMs / 1000 + " s";
				break;
			case "preset":
			case "guardado":
			{
				string nv;
				int idx;
				if (!q.TryGetValue("n", out nv) || !int.TryParse(nv, out idx) || idx < 1 || idx > presetSec.Length)
				{
					return "{\"error\":\"falta n=1…" + presetSec.Length + "\"}";
				}
				ApplyPreset(idx - 1);
				msg = PresetLabel(idx - 1);
				break;
			}
			case "cortar":
				if (!engine.OutputActive)
				{
					msg = "No estás emitiendo";
				}
				else
				{
					engine.CutNow();
					CutVertical();
					AppendLog("Mando: «Cortar ya» pulsado a distancia.");
					msg = "Transmisión cortada";
				}
				break;
			case "clip":
				msg = SaveClip("Mando", null) ? ("Guardando clip (últimos " + ClipSeconds + " s)…") : "Se acaba de guardar un clip";
				break;
			case "probar_aviso":
				Alert("Aviso de prueba desde el mando.", "bad");
				msg = "Aviso de prueba enviado";
				break;
			default:
				return "{\"error\":\"acción desconocida\"}";
			}
			if (msg != null && a != "estado")
			{
				RefreshStatus();
			}
			TrackTransition();
			return RemoteStateJson(msg);
		}

		// ================= Pestaña Mando =================

		private void BuildMando(int y0)
		{
			if (remoteKey.Length < 8)
			{
				remoteKey = NewRemoteKey();
				SaveConfig();
			}
			// --- Mando a distancia ---
			Card c1 = new Card();
			c1.Title = "Mando a distancia";
			c1.Subtitle = "El delay desde el móvil, una tablet o como panel en OBS.";
			Place(this, c1, 20f, y0, 420f, 204f, 6);
			VButton newKey = new VButton { Kind = ButtonKind.Link, Text = "Nueva clave →", FontSize = 8.5f };
			Place(c1, newKey, 280f, 13f, 120f, 24f);
			newKey.Click += delegate
			{
				if (I18n.Show(this, "Se crea una clave nueva: las URL que tengas copiadas (móvil, panel de OBS, Stream Deck) dejarán de funcionar y tendrás que copiarlas otra vez. ¿Seguir?", "Nueva clave", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					remoteKey = NewRemoteKey();
					SaveConfig();
					UpdateMandoUrl();
					AppendLog("Mando: clave nueva creada. Copia de nuevo las URL del mando.");
				}
			};
			Toggle tLan = new Toggle { Text = "Permitir desde el móvil (misma wifi)", Checked = remoteLan };
			Place(c1, tLan, 18f, 54f, 384f, 28f);
			fldMandoUrl = new Field { ReadOnly = true };
			Place(c1, fldMandoUrl, 20f, 90f, 292f, 36f);
			VButton copy = new VButton { Kind = ButtonKind.Ghost, Text = "Copiar", FontSize = 9f };
			Place(c1, copy, 320f, 90f, 80f, 36f);
			copy.Click += delegate
			{
				if (EnsureChatServer())
				{
					CopyText(fldMandoUrl.Box.Text, copy, "Copiar");
				}
			};
			VButton qr = new VButton { Kind = ButtonKind.Primary, Text = "Código QR", FontSize = 9f };
			Place(c1, qr, 20f, 136f, 124f, 36f);
			qr.Click += delegate
			{
				if (EnsureChatServer())
				{
					Http.OpenBrowser(LocalMandoUrl() + "&qr=1");
				}
			};
			VButton open = new VButton { Kind = ButtonKind.Ghost, Text = "Abrir aquí", FontSize = 9f };
			Place(c1, open, 150f, 136f, 120f, 36f);
			open.Click += delegate
			{
				if (EnsureChatServer())
				{
					Http.OpenBrowser(LocalMandoUrl());
				}
			};
			VButton deck = new VButton { Kind = ButtonKind.Ghost, Text = "Stream Deck", FontSize = 9f };
			Place(c1, deck, 276f, 136f, 124f, 36f);
			deck.Click += delegate
			{
				if (EnsureChatServer())
				{
					StreamDeckDialog();
				}
			};
			mandoHint = Box(c1, 20f, 178f, 380f, 18f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, remoteLan ? "En el móvil: abre esa URL o escanea el QR. Conectado a la misma wifi que el PC." : "Ahora solo desde este PC (panel de OBS: Docks → Docks de navegador).", Theme.Body(7.25f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});
			tLan.CheckedChanged += delegate
			{
				remoteLan = tLan.Checked;
				SaveConfig();
				RestartWeb();
				UpdateMandoUrl();
			};

			// --- Delays guardados ---
			int y2 = y0 + 212;
			Card c2 = new Card();
			c2.Title = "Delays guardados";
			c2.Subtitle = "Un clic en Directo los aplica. 0 s = quitar el delay.";
			Place(this, c2, 20f, y2, 420f, 106f, 6);
			VButton edit = new VButton { Kind = ButtonKind.Link, Text = "Editar →", FontSize = 8.5f };
			Place(c2, edit, 300f, 13f, 100f, 24f);
			edit.Click += delegate
			{
				PresetsDialog();
			};
			mandoPresetBox = Box(c2, 20f, 60f, 380f, 30f, delegate(Graphics g, Rectangle r)
			{
				float w = (r.Width - S(5f) * 5) / 6f;
				for (int i = 0; i < 6; i++)
				{
					RectangleF b = new RectangleF(r.X + i * (w + S(5f)), r.Y + S(2f), w, r.Height - S(4f));
					using (System.Drawing.Drawing2D.GraphicsPath gp = Theme.Round(b, b.Height / 2f))
					{
						using (Pen pen = new Pen(PresetActive(i) ? Theme.Violet : Theme.Border))
						{
							g.DrawPath(pen, gp);
						}
					}
					Theme.DrawText(g, PresetLabel(i), Theme.BodySemi(7.25f), PresetActive(i) ? Theme.Text : Theme.Muted, b, StringAlignment.Center, StringAlignment.Center);
				}
			});
			mandoPresetBox.Cursor = Cursors.Hand;
			mandoPresetBox.Click += delegate
			{
				PresetsDialog();
			};

			// --- Avisos si algo falla ---
			int y3 = y2 + 114;
			Card c3 = new Card();
			c3.Title = "Avisos si algo falla";
			c3.Subtitle = "Si se cae una plataforma o tu subida no da para todo.";
			Place(this, c3, 20f, y3, 420f, 160f, 6);
			Toggle tS = new Toggle { Text = "Sonar un aviso", Checked = hlSound };
			Place(c3, tS, 18f, 54f, 190f, 28f);
			tS.CheckedChanged += delegate { hlSound = tS.Checked; SaveConfig(); };
			Toggle tF = new Toggle { Text = "Parpadear en la barra", Checked = hlFlash };
			Place(c3, tF, 214f, 54f, 190f, 28f);
			tF.CheckedChanged += delegate { hlFlash = tF.Checked; SaveConfig(); };
			Toggle tSl = new Toggle { Text = "Avisar si la subida no da para todo", Checked = hlSlow };
			Place(c3, tSl, 18f, 84f, 384f, 28f);
			tSl.CheckedChanged += delegate { hlSlow = tSl.Checked; SaveConfig(); };
			VButton test = new VButton { Kind = ButtonKind.Ghost, Text = "Probar aviso", FontSize = 9f };
			Place(c3, test, 20f, 116f, 380f, 32f);
			test.Click += delegate
			{
				lastBeep = DateTime.MinValue;
				Alert("Aviso de prueba: así suena y se ve si se cae una plataforma.", "bad");
			};
			Box(this, 22f, y3 + 164, 416f, 18f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "Los avisos también salen en el mando del móvil y en Directo (abajo, en rojo).", Theme.Body(7.25f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			}, 6);
			UpdateMandoUrl();
		}

		private void UpdateMandoUrl()
		{
			if (fldMandoUrl == null)
			{
				return;
			}
			fldMandoUrl.Box.Text = chatServer.Running ? (remoteLan ? LanUrl() : LocalMandoUrl()) : I18n.Tr("(se abre al iniciar Vortexs Live)");
			if (mandoHint != null)
			{
				mandoHint.Invalidate();
			}
		}

		// Ventana «Stream Deck»: cada acción con su atajo de teclado (para la acción «Tecla de acceso
		// rápido», que no abre nada) y su URL (para «Sitio web», que abre el navegador si no se marca
		// «Acceder en segundo plano»).
		private void StreamDeckDialog()
		{
			string[][] rows = new string[13][]
			{
				new string[2] { "Activar / quitar delay", "toggle" },
				new string[2] { "Activar delay", "on" },
				new string[2] { "Quitar delay", "off" },
				new string[2] { "Subir 5 s", "mas" },
				new string[2] { "Bajar 5 s", "menos" },
				new string[2] { "Delay guardado 1 (" + PresetLabel(0) + ")", "preset&n=1" },
				new string[2] { "Delay guardado 2 (" + PresetLabel(1) + ")", "preset&n=2" },
				new string[2] { "Delay guardado 3 (" + PresetLabel(2) + ")", "preset&n=3" },
				new string[2] { "Delay guardado 4 (" + PresetLabel(3) + ")", "preset&n=4" },
				new string[2] { "Delay guardado 5 (" + PresetLabel(4) + ")", "preset&n=5" },
				new string[2] { "Delay guardado 6 (" + PresetLabel(5) + ")", "preset&n=6" },
				new string[2] { "Guardar clip (últimos 60 s)", "clip" },
				new string[2] { "Cortar ya (¡sin confirmación!)", "cortar" }
			};
			float rowH = 36f;
			float top = 150f;
			Form f = DarkForm("Stream Deck", 560, (int)(top + (float)rows.Length * rowH + 74f));
			Box(f, 22f, 14f, 516f, 128f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "RECOMENDADO · NO ABRE NADA", Theme.Mono(7.5f), Theme.Violet, new RectangleF(r.X, r.Y, r.Width, S(16f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawWrapped(g, "Arrastra la acción «Tecla de acceso rápido» (Sistema) a un botón y pulsa el atajo de la lista. Funciona aunque estés en el juego.", Theme.Body(8.5f), Theme.Text, new RectangleF(r.X, r.Y + S(18f), r.Width, S(40f)));
				Theme.DrawText(g, "CON URL", Theme.Mono(7.5f), Theme.Muted, new RectangleF(r.X, r.Y + S(64f), r.Width, S(16f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawWrapped(g, "Acción «Sitio web»: pega la URL y marca «Acceder en segundo plano» (GET request in background). Si no la marcas, se abre una pestaña del navegador cada vez.", Theme.Body(8.5f), Theme.Muted, new RectangleF(r.X, r.Y + S(82f), r.Width, S(44f)));
			});
			for (int i = 0; i < rows.Length; i++)
			{
				string url = ActionUrl(rows[i][1]);
				string label = rows[i][0];
				string keys = HotkeyFor(rows[i][1]);
				float y = top + (float)i * rowH;
				Box(f, 22f, y, 250f, 30f, delegate(Graphics g, Rectangle r)
				{
					Theme.DrawText(g, label, Theme.BodySemi(8.5f), Theme.Text, r, StringAlignment.Near, StringAlignment.Center);
				});
				VButton k = new VButton { Kind = ButtonKind.Pill, Text = keys, FontSize = 7.5f, Selected = true };
				Place(f, k, 276f, y + 2f, 150f, 26f);
				k.Click += delegate
				{
					CopyText(keys, k, keys);
				};
				VButton b = new VButton { Kind = ButtonKind.Ghost, Text = "Copiar URL", FontSize = 8f };
				Place(f, b, 434f, y, 104f, 30f);
				b.Click += delegate
				{
					CopyText(url, b, "Copiar URL");
				};
			}
			VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "Cerrar", FontSize = 10f };
			Place(f, ok, 20f, top + (float)rows.Length * rowH + 14f, 520f, 44f);
			ok.Click += delegate
			{
				f.Close();
			};
			f.ShowDialog(this);
		}

		// ================= Configuración =================

		private void LoadRemoteConfig(Dictionary<string, string> raw)
		{
			string v;
			if (raw.TryGetValue("remote_key", out v) && v.Length >= 8)
			{
				remoteKey = v.Trim().ToLowerInvariant();
			}
			if (remoteKey.Length < 8)
			{
				remoteKey = NewRemoteKey();
			}
			if (raw.TryGetValue("remote_lan", out v))
			{
				remoteLan = v == "1";
			}
			if (raw.TryGetValue("presets", out v) && v.Length > 0)
			{
				string[] parts = v.Split('|');
				for (int i = 0; i < parts.Length && i < presetSec.Length; i++)
				{
					int c = parts[i].LastIndexOf(':');
					int s;
					if (c >= 0 && int.TryParse(parts[i].Substring(c + 1), out s))
					{
						presetName[i] = parts[i].Substring(0, c);
						presetSec[i] = Math.Max(0, Math.Min(MaxDelay, s));
					}
				}
			}
			if (raw.TryGetValue("hl_sound", out v))
			{
				hlSound = v != "0";
			}
			if (raw.TryGetValue("hl_flash", out v))
			{
				hlFlash = v != "0";
			}
			if (raw.TryGetValue("hl_slow", out v))
			{
				hlSlow = v != "0";
			}
		}

		private void SaveRemoteConfig(List<string> lines)
		{
			if (remoteKey.Length < 8)
			{
				remoteKey = NewRemoteKey();
			}
			lines.Add("remote_key=" + remoteKey);
			lines.Add("remote_lan=" + (remoteLan ? "1" : "0"));
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < presetSec.Length; i++)
			{
				if (i > 0)
				{
					sb.Append('|');
				}
				sb.Append(presetName[i].Replace("|", " ").Replace(":", " ")).Append(':').Append(presetSec[i]);
			}
			lines.Add("presets=" + sb);
			lines.Add("hl_sound=" + (hlSound ? "1" : "0"));
			lines.Add("hl_flash=" + (hlFlash ? "1" : "0"));
			lines.Add("hl_slow=" + (hlSlow ? "1" : "0"));
		}
	}
}
