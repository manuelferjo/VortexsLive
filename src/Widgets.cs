// Overlays de la 4.19: alertas, mensaje destacado, barra de objetivo, cinta de últimos eventos y
// encuesta. Todos van en una sola página (res\widgets.html) que se sirve en /alertas, /destacado,
// /objetivo, /eventos y /encuesta. Los ajustes y el estado se leen en /widgets.json (cada 2 s) y
// lo que pasa al momento llega por /events (t: "ev", "alert", "feature", "goal", "poll").
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private readonly object wgLock = new object();

		// ---- Alertas ----
		// Tipos: follow, sub (subs, regalos, miembros), raid, bits, reward (canjes), tip (donaciones,
		// Super Chats, Kicks y regalos de TikTok).
		private static readonly string[] AlertKinds = new string[6] { "follow", "sub", "raid", "bits", "reward", "tip" };
		private readonly bool[] alertOn = new bool[6] { true, true, true, true, true, true };
		private int alertDur = 6;
		private bool alertSound = true;
		private string alertPos = "top";
		private int alertVolume = 80;    // %

		// Sonidos propios: se copian a la carpeta "sonidos" junto a Vortexs Live con el nombre del tipo
		// (follow.mp3, sub.wav…). Sin archivo, suena el sonido de siempre.
		private static readonly string[] SoundExts = new string[4] { ".mp3", ".wav", ".ogg", ".m4a" };

		private static string SoundDir
		{
			get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sonidos"); }
		}

		private string AlertSoundFile(string kind)
		{
			if (Array.IndexOf(AlertKinds, kind) < 0)
			{
				return null;
			}
			foreach (string ext in SoundExts)
			{
				string f = Path.Combine(SoundDir, kind + ext);
				if (File.Exists(f))
				{
					return f;
				}
			}
			return null;
		}

		// ---- Mensaje destacado ----
		private string featPos = "bl";
		private int featHide;            // segundos; 0 = hasta quitarlo
		private Dictionary<string, object> featItem;
		private System.Threading.Timer featTimer;

		// ---- Objetivo ----
		private string goalType = "follows";   // follows · subs · bits · manual
		private string goalTitle = "Objetivo de follows";
		private int goalTarget = 50;
		private int goalCount;
		private string goalPos = "top";
		private DateTime goalSavedAt = DateTime.MinValue;

		// ---- Últimos eventos ----
		private int tickN = 10;
		private string tickSpeed = "normal";
		private string tickPos = "bottom";

		// ---- Encuesta ----
		private string pollPos = "left";
		private string pollQ = "";
		private List<string> pollOpts = new List<string>();
		private int[] pollVotes = new int[0];
		private readonly Dictionary<string, int> pollVoters = new Dictionary<string, int>();
		private bool pollOpen;
		private bool pollShown = true;
		private long pollEnds;           // ms Unix; 0 = sin límite
		private bool pollAnnounce = true;
		private System.Threading.Timer pollTimer;
		private DateTime pollSentAt = DateTime.MinValue;
		private bool pollPending;

		private static long NowMs()
		{
			return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
		}

		// ---------- Lo que leen los overlays ----------

		private string WidgetsJson()
		{
			Dictionary<string, object> d = new Dictionary<string, object>();
			lock (wgLock)
			{
				List<object> kinds = new List<object>();
				for (int i = 0; i < AlertKinds.Length; i++)
				{
					if (alertOn[i])
					{
						kinds.Add(AlertKinds[i]);
					}
				}
				Dictionary<string, object> sounds = new Dictionary<string, object>();
				foreach (string k in AlertKinds)
				{
					string f = AlertSoundFile(k);
					if (f != null)
					{
						// La fecha en la URL: si cambias el archivo, OBS no usa el viejo.
						sounds[k] = "/sonido/" + k + "?v=" + File.GetLastWriteTimeUtc(f).Ticks;
					}
				}
				d["alerts"] = new Dictionary<string, object> { { "kinds", kinds }, { "dur", alertDur }, { "sound", alertSound }, { "pos", alertPos }, { "sounds", sounds }, { "vol", alertVolume } };
				d["feature"] = new Dictionary<string, object> { { "pos", featPos }, { "item", featItem } };
				d["goal"] = GoalState();
				d["ticker"] = new Dictionary<string, object> { { "n", tickN }, { "speed", tickSpeed }, { "pos", tickPos } };
				d["poll"] = PollState();
				d["pollPos"] = pollPos;
			}
			return Web.ToJson(d);
		}

		private Dictionary<string, object> GoalState()
		{
			return new Dictionary<string, object>
			{
				{ "type", goalType }, { "title", goalTitle }, { "target", goalTarget }, { "count", goalCount }, { "pos", goalPos }
			};
		}

		private Dictionary<string, object> PollState()
		{
			if (pollOpts.Count == 0)
			{
				return null;
			}
			List<object> opts = new List<object>();
			for (int i = 0; i < pollOpts.Count; i++)
			{
				opts.Add(new Dictionary<string, object> { { "t", pollOpts[i] }, { "n", pollVotes[i] } });
			}
			return new Dictionary<string, object>
			{
				{ "q", pollQ }, { "opts", opts }, { "open", pollOpen }, { "shown", pollShown }, { "ends", pollEnds }, { "now", NowMs() }, { "pos", pollPos }
			};
		}

		private void WidgetSend(string t, Dictionary<string, object> data)
		{
			try
			{
				if (chatServer != null && chatServer.Running)
				{
					chatServer.Send(new ChatItem { t = t, data = data });
				}
			}
			catch
			{
			}
		}

		private void GoalBroadcast()
		{
			Dictionary<string, object> s;
			lock (wgLock)
			{
				s = GoalState();
			}
			WidgetSend("goal", s);
		}

		// ---------- Cada mensaje o evento del chat (hilo del chat) ----------

		private void WidgetsOnItem(ChatItem it)
		{
			if (it == null)
			{
				return;
			}
			try
			{
				if (it.t == "ev")
				{
					GoalCount(it);
				}
				else if (it.t == "msg")
				{
					PollVote(it);
				}
			}
			catch
			{
			}
		}

		private static int LeadingInt(string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return 0;
			}
			StringBuilder sb = new StringBuilder();
			foreach (char c in s)
			{
				if (char.IsDigit(c))
				{
					sb.Append(c);
				}
				else if (sb.Length > 0 && c != '.' && c != ',')
				{
					break;
				}
			}
			int n;
			return int.TryParse(sb.ToString(), out n) ? n : 0;
		}

		private void GoalCount(ChatItem it)
		{
			int add = 0;
			string k = it.kind ?? "";
			lock (wgLock)
			{
				if (goalType == "follows" && k == "follow")
				{
					add = 1;
				}
				else if (goalType == "subs")
				{
					if (k == "sub" || k == "resub" || k == "gift" || k == "member")
					{
						add = 1;
					}
					else if (k == "giftbomb" || k == "giftmember")
					{
						add = Math.Max(1, LeadingInt(it.amount));
					}
				}
				else if (goalType == "bits" && k == "bits")
				{
					add = LeadingInt(it.amount);
				}
				if (add <= 0)
				{
					return;
				}
				goalCount += add;
			}
			GoalBroadcast();
			// Se guarda (como mucho cada 10 s) para que no se pierda al cerrar.
			if (DateTime.UtcNow - goalSavedAt > TimeSpan.FromSeconds(10.0))
			{
				goalSavedAt = DateTime.UtcNow;
				try
				{
					BeginInvoke((Action)SaveConfig);
				}
				catch
				{
				}
			}
		}

		// ---------- Encuesta ----------

		// Vale "1", "#1", "!1" o "!votar 1".
		private int VoteNumber(string text)
		{
			string s = (text ?? "").Trim().ToLowerInvariant();
			if (s.StartsWith("!votar") || s.StartsWith("!vote"))
			{
				s = s.Substring(s.IndexOf(' ') < 0 ? s.Length : s.IndexOf(' ')).Trim();
			}
			s = s.TrimStart('#', '!');
			int n;
			if (s.Length > 0 && s.Length <= 2 && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n))
			{
				return n;
			}
			return 0;
		}

		private void PollVote(ChatItem it)
		{
			int n = VoteNumber(PlainText(it));
			if (n <= 0)
			{
				return;
			}
			lock (wgLock)
			{
				if (!pollOpen || n > pollOpts.Count)
				{
					return;
				}
				if (pollEnds > 0 && NowMs() > pollEnds)
				{
					return;
				}
				string who = (it.p ?? "") + ":" + (string.IsNullOrEmpty(it.uid) ? (it.user ?? "").ToLowerInvariant() : it.uid);
				int prev;
				if (pollVoters.TryGetValue(who, out prev))
				{
					if (prev == n - 1)
					{
						return;
					}
					pollVotes[prev]--;
				}
				pollVoters[who] = n - 1;
				pollVotes[n - 1]++;
			}
			PollBroadcastSoon();
		}

		// Como mucho 4 veces por segundo, para no saturar las páginas si vota mucha gente a la vez.
		private void PollBroadcastSoon()
		{
			lock (wgLock)
			{
				if (pollPending)
				{
					return;
				}
				double ms = (DateTime.UtcNow - pollSentAt).TotalMilliseconds;
				if (ms >= 250.0)
				{
					pollSentAt = DateTime.UtcNow;
				}
				else
				{
					pollPending = true;
					ThreadPool.QueueUserWorkItem(delegate
					{
						Thread.Sleep(260);
						lock (wgLock)
						{
							pollPending = false;
							pollSentAt = DateTime.UtcNow;
						}
						PollBroadcast();
					});
					return;
				}
			}
			PollBroadcast();
		}

		private void PollBroadcast()
		{
			Dictionary<string, object> s;
			lock (wgLock)
			{
				s = PollState();
			}
			WidgetSend("poll", s);
		}

		private void PollEnd(bool announce)
		{
			string result = null;
			lock (wgLock)
			{
				if (!pollOpen)
				{
					return;
				}
				pollOpen = false;
				pollEnds = 0;
				if (pollTimer != null)
				{
					pollTimer.Dispose();
					pollTimer = null;
				}
				int total = 0, best = -1;
				for (int i = 0; i < pollVotes.Length; i++)
				{
					total += pollVotes[i];
					if (best < 0 || pollVotes[i] > pollVotes[best])
					{
						best = i;
					}
				}
				if (announce && pollAnnounce && total > 0)
				{
					bool tie = false;
					for (int i = 0; i < pollVotes.Length; i++)
					{
						if (i != best && pollVotes[i] == pollVotes[best])
						{
							tie = true;
						}
					}
					result = tie ? I18n.Tr("📊 Encuesta terminada: empate. ") + total + I18n.Tr(" votos.")
						: I18n.Tr("📊 Ha ganado: ") + pollOpts[best] + " (" + (pollVotes[best] * 100 / total) + " %, " + total + I18n.Tr(" votos).");
				}
			}
			LogLater("Encuesta terminada.");
			Toast(result != null ? result.Replace("📊 ", "") : I18n.Tr("Encuesta terminada."), "info");
			PollBroadcast();
			if (result != null)
			{
				PollSay(result);
			}
		}

		// Escribe en los chats conectados (Twitch, Kick y YouTube).
		private void PollSay(string text)
		{
			List<string> to = new List<string>();
			if (twitch.Connected) to.Add("twitch");
			if (kick.Connected) to.Add("kick");
			if (youtube.Connected) to.Add("youtube");
			if (to.Count == 0)
			{
				return;
			}
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					ChatSendAll(text, to.ToArray());
				}
				catch (Exception ex)
				{
					LogLater("Encuesta: no se pudo avisar en el chat (" + ex.Message + ").");
				}
			});
		}

		// ---------- Acciones desde la página del chat ----------

		private Dictionary<string, object> WidgetMod(Dictionary<string, object> d, string a)
		{
			Dictionary<string, object> res = new Dictionary<string, object>();
			if (a == "feature")
			{
				Dictionary<string, object> item = J.At(d, "item") as Dictionary<string, object>;
				if (item == null)
				{
					res["error"] = "falta el mensaje";
					return res;
				}
				// Solo lo que hace falta para pintarlo.
				Dictionary<string, object> keep = new Dictionary<string, object>();
				foreach (string k in new string[] { "p", "id", "uid", "user", "color", "roles", "bd", "parts" })
				{
					object v;
					if (item.TryGetValue(k, out v))
					{
						keep[k] = v;
					}
				}
				lock (wgLock)
				{
					featItem = keep;
					if (featTimer != null)
					{
						featTimer.Dispose();
						featTimer = null;
					}
					if (featHide > 0)
					{
						featTimer = new System.Threading.Timer(delegate { Unfeature(); }, null, featHide * 1000, Timeout.Infinite);
					}
				}
				WidgetSend("feature", keep);
				res["ok"] = true;
				res["msg"] = "en pantalla";
				return res;
			}
			if (a == "unfeature")
			{
				Unfeature();
				res["ok"] = true;
				res["msg"] = "quitado de la pantalla";
				return res;
			}
			// Encuesta: op = start · end · show · hide · clear
			string op = J.S(d, "op");
			if (op == "start")
			{
				string q = J.S(d, "q").Trim();
				List<string> opts = new List<string>();
				foreach (object o in J.L(d, "opts"))
				{
					string t = Convert.ToString(o).Trim();
					if (t.Length > 0 && opts.Count < 6)
					{
						opts.Add(t.Length > 60 ? t.Substring(0, 60) : t);
					}
				}
				if (q.Length == 0 || opts.Count < 2)
				{
					res["error"] = "pon la pregunta y al menos dos opciones";
					return res;
				}
				int secs = (int)Math.Max(0, Math.Min(3600, J.N(d, 0, "secs")));
				bool announce = Convert.ToString(J.At(d, "announce")).ToLowerInvariant() != "false";
				lock (wgLock)
				{
					pollQ = q.Length > 120 ? q.Substring(0, 120) : q;
					pollOpts = opts;
					pollVotes = new int[opts.Count];
					pollVoters.Clear();
					pollOpen = true;
					pollShown = true;
					pollAnnounce = announce;
					pollEnds = secs > 0 ? NowMs() + secs * 1000L : 0;
					if (pollTimer != null)
					{
						pollTimer.Dispose();
						pollTimer = null;
					}
					if (secs > 0)
					{
						pollTimer = new System.Threading.Timer(delegate { PollEnd(true); }, null, secs * 1000, Timeout.Infinite);
					}
				}
				LogLater("Encuesta: " + q);
				PollBroadcast();
				if (announce)
				{
					StringBuilder sb = new StringBuilder(I18n.Tr("📊 Encuesta: ") + q + I18n.Tr(" → vota escribiendo el número: "));
					for (int i = 0; i < opts.Count; i++)
					{
						sb.Append(i == 0 ? "" : " · ").Append(i + 1).Append(' ').Append(opts[i]);
					}
					PollSay(sb.ToString());
				}
				res["ok"] = true;
				return res;
			}
			if (op == "end")
			{
				PollEnd(true);
				res["ok"] = true;
				return res;
			}
			if (op == "show" || op == "hide")
			{
				lock (wgLock)
				{
					pollShown = op == "show";
				}
				PollBroadcast();
				res["ok"] = true;
				return res;
			}
			if (op == "clear")
			{
				lock (wgLock)
				{
					pollOpen = false;
					pollQ = "";
					pollOpts = new List<string>();
					pollVotes = new int[0];
					pollVoters.Clear();
					pollEnds = 0;
					if (pollTimer != null)
					{
						pollTimer.Dispose();
						pollTimer = null;
					}
				}
				WidgetSend("poll", null);
				res["ok"] = true;
				return res;
			}
			if (op == "state")
			{
				lock (wgLock)
				{
					res["poll"] = PollState();
				}
				res["ok"] = true;
				return res;
			}
			res["error"] = "acción desconocida";
			return res;
		}

		private void Unfeature()
		{
			lock (wgLock)
			{
				featItem = null;
				if (featTimer != null)
				{
					featTimer.Dispose();
					featTimer = null;
				}
			}
			WidgetSend("feature", null);
		}

		// Alerta de prueba (botón de la pestaña Overlays): solo la ven los overlays abiertos.
		private static readonly string[][] TestAlerts = new string[][]
		{
			new string[] { "twitch", "follow", "Pepe", "Pepe te sigue", "" },
			new string[] { "twitch", "sub", "Ana", "Ana se ha suscrito (nivel 1)", "" },
			new string[] { "kick", "giftbomb", "KickFan", "KickFan ha regalado 5 suscripciones al chat", "5" },
			new string[] { "twitch", "raid", "Nocturno", "Nocturno hace raid con 42 espectadores", "42" },
			new string[] { "twitch", "bits", "Laura", "Laura ha enviado 500 bits", "500 bits" },
			new string[] { "twitch", "reward", "Marta", "Marta ha canjeado Hidrátate", "1.500 pts" },
			new string[] { "youtube", "superchat", "ytfan", "ytfan ha enviado un Super Chat de 5,00 €", "5,00 €" },
			new string[] { "tiktok", "tiktokgift", "Racha", "Racha ha enviado 5 × Rosa", "💎 5" }
		};
		private int testAlertNext;

		private void SendTestAlert()
		{
			if (!EnsureChatServer())
			{
				return;
			}
			string[] t = TestAlerts[testAlertNext++ % TestAlerts.Length];
			Dictionary<string, object> ev = new Dictionary<string, object>
			{
				{ "t", "ev" }, { "p", t[0] }, { "kind", t[1] }, { "user", t[2] }, { "text", I18n.Tr(t[3]) }, { "amount", t[4] }, { "test", true }
			};
			chatServer.Send(new ChatItem { t = "alert", data = ev });
			Toast("Alerta de prueba enviada a los overlays de alertas abiertos.", "info");
		}

		// ---------- Pestaña Overlays: un recuadro por overlay nuevo ----------

		// Píldoras que se encienden y apagan cada una por su cuenta.
		private void MultiPills(Control parent, string[] labels, bool[] on, float x, float y, float w, int perRow)
		{
			for (int i = 0; i < labels.Length; i++)
			{
				int idx = i;
				VButton b = new VButton { Kind = ButtonKind.Pill, Text = labels[i], Selected = on[i], FontSize = 7.5f };
				Place(parent, b, x + (float)(i % perRow) * (w + 6f), y + (float)(i / perRow) * 32f, w, 26f);
				b.Click += delegate
				{
					on[idx] = !on[idx];
					b.Selected = on[idx];
					b.Invalidate();
					SaveConfig();
				};
			}
		}

		private Card[] BuildWidgetCards(float yc)
		{
			// --- Alertas ---
			Card a = new Card { Title = "Alertas", Subtitle = "Follows, subs, raids, bits, canjes y donaciones de todo." };
			Place(this, a, 20f, yc, 420f, 372f, 5);
			Label(a, "QUÉ AVISOS", 20f, 54f, 380f);
			MultiPills(a, new string[6] { "Follows", "Subs y regalos", "Raids", "Bits", "Canjes", "Donaciones" }, alertOn, 20f, 72f, 122f, 3);
			Label(a, "DURACIÓN", 20f, 140f, 180f);
			int[] durs = new int[4] { 4, 6, 8, 10 };
			Choice(a, new string[4] { "4 s", "6 s", "8 s", "10 s" }, Math.Max(0, Array.IndexOf(durs, alertDur)), 20f, 158f, 56f, delegate(int i) { lock (wgLock) { alertDur = durs[i]; } });
			Toggle ts = new Toggle { Text = "Sonido", Checked = alertSound };
			Place(a, ts, 280f, 156f, 120f, 30f);
			ts.CheckedChanged += delegate { alertSound = ts.Checked; SaveConfig(); };
			Label(a, "POSICIÓN", 20f, 192f, 180f);
			string[] apos = new string[3] { "top", "center", "bottom" };
			Choice(a, new string[3] { "Arriba", "Centro", "Abajo" }, Math.Max(0, Array.IndexOf(apos, alertPos)), 20f, 210f, 90f, delegate(int i) { lock (wgLock) { alertPos = apos[i]; } });
			VButton test = new VButton { Kind = ButtonKind.Ghost, Text = "Probar una alerta", FontSize = 9f, Icon = "play" };
			Place(a, test, 20f, 250f, 186f, 34f);
			VButton snd = new VButton { Kind = ButtonKind.Ghost, Text = "Elegir sonidos", FontSize = 9f, Icon = "sound" };
			Place(a, snd, 214f, 250f, 186f, 34f);
			snd.Click += delegate { AlertSoundsDialog(); };
			test.Click += delegate { SendTestAlert(); };
			Box(a, 20f, 288f, 380f, 30f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Cada pulsación manda un tipo distinto. Solo se ve en los overlays abiertos, no en el chat.", Theme.Body(7.25f), Theme.Muted, r);
			});
			UrlButtons(a, "alertas", "alertas", 324f);
			StyleLink(a);

			// --- Mensaje destacado ---
			Card f = new Card { Title = "Mensaje destacado", Subtitle = "En el chat conjunto, pulsa un mensaje → «📌 En pantalla»." };
			Place(this, f, 20f, yc, 420f, 262f, 5);
			Label(f, "POSICIÓN", 20f, 54f, 180f);
			string[] fpos = new string[3] { "bl", "bc", "tl" };
			Choice(f, new string[3] { "Abajo izq.", "Abajo centro", "Arriba izq." }, Math.Max(0, Array.IndexOf(fpos, featPos)), 20f, 72f, 100f, delegate(int i) { lock (wgLock) { featPos = fpos[i]; } });
			Label(f, "SE QUITA SOLO", 20f, 106f, 180f);
			int[] fh = new int[4] { 0, 15, 30, 60 };
			Choice(f, new string[4] { "Nunca", "15 s", "30 s", "1 min" }, Math.Max(0, Array.IndexOf(fh, featHide)), 20f, 124f, 70f, delegate(int i) { lock (wgLock) { featHide = fh[i]; } });
			VButton unf = new VButton { Kind = ButtonKind.Ghost, Text = "Quitar de la pantalla", FontSize = 9f, Icon = "x" };
			Place(f, unf, 20f, 166f, 380f, 34f);
			unf.Click += delegate { Unfeature(); };
			UrlButtons(f, "destacado", "mensaje destacado", 214f);
			StyleLink(f);

			// --- Objetivo ---
			Card o = new Card { Title = "Barra de objetivo", Subtitle = "Se rellena sola sumando todas las plataformas." };
			Place(this, o, 20f, yc, 420f, 400f, 5);
			Label(o, "QUÉ CUENTA", 20f, 54f, 380f);
			string[] gtypes = new string[4] { "follows", "subs", "bits", "manual" };
			string[] gtitles = new string[4] { "Objetivo de follows", "Objetivo de subs", "Objetivo de bits", "Objetivo" };
			Field fTitle = new Field();
			Choice(o, new string[4] { "Follows", "Subs", "Bits", "A mano" }, Math.Max(0, Array.IndexOf(gtypes, goalType)), 20f, 72f, 89f, delegate(int i)
			{
				bool defTitle = Array.IndexOf(gtitles, goalTitle) >= 0 || goalTitle.Length == 0;
				lock (wgLock) { goalType = gtypes[i]; }
				if (defTitle)
				{
					fTitle.Box.Text = gtitles[i];
				}
				GoalBroadcast();
			});
			Label(o, "TÍTULO", 20f, 106f, 380f);
			Place(o, fTitle, 20f, 124f, 380f, 34f);
			fTitle.Box.Text = goalTitle;
			fTitle.Box.MaxLength = 60;
			fTitle.Box.TextChanged += delegate { lock (wgLock) { goalTitle = fTitle.Box.Text.Trim(); } SaveConfig(); GoalBroadcast(); };
			Label(o, "META", 20f, 164f, 120f);
			Field fTarget = new Field();
			Place(o, fTarget, 20f, 182f, 120f, 34f);
			fTarget.Box.Text = goalTarget.ToString();
			fTarget.Box.TextChanged += delegate
			{
				int n;
				if (int.TryParse(fTarget.Box.Text.Trim(), out n) && n > 0)
				{
					lock (wgLock) { goalTarget = n; }
					SaveConfig();
					GoalBroadcast();
				}
			};
			Label(o, "LLEVAS", 152f, 164f, 120f);
			Field fCount = new Field();
			Place(o, fCount, 152f, 182f, 100f, 34f);
			fCount.Box.Text = goalCount.ToString();
			bool syncing = false;
			fCount.Box.TextChanged += delegate
			{
				int n;
				if (!syncing && int.TryParse(fCount.Box.Text.Trim(), out n) && n >= 0)
				{
					lock (wgLock) { goalCount = n; }
					SaveConfig();
					GoalBroadcast();
				}
			};
			Action<int> bump = delegate(int delta)
			{
				lock (wgLock) { goalCount = Math.Max(0, goalCount + delta); }
				syncing = true;
				fCount.Box.Text = goalCount.ToString();
				syncing = false;
				SaveConfig();
				GoalBroadcast();
			};
			VButton minus = new VButton { Kind = ButtonKind.Ghost, Text = "−1", FontSize = 9f };
			Place(o, minus, 260f, 182f, 44f, 34f);
			minus.Click += delegate { bump(-1); };
			VButton plus = new VButton { Kind = ButtonKind.Ghost, Text = "+1", FontSize = 9f };
			Place(o, plus, 308f, 182f, 44f, 34f);
			plus.Click += delegate { bump(1); };
			VButton zero = new VButton { Kind = ButtonKind.Ghost, Text = "0", FontSize = 9f };
			Place(o, zero, 356f, 182f, 44f, 34f);
			zero.Click += delegate { bump(-goalCount); };
			// Lo que suma el chat se ve también aquí.
			System.Windows.Forms.Timer gsync = new System.Windows.Forms.Timer { Interval = 1000 };
			gsync.Tick += delegate
			{
				if (!o.Visible || fCount.Box.Focused)
				{
					return;
				}
				string now = goalCount.ToString();
				if (fCount.Box.Text != now)
				{
					syncing = true;
					fCount.Box.Text = now;
					syncing = false;
				}
			};
			gsync.Start();
			Label(o, "POSICIÓN", 20f, 224f, 180f);
			string[] gpos = new string[2] { "top", "bottom" };
			Choice(o, new string[2] { "Arriba", "Abajo" }, Math.Max(0, Array.IndexOf(gpos, goalPos)), 20f, 242f, 90f, delegate(int i) { lock (wgLock) { goalPos = gpos[i]; } GoalBroadcast(); });
			Box(o, 20f, 278f, 380f, 64f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Follows: cada follow (Twitch con tu cuenta conectada o StreamElements/Streamlabs). Subs: subs, renovaciones, regalos y miembros de YouTube. Bits: los bits de Twitch. A mano: solo con −1 / +1. Cuenta mientras el chat está activado.", Theme.Body(7.25f), Theme.Muted, r);
			});
			UrlButtons(o, "objetivo", "objetivo", 352f);
			StyleLink(o);

			// --- Últimos eventos ---
			Card e = new Card { Title = "Últimos eventos", Subtitle = "Una cinta que pasa con los últimos follows, subs, raids, bits…" };
			Place(this, e, 20f, yc, 420f, 250f, 5);
			Label(e, "CUÁNTOS", 20f, 54f, 180f);
			int[] tns = new int[3] { 5, 10, 20 };
			Choice(e, new string[3] { "5", "10", "20" }, Math.Max(0, Array.IndexOf(tns, tickN)), 20f, 72f, 60f, delegate(int i) { lock (wgLock) { tickN = tns[i]; } });
			Label(e, "VELOCIDAD", 220f, 54f, 180f);
			string[] tsp = new string[3] { "slow", "normal", "fast" };
			Choice(e, new string[3] { "Lenta", "Normal", "Rápida" }, Math.Max(0, Array.IndexOf(tsp, tickSpeed)), 220f, 72f, 56f, delegate(int i) { lock (wgLock) { tickSpeed = tsp[i]; } });
			Label(e, "POSICIÓN", 20f, 106f, 180f);
			string[] tpos = new string[2] { "bottom", "top" };
			Choice(e, new string[2] { "Abajo", "Arriba" }, Math.Max(0, Array.IndexOf(tpos, tickPos)), 20f, 124f, 90f, delegate(int i) { lock (wgLock) { tickPos = tpos[i]; } });
			Box(e, 20f, 158f, 380f, 30f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Usa los mismos tipos que las alertas. Ocupa todo el ancho: fuente Navegador de 1920x1080.", Theme.Body(7.25f), Theme.Muted, r);
			});
			UrlButtons(e, "eventos", "últimos eventos", 200f);
			StyleLink(e);

			// --- Encuesta ---
			Card p = new Card { Title = "Encuesta", Subtitle = "Se crea en el chat conjunto: botón 🗳 Encuesta, al lado de Moderar." };
			Place(this, p, 20f, yc, 420f, 262f, 5);
			Label(p, "POSICIÓN", 20f, 54f, 180f);
			string[] ppos = new string[2] { "left", "right" };
			Choice(p, new string[2] { "Izquierda", "Derecha" }, Math.Max(0, Array.IndexOf(ppos, pollPos)), 20f, 72f, 90f, delegate(int i) { lock (wgLock) { pollPos = ppos[i]; } PollBroadcast(); });
			VButton openChat = new VButton { Kind = ButtonKind.Ghost, Text = "Abrir el chat conjunto", FontSize = 9f, Icon = "chat" };
			Place(p, openChat, 20f, 112f, 380f, 34f);
			openChat.Click += delegate
			{
				string u = OverlayUrl("");
				if (u != null)
				{
					Http.OpenBrowser(u);
				}
			};
			Box(p, 20f, 152f, 380f, 46f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Se vota escribiendo el número de la opción (1, 2…) en Twitch, Kick, YouTube o TikTok. Un voto por persona: si cambia de número, cuenta el último.", Theme.Body(7.25f), Theme.Muted, r);
			});
			UrlButtons(p, "encuesta", "encuesta", 214f);
			StyleLink(p);

			return new Card[5] { a, f, o, e, p };
		}

		// ---------- Ventana "Sonidos de las alertas" ----------

		[DllImport("winmm.dll", CharSet = CharSet.Unicode)]
		private static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr callback);

		// Escuchar un sonido en el PC (MP3 y WAV; los OGG se oyen en OBS pero aquí no).
		private void PlaySoundPreview(string file, int volume)
		{
			try
			{
				mciSendString("close vlprev", null, 0, IntPtr.Zero);
				if (file == null)
				{
					return;
				}
				if (mciSendString("open \"" + file + "\" type mpegvideo alias vlprev", null, 0, IntPtr.Zero) != 0)
				{
					return;
				}
				mciSendString("setaudio vlprev volume to " + Math.Max(0, Math.Min(1000, volume * 10)), null, 0, IntPtr.Zero);
				mciSendString("play vlprev", null, 0, IntPtr.Zero);
			}
			catch
			{
			}
		}

		private void AlertSoundsDialog()
		{
			Form f = DarkForm("Sonidos de las alertas", 460, 486);
			Card c = new Card { Title = "Un sonido para cada alerta", Subtitle = "MP3, WAV u OGG de hasta 5 MB. Sin elegir, suena el de siempre." };
			Place(f, c, 20f, 18f, 420f, 384f);
			string[] names = new string[6] { "Follows", "Subs y regalos", "Raids", "Bits", "Canjes", "Donaciones" };
			for (int i = 0; i < AlertKinds.Length; i++)
			{
				string kind = AlertKinds[i];
				float y = 58f + (float)i * 40f;
				PaintBox row = Box(c, 20f, y, 206f, 34f, delegate(Graphics g, Rectangle r)
				{
					string file = AlertSoundFile(kind);
					Rectangle top = new Rectangle(r.X, r.Y, r.Width, r.Height / 2 + 1);
					Rectangle bottom = new Rectangle(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2);
					Theme.DrawText(g, names[Array.IndexOf(AlertKinds, kind)], Theme.BodyBold(9f), Theme.Text, top, StringAlignment.Near, StringAlignment.Far);
					Theme.DrawText(g, file == null ? "Sonido de siempre" : Path.GetFileName(file), Theme.Body(7.5f), file == null ? Theme.Muted : Theme.Violet, bottom, StringAlignment.Near, StringAlignment.Near);
				});
				VButton pick = new VButton { Kind = ButtonKind.Ghost, Text = "Elegir…", FontSize = 8.5f };
				Place(c, pick, 232f, y + 2f, 84f, 30f);
				VButton play = new VButton { Kind = ButtonKind.Ghost, Text = "▶", FontSize = 9f };
				Place(c, play, 322f, y + 2f, 36f, 30f);
				VButton clear = new VButton { Kind = ButtonKind.Ghost, Text = "✕", FontSize = 9f };
				Place(c, clear, 364f, y + 2f, 36f, 30f);
				pick.Click += delegate
				{
					using (OpenFileDialog dlg = new OpenFileDialog())
					{
						dlg.Title = I18n.Tr("Sonido para ") + I18n.Tr(names[Array.IndexOf(AlertKinds, kind)]);
						dlg.Filter = I18n.Tr("Sonidos") + " (*.mp3;*.wav;*.ogg;*.m4a)|*.mp3;*.wav;*.ogg;*.m4a";
						if (dlg.ShowDialog(f) != DialogResult.OK)
						{
							return;
						}
						try
						{
							FileInfo info = new FileInfo(dlg.FileName);
							if (info.Length > 5 * 1024 * 1024)
							{
								I18n.Show(f, "Ese archivo pesa más de 5 MB. Elige un sonido más corto.", "Sonidos");
								return;
							}
							string ext = info.Extension.ToLowerInvariant();
							if (Array.IndexOf(SoundExts, ext) < 0)
							{
								return;
							}
							PlaySoundPreview(null, 0);
							Directory.CreateDirectory(SoundDir);
							foreach (string e2 in SoundExts)
							{
								string old = Path.Combine(SoundDir, kind + e2);
								if (File.Exists(old))
								{
									File.Delete(old);
								}
							}
							string dest = Path.Combine(SoundDir, kind + ext);
							File.Copy(dlg.FileName, dest, true);
							File.SetLastWriteTimeUtc(dest, DateTime.UtcNow);
							AppendLog("Alertas: sonido de " + names[Array.IndexOf(AlertKinds, kind)] + " → " + info.Name);
						}
						catch (Exception ex)
						{
							I18n.Show(f, "No se pudo copiar el sonido: " + ex.Message, "Sonidos");
						}
						row.Invalidate();
					}
				};
				play.Click += delegate { PlaySoundPreview(AlertSoundFile(kind), alertVolume); };
				clear.Click += delegate
				{
					PlaySoundPreview(null, 0);
					foreach (string e2 in SoundExts)
					{
						try
						{
							string old = Path.Combine(SoundDir, kind + e2);
							if (File.Exists(old))
							{
								File.Delete(old);
							}
						}
						catch
						{
						}
					}
					row.Invalidate();
				};
			}
			Label(c, "VOLUMEN EN OBS", 20f, 304f, 380f);
			int[] vols = new int[5] { 20, 40, 60, 80, 100 };
			int vk = 0;
			for (int i = 0; i < vols.Length; i++)
			{
				if (Math.Abs(vols[i] - alertVolume) < Math.Abs(vols[vk] - alertVolume))
				{
					vk = i;
				}
			}
			Choice(c, new string[5] { "20 %", "40 %", "60 %", "80 %", "100 %" }, vk, 20f, 322f, 70f, delegate(int i) { lock (wgLock) { alertVolume = vols[i]; } });
			Box(c, 20f, 352f, 380f, 26f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Para oírlas en el directo, en OBS marca «Controlar audio mediante OBS» en la fuente Navegador.", Theme.Body(7.25f), Theme.Muted, r);
			});
			VButton test = new VButton { Kind = ButtonKind.Ghost, Text = "Probar una alerta", FontSize = 9.5f };
			VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "Listo", FontSize = 10f };
			Place(f, test, 20f, 420f, 190f, 44f);
			Place(f, ok, 218f, 420f, 222f, 44f);
			test.Click += delegate { SendTestAlert(); };
			ok.Click += delegate { f.Close(); };
			f.FormClosing += delegate { PlaySoundPreview(null, 0); SaveConfig(); };
			f.ShowDialog(this);
		}

		// ---------- Configuración ----------

		private void LoadWidgetsConfig(Dictionary<string, string> raw)
		{
			string v;
			int n;
			if (raw.TryGetValue("wg_alerts", out v) && v.Length == alertOn.Length) for (int i = 0; i < v.Length; i++) alertOn[i] = v[i] == '1';
			if (raw.TryGetValue("wg_alert_dur", out v) && int.TryParse(v, out n) && n >= 2 && n <= 30) alertDur = n;
			if (raw.TryGetValue("wg_alert_sound", out v)) alertSound = v != "0";
			if (raw.TryGetValue("wg_alert_pos", out v) && v.Length > 0) alertPos = v;
			if (raw.TryGetValue("wg_alert_vol", out v) && int.TryParse(v, out n) && n >= 0 && n <= 100) alertVolume = n;
			if (raw.TryGetValue("wg_feat_pos", out v) && v.Length > 0) featPos = v;
			if (raw.TryGetValue("wg_feat_hide", out v) && int.TryParse(v, out n) && n >= 0) featHide = n;
			if (raw.TryGetValue("wg_goal_type", out v) && v.Length > 0) goalType = v;
			if (raw.TryGetValue("wg_goal_title", out v)) goalTitle = Unb64(v, goalTitle);
			if (raw.TryGetValue("wg_goal_target", out v) && int.TryParse(v, out n) && n > 0) goalTarget = n;
			if (raw.TryGetValue("wg_goal_count", out v) && int.TryParse(v, out n) && n >= 0) goalCount = n;
			if (raw.TryGetValue("wg_goal_pos", out v) && v.Length > 0) goalPos = v;
			if (raw.TryGetValue("wg_tick_n", out v) && int.TryParse(v, out n) && n > 0) tickN = n;
			if (raw.TryGetValue("wg_tick_speed", out v) && v.Length > 0) tickSpeed = v;
			if (raw.TryGetValue("wg_tick_pos", out v) && v.Length > 0) tickPos = v;
			if (raw.TryGetValue("wg_poll_pos", out v) && v.Length > 0) pollPos = v;
			if (raw.TryGetValue("ov_pick", out v) && int.TryParse(v, out n) && n >= 0 && n < 8) ovPick = n;
			if (raw.TryGetValue("win_w", out v) && int.TryParse(v, out n) && n >= 460 && n <= 1180) winWidth = n;
			if (raw.TryGetValue("wide_w", out v) && int.TryParse(v, out n) && n >= 760 && n <= 1180) wideWidth = n;
			if (raw.TryGetValue("panel_tip", out v)) panelTipShown = v == "1";
		}

		private void SaveWidgetsConfig(List<string> lines)
		{
			StringBuilder sb = new StringBuilder();
			foreach (bool b in alertOn)
			{
				sb.Append(b ? '1' : '0');
			}
			lock (wgLock)
			{
				lines.Add("wg_alerts=" + sb);
				lines.Add("wg_alert_dur=" + alertDur);
				lines.Add("wg_alert_sound=" + (alertSound ? "1" : "0"));
				lines.Add("wg_alert_pos=" + alertPos);
				lines.Add("wg_alert_vol=" + alertVolume);
				lines.Add("wg_feat_pos=" + featPos);
				lines.Add("wg_feat_hide=" + featHide);
				lines.Add("wg_goal_type=" + goalType);
				lines.Add("wg_goal_title=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(goalTitle ?? "")));
				lines.Add("wg_goal_target=" + goalTarget);
				lines.Add("wg_goal_count=" + goalCount);
				lines.Add("wg_goal_pos=" + goalPos);
				lines.Add("wg_tick_n=" + tickN);
				lines.Add("wg_tick_speed=" + tickSpeed);
				lines.Add("wg_tick_pos=" + tickPos);
				lines.Add("wg_poll_pos=" + pollPos);
			}
			lines.Add("ov_pick=" + ovPick);
			lines.Add("win_w=" + winWidth);
			lines.Add("wide_w=" + wideWidth);
			lines.Add("panel_tip=" + (panelTipShown ? "1" : "0"));
		}
	}
}
