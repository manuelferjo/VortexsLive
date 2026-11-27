// Resumen del directo: mientras emites se van apuntando los espectadores, los eventos y el chat;
// al terminar (sin destinos en directo durante 90 s, o al cerrar la app) se guarda una página en
// la carpeta "resumenes" junto a Vortexs Live y se abre en el navegador. historial.csv guarda
// las cifras de cada directo para compararlas con el anterior.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private class LiveStats
		{
			public DateTime Start = DateTime.Now;
			public DateTime End = DateTime.Now;
			public readonly Dictionary<string, int> Peak = new Dictionary<string, int>();
			public int Follows, Subs, Bits, Redemptions, Msgs, Clips, MaxDelay;
			public readonly Dictionary<string, int> MsgsBy = new Dictionary<string, int>();
			public readonly Dictionary<string, int> Chatters = new Dictionary<string, int>();     // "p\u001fusuario"
			public readonly Dictionary<string, int> Gifters = new Dictionary<string, int>();
			public readonly List<string[]> Raids = new List<string[]>();                          // p, usuario, espectadores
			public readonly List<string[]> Donations = new List<string[]>();                      // p, usuario, cantidad, tipo
			public readonly List<string> ClipWho = new List<string>();
			// Media de espectadores (ponderada por tiempo): suma de espectadores × segundos y segundos con datos.
			public readonly Dictionary<string, double> VSum = new Dictionary<string, double>();
			public readonly Dictionary<string, double> VSecs = new Dictionary<string, double>();
			// Juegos en el orden en que se jugaron, y el de ahora.
			public readonly List<GameStat> Games = new List<GameStat>();
			public GameStat Cur;
			// Espectadores cada minuto (para la gráfica): minuto, total (-1 sin datos), juego.
			public readonly List<object[]> Timeline = new List<object[]>();
			public DateTime LastTick = DateTime.MinValue, NextSample = DateTime.MinValue;

			public int Avg(string k)
			{
				double sum, secs;
				return (VSum.TryGetValue(k, out sum) && VSecs.TryGetValue(k, out secs) && secs > 0.0) ? (int)Math.Round(sum / secs) : -1;
			}
		}

		// Lo de cada juego o categoría del día.
		private class GameStat
		{
			public string Name;
			public double Secs, VSum, VSecs;
			public int Peak = -1, Follows, Subs, Msgs;

			public int Avg
			{
				get { return VSecs > 0.0 ? (int)Math.Round(VSum / VSecs) : -1; }
			}
		}

		private readonly object sumLock = new object();
		private LiveStats stats;
		private DateTime sumLastLive = DateTime.MinValue;
		private string lastSummaryFile;

		private static string SummaryDir
		{
			get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resumenes"); }
		}

		// ---------- Cada medio segundo (hilo de la ventana) ----------

		private void SummaryTick()
		{
			bool live = engine.OutputActive;
			DateTime now = DateTime.Now;
			LiveStats done = null;
			lock (sumLock)
			{
				if (live)
				{
					if (stats == null)
					{
						stats = new LiveStats();
					}
					sumLastLive = now;
					stats.End = now;
					// Segundos desde la última vez (como mucho 2: si la ventana se ha quedado parada no cuenta de más).
					double dt = (stats.LastTick == DateTime.MinValue) ? 0.0 : Math.Min(2.0, (now - stats.LastTick).TotalSeconds);
					stats.LastTick = now;
					ChatHub h = chatHub;
					int total = 0;
					bool any = false;
					string game = "";
					if (h != null)
					{
						Peak("twitch", h.TwitchViewers, ref total, ref any, dt);
						Peak("kick", h.KickViewers, ref total, ref any, dt);
						Peak("youtube", h.YouTubeViewers, ref total, ref any, dt);
						Peak("tiktok", h.TikTokViewers, ref total, ref any, dt);
						if (any)
						{
							PeakSet("total", total);
							Avg("total", total, dt);
						}
						game = h.CurrentGame;
					}
					// Juego de ahora: si no se sabe todavía, se sigue con el último conocido.
					if (game.Length == 0 && stats.Cur != null)
					{
						game = stats.Cur.Name;
					}
					if (game.Length > 0 && (stats.Cur == null || stats.Cur.Name != game))
					{
						stats.Cur = stats.Games.FirstOrDefault(g => g.Name == game);
						if (stats.Cur == null)
						{
							stats.Cur = new GameStat { Name = game };
							stats.Games.Add(stats.Cur);
						}
					}
					if (stats.Cur != null)
					{
						stats.Cur.Secs += dt;
						if (any)
						{
							stats.Cur.VSum += total * dt;
							stats.Cur.VSecs += dt;
							stats.Cur.Peak = Math.Max(stats.Cur.Peak, total);
						}
					}
					if (now >= stats.NextSample && stats.Timeline.Count < 1440)
					{
						stats.NextSample = now.AddMinutes(1.0);
						stats.Timeline.Add(new object[3] { (now - stats.Start).TotalMinutes, any ? total : -1, stats.Cur != null ? stats.Cur.Name : "" });
					}
					if (engine.Enabled)
					{
						stats.MaxDelay = Math.Max(stats.MaxDelay, engine.TargetMs / 1000);
					}
				}
				else if (stats != null && (now - sumLastLive).TotalSeconds > 90.0)
				{
					done = stats;
					stats = null;
				}
			}
			if (done != null)
			{
				FinishSummary(done, true);
			}
		}

		private void Peak(string p, int v, ref int total, ref bool any, double dt)
		{
			if (v >= 0)
			{
				total += v;
				any = true;
				PeakSet(p, v);
				Avg(p, v, dt);
			}
		}

		private void Avg(string p, int v, double dt)
		{
			double sum, secs;
			stats.VSum.TryGetValue(p, out sum);
			stats.VSecs.TryGetValue(p, out secs);
			stats.VSum[p] = sum + v * dt;
			stats.VSecs[p] = secs + dt;
		}

		private void PeakSet(string p, int v)
		{
			int old;
			if (!stats.Peak.TryGetValue(p, out old) || v > old)
			{
				stats.Peak[p] = v;
			}
		}

		// Al cerrar la app en pleno directo también se guarda.
		private void SummaryOnClose()
		{
			LiveStats done;
			lock (sumLock)
			{
				done = stats;
				stats = null;
			}
			if (done != null)
			{
				FinishSummary(done, false);
			}
		}

		// ---------- Lo que pasa en el chat (hilo del chat) ----------

		private static void Add(Dictionary<string, int> d, string k, int n)
		{
			int v;
			d.TryGetValue(k, out v);
			d[k] = v + n;
		}

		private void SummaryOnItem(ChatItem it)
		{
			if (it == null)
			{
				return;
			}
			lock (sumLock)
			{
				if (stats == null)
				{
					return;
				}
				string p = it.p ?? "";
				string user = it.user ?? "";
				// Lo que se escribe en otros canales del chat compartido no es de tu directo.
				if (it.src != null)
				{
					return;
				}
				GameStat cur = stats.Cur;
				if (it.t == "msg")
				{
					stats.Msgs++;
					if (cur != null) cur.Msgs++;
					Add(stats.MsgsBy, p, 1);
					if (user.Length > 0)
					{
						Add(stats.Chatters, p + "\u001f" + user, 1);
					}
					return;
				}
				if (it.t != "ev")
				{
					return;
				}
				switch (it.kind ?? "")
				{
				case "follow":
					stats.Follows++;
					if (cur != null) cur.Follows++;
					break;
				case "sub":
				case "resub":
				case "member":
					stats.Subs++;
					if (cur != null) cur.Subs++;
					break;
				case "gift":
					stats.Subs++;
					if (cur != null) cur.Subs++;
					Add(stats.Gifters, p + "\u001f" + user, 1);
					break;
				case "giftbomb":
				case "giftmember":
				{
					int n = Math.Max(1, LeadingInt(it.amount));
					stats.Subs += n;
					if (cur != null) cur.Subs += n;
					Add(stats.Gifters, p + "\u001f" + user, n);
					break;
				}
				case "bits":
					stats.Bits += LeadingInt(it.amount);
					break;
				case "reward":
					stats.Redemptions++;
					break;
				case "raid":
					stats.Raids.Add(new string[3] { p, user, it.amount ?? "" });
					break;
				case "tip":
				case "superchat":
				case "sticker":
				case "kicks":
				case "tiktokgift":
					if (stats.Donations.Count < 200)
					{
						stats.Donations.Add(new string[4] { p, user, it.amount ?? "", it.kind });
					}
					break;
				}
			}
		}

		private void SummaryClip(string who)
		{
			lock (sumLock)
			{
				if (stats != null)
				{
					stats.Clips++;
					stats.ClipWho.Add(who ?? "");
				}
			}
		}

		// ---------- Guardar y enseñar ----------

		private void FinishSummary(LiveStats s, bool open)
		{
			if ((s.End - s.Start).TotalMinutes < 3.0)
			{
				return;   // una prueba corta no merece resumen
			}
			try
			{
				Directory.CreateDirectory(SummaryDir);
				string[] prev = LastHistory();
				string file = Path.Combine(SummaryDir, "Directo " + s.Start.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture) + ".html");
				// La imagen para historias: en Imágenes\Vortexs Live\Resúmenes y, si hay Telegram, al móvil.
				string png = MakeRecapPng(s, Path.Combine(RecapStory.Folder, "Directo " + s.Start.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture) + ".png"));
				bool tg = png != null && !string.IsNullOrEmpty(xpTgToken) && !string.IsNullOrEmpty(xpTgChat);
				File.WriteAllText(file, SummaryHtml(s, prev, false, png, tg), new UTF8Encoding(false));
				if (tg)
				{
					SendRecapToTelegram(s, png);
				}
				AppendHistory(s);
				lastSummaryFile = file;
				LogLater("Resumen del directo guardado: " + file);
				Toast("Resumen del directo guardado. Lo tienes en Ajustes → Registro → Resúmenes.", "ok");
				if (open)
				{
					try
					{
						BeginInvoke((Action)delegate { Http.OpenBrowser(file); });
					}
					catch
					{
					}
				}
			}
			catch (Exception ex)
			{
				LogLater("No se pudo guardar el resumen del directo: " + ex.Message);
			}
		}

		// Botón "Resúmenes" (pestaña Registro): el de ahora si estás en directo; si no, el último.
		private void OpenSummary()
		{
			LiveStats now = null;
			lock (sumLock)
			{
				if (stats != null)
				{
					now = stats;
				}
			}
			try
			{
				if (now != null)
				{
					string tmp = Path.Combine(Path.GetTempPath(), "Vortexs Live - resumen de ahora.html");
					lock (sumLock)
					{
						string png = MakeRecapPng(now, Path.Combine(Path.GetTempPath(), "Vortexs Live - resumen de ahora.png"));
						File.WriteAllText(tmp, SummaryHtml(now, LastHistory(), true, png, false), new UTF8Encoding(false));
					}
					Http.OpenBrowser(tmp);
					return;
				}
				string last = lastSummaryFile;
				if (last == null || !File.Exists(last))
				{
					last = Directory.Exists(SummaryDir) ? Directory.GetFiles(SummaryDir, "*.html").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
				}
				if (last == null)
				{
					Toast("Todavía no hay ningún resumen. Se crea solo al terminar cada directo (de 3 minutos o más) y se abre en el navegador.", "info");
					return;
				}
				Http.OpenBrowser(last);
			}
			catch (Exception ex)
			{
				AppendLog("No se pudo abrir el resumen: " + ex.Message);
			}
		}

		// historial.csv: fecha;minutos;pico;follows;subs;bits;mensajes;personas;media;juegos (los dos últimos desde la 4.31)
		private static string HistoryFile
		{
			get { return Path.Combine(SummaryDir, "historial.csv"); }
		}

		private string[] LastHistory()
		{
			try
			{
				if (!File.Exists(HistoryFile))
				{
					return null;
				}
				string[] lines = File.ReadAllLines(HistoryFile);
				for (int i = lines.Length - 1; i >= 1; i--)
				{
					string[] c = lines[i].Split(';');
					if (c.Length >= 8)
					{
						return c;
					}
				}
			}
			catch
			{
			}
			return null;
		}

		private void AppendHistory(LiveStats s)
		{
			if (!File.Exists(HistoryFile))
			{
				File.WriteAllText(HistoryFile, "fecha;minutos;pico;follows;subs;bits;mensajes;personas;media;juegos\r\n", new UTF8Encoding(false));
			}
			int peak;
			s.Peak.TryGetValue("total", out peak);
			string games = string.Join(" | ", s.Games.Select(g => g.Name.Replace(";", ",").Replace("|", "/")).ToArray());
			File.AppendAllText(HistoryFile, s.Start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ";" + (int)(s.End - s.Start).TotalMinutes + ";" + peak + ";" + s.Follows + ";" + s.Subs + ";" + s.Bits + ";" + s.Msgs + ";" + s.Chatters.Count + ";" + Math.Max(0, s.Avg("total")) + ";" + games + "\r\n", new UTF8Encoding(false));
		}

		// ---------- La página ----------

		private static string H(string s)
		{
			return System.Net.WebUtility.HtmlEncode(s ?? "");
		}

		private static string T(string s)
		{
			return H(I18n.Tr(s));
		}

		private static string Num(int n)
		{
			return n.ToString("N0", I18n.English ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("es-ES"));
		}

		private static string Dur(TimeSpan t)
		{
			int h = (int)t.TotalHours;
			return (h > 0 ? h + " h " : "") + t.Minutes + " min";
		}

		private static readonly Dictionary<string, string> PfName = new Dictionary<string, string>
		{
			{ "twitch", "Twitch" }, { "kick", "Kick" }, { "youtube", "YouTube" }, { "tiktok", "TikTok" }, { "tip", "Donación" }
		};

		private static readonly Dictionary<string, string> PfColor = new Dictionary<string, string>
		{
			{ "twitch", "#a970ff" }, { "kick", "#53fc18" }, { "youtube", "#ff3b3b" }, { "tiktok", "#25f4ee" }, { "tip", "#ffd166" }
		};

		private static string Pf(string p)
		{
			string n, c;
			if (!PfName.TryGetValue(p ?? "", out n)) n = p ?? "";
			if (!PfColor.TryGetValue(p ?? "", out c)) c = "#a1a1aa";
			return "<span class=\"pf\" style=\"--c:" + c + "\">" + T(n) + "</span>";
		}

		// Flecha comparando con el directo anterior (columna col de historial.csv).
		private static string Cmp(string[] prev, int col, int now)
		{
			int before;
			if (prev == null || prev.Length <= col || !int.TryParse(prev[col], out before))
			{
				return "";
			}
			if (now == before)
			{
				return "<span class=\"cmp\">= " + T("que el anterior") + "</span>";
			}
			return "<span class=\"cmp " + (now > before ? "up" : "down") + "\">" + (now > before ? "▲ +" : "▼ ") + Num(now - before) + " " + T("vs. el anterior") + "</span>";
		}

		private string SummaryHtml(LiveStats s, string[] prev, bool ongoing, string png, bool telegram)
		{
			CultureInfo ci = I18n.English ? CultureInfo.GetCultureInfo("en-GB") : CultureInfo.GetCultureInfo("es-ES");
			int peak;
			s.Peak.TryGetValue("total", out peak);
			TimeSpan dur = s.End - s.Start;
			StringBuilder b = new StringBuilder();
			b.Append("<!doctype html><html lang=\"").Append(I18n.English ? "en" : "es").Append("\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
			b.Append("<title>").Append(T("Resumen del directo")).Append(" · ").Append(H(s.Start.ToString("d MMM yyyy", ci))).Append("</title><style>");
			b.Append(":root{--bg:#0b0b0f;--card:#15151c;--b:#26262f;--tx:#f4f4f5;--mu:#a1a1aa;--dim:#71717a;--v:#c09afd;--y:#ffd166;--g:#4ade80;--r:#ff6b7a}");
			b.Append("*{box-sizing:border-box}body{margin:0;background:radial-gradient(1200px 500px at 0 -10%,rgba(192,154,253,.16),transparent),radial-gradient(900px 400px at 100% 0,rgba(255,209,102,.08),transparent),var(--bg);color:var(--tx);font:15px/1.45 'Segoe UI',system-ui,sans-serif}");
			b.Append(".w{max-width:980px;margin:0 auto;padding:36px 20px 60px}h1{font-size:30px;margin:0 0 4px;letter-spacing:-.01em}h1 b{background:linear-gradient(90deg,var(--v),var(--y));-webkit-background-clip:text;background-clip:text;color:transparent}");
			b.Append(".sub{color:var(--mu);margin-bottom:26px}.live{display:inline-block;padding:2px 10px;border-radius:999px;background:rgba(255,70,85,.15);color:var(--r);font-weight:700;font-size:12px;margin-left:8px;vertical-align:middle}");
			b.Append(".grid{display:grid;grid-template-columns:repeat(auto-fill,minmax(210px,1fr));gap:12px;margin-bottom:26px}.k{background:var(--card);border:1px solid var(--b);border-radius:14px;padding:16px 18px}");
			b.Append(".k .l{color:var(--mu);font-size:12px;text-transform:uppercase;letter-spacing:.08em;font-weight:600}.k .n{font-size:30px;font-weight:800;margin-top:4px;font-variant-numeric:tabular-nums}.k .x{color:var(--dim);font-size:13px;margin-top:2px}");
			b.Append(".cmp{display:block;font-size:12.5px;margin-top:4px;color:var(--dim)}.cmp.up{color:var(--g)}.cmp.down{color:var(--r)}");
			b.Append("h2{font-size:17px;margin:28px 0 10px}.box{background:var(--card);border:1px solid var(--b);border-radius:14px;padding:6px 18px}");
			b.Append(".row{display:flex;align-items:center;gap:10px;padding:10px 0;border-bottom:1px solid var(--b)}.row:last-child{border-bottom:0}.row .rk{width:22px;color:var(--dim);font-weight:700}.row .u{font-weight:600}.row .am{margin-left:auto;font-weight:700;font-variant-numeric:tabular-nums}");
			b.Append(".pf{display:inline-block;font-size:11.5px;font-weight:700;padding:1px 8px;border-radius:999px;color:var(--c);background:color-mix(in srgb,var(--c) 14%,transparent)}");
			b.Append(".bars{display:flex;flex-direction:column;gap:8px;padding:12px 0}.bar{display:flex;align-items:center;gap:10px}.bar .t{position:relative;flex:1;height:10px;border-radius:999px;background:#202029;overflow:hidden}.bar .t i{position:absolute;inset:0 auto 0 0;border-radius:inherit;background:var(--c)}.bar .am{width:70px;text-align:right;font-weight:700}");
			b.Append(".none{color:var(--dim);padding:12px 0}.gt{width:100%;border-collapse:collapse;margin:6px 0}.gt th{color:var(--mu);font-size:12px;font-weight:600;text-transform:uppercase;letter-spacing:.06em;text-align:right;padding:8px 6px;border-bottom:1px solid var(--b)}.gt th:nth-child(2){text-align:left}.gt td{padding:10px 6px;border-bottom:1px solid var(--b);text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}.gt tr:last-child td{border-bottom:0}.gt td.gn{text-align:left;font-weight:600;white-space:normal}.gt small{color:var(--dim)}.dot{display:inline-block;width:10px;height:10px;border-radius:50%;vertical-align:middle}.best{font-size:11.5px;font-weight:700;color:var(--y);margin-left:6px}.story{display:flex;gap:22px;align-items:center;flex-wrap:wrap;padding:18px}.story img#card{width:270px;height:480px;border-radius:16px;border:1px solid var(--b);box-shadow:0 14px 40px rgba(0,0,0,.45)}.sx{flex:1;min-width:220px}.sx p{color:var(--mu);margin:0 0 14px}.sx .btn,.sx button{display:inline-block;text-decoration:none;font:600 15px 'Segoe UI',system-ui,sans-serif;border:0;border-radius:12px;padding:12px 18px;margin:0 8px 8px 0;cursor:pointer;background:linear-gradient(90deg,var(--v),var(--y));color:#15151c}.sx code{font-size:12px;color:var(--dim);word-break:break-all}.sx button.g{background:#202029;color:var(--tx);border:1px solid var(--b)}#msg{display:block;color:var(--g);font-size:13.5px;margin-top:4px}.foot{margin-top:34px;color:var(--dim);font-size:12.5px}</style></head><body><div class=\"w\">");

			b.Append("<h1>📊 <b>").Append(T("Resumen del directo")).Append("</b>").Append(ongoing ? "<span class=\"live\">● " + T("EN DIRECTO") + "</span>" : "").Append("</h1>");
			b.Append("<div class=\"sub\">").Append(H(s.Start.ToString("dddd d MMMM yyyy", ci))).Append(" · ").Append(H(s.Start.ToString("HH:mm", ci))).Append("–").Append(H(s.End.ToString("HH:mm", ci))).Append(" · ").Append(H(Dur(dur)));
			if (s.MaxDelay > 0)
			{
				b.Append(" · ").Append(T("delay de hasta ")).Append(s.MaxDelay).Append(" s");
			}
			if (s.Games.Count > 0)
			{
				b.Append("<br>🎮 ").Append(H(string.Join(" · ", s.Games.Select(g => g.Name).ToArray())));
			}
			b.Append("</div>");

			// Cifras grandes
			b.Append("<div class=\"grid\">");
			Action<string, int, string, int> kpi = delegate(string label, int n, string extra, int col)
			{
				b.Append("<div class=\"k\"><div class=\"l\">").Append(T(label)).Append("</div><div class=\"n\">").Append(Num(n)).Append("</div>");
				if (!string.IsNullOrEmpty(extra)) b.Append("<div class=\"x\">").Append(extra).Append("</div>");
				if (col > 0) b.Append(Cmp(prev, col, n));
				b.Append("</div>");
			};
			int avg = s.Avg("total");
			kpi("Media de espectadores", Math.Max(0, avg), avg >= 0 ? T("sumando todas las plataformas") : T("sin datos (activa el chat conjunto)"), 8);
			kpi("Pico de espectadores", peak, s.Peak.Count > 1 ? T("sumando todas las plataformas") : T("sin datos (activa el chat conjunto)"), 2);
			kpi("Follows nuevos", s.Follows, "", 3);
			kpi("Subs", s.Subs, s.Gifters.Count > 0 ? T("incluye regalos") : "", 4);
			kpi("Bits", s.Bits, "", 5);
			kpi("Donaciones y Super Chats", s.Donations.Count, "", 0);
			kpi("Raids recibidos", s.Raids.Count, "", 0);
			kpi("Canjes de puntos", s.Redemptions, "", 0);
			kpi("Mensajes en el chat", s.Msgs, Num(s.Chatters.Count) + " " + T("personas distintas"), 6);
			kpi("Clips guardados", s.Clips, "", 0);
			b.Append("</div>");

			// Espectadores por plataforma
			b.Append(GamesAndChartHtml(s));
			b.Append("<h2>👀 ").Append(T("Espectadores por plataforma (pico y media)")).Append("</h2><div class=\"box\">");
			int maxPeak = 1;
			foreach (string p in new[] { "twitch", "kick", "youtube", "tiktok" })
			{
				int v;
				if (s.Peak.TryGetValue(p, out v)) maxPeak = Math.Max(maxPeak, v);
			}
			bool anyPeak = false;
			b.Append("<div class=\"bars\">");
			foreach (string p in new[] { "twitch", "kick", "youtube", "tiktok" })
			{
				int v;
				if (!s.Peak.TryGetValue(p, out v)) continue;
				anyPeak = true;
				int pa = s.Avg(p);
				b.Append("<div class=\"bar\" style=\"--c:").Append(PfColor[p]).Append("\"><span style=\"width:76px\">").Append(Pf(p)).Append("</span><span class=\"t\"><i style=\"width:").Append((v * 100 / maxPeak).ToString(CultureInfo.InvariantCulture)).Append("%\"></i></span><span class=\"am\" style=\"width:150px\">").Append(Num(v)).Append(pa >= 0 ? " <small style=\"color:var(--mu);font-weight:500\">· " + T("media") + " " + Num(pa) + "</small>" : "").Append("</span></div>");
			}
			b.Append("</div>");
			if (!anyPeak) b.Append("<div class=\"none\">").Append(T("Sin datos: los espectadores se leen con el chat conjunto activado.")).Append("</div>");
			b.Append("</div>");

			// Quién ha escrito más
			b.Append("<h2>💬 ").Append(T("Quién ha escrito más")).Append("</h2><div class=\"box\">");
			var top = s.Chatters.OrderByDescending(kv => kv.Value).Take(10).ToList();
			if (top.Count == 0) b.Append("<div class=\"none\">").Append(T("Nadie ha escrito (o el chat conjunto estaba apagado).")).Append("</div>");
			for (int i = 0; i < top.Count; i++)
			{
				string[] k = top[i].Key.Split('\u001f');
				b.Append("<div class=\"row\"><span class=\"rk\">").Append(i + 1).Append("</span>").Append(Pf(k[0])).Append("<span class=\"u\">").Append(H(k.Length > 1 ? k[1] : "")).Append("</span><span class=\"am\">").Append(Num(top[i].Value)).Append(" ").Append(T(top[i].Value == 1 ? "mensaje" : "mensajes")).Append("</span></div>");
			}
			if (s.MsgsBy.Count > 1)
			{
				b.Append("<div class=\"row\" style=\"color:var(--mu)\">");
				foreach (var kv in s.MsgsBy.OrderByDescending(kv => kv.Value)) b.Append(Pf(kv.Key)).Append(" ").Append(Num(kv.Value)).Append(" &nbsp; ");
				b.Append("</div>");
			}
			b.Append("</div>");

			// Donaciones, regalos y raids
			if (s.Donations.Count > 0)
			{
				b.Append("<h2>💸 ").Append(T("Donaciones, Super Chats y regalos")).Append("</h2><div class=\"box\">");
				foreach (string[] d in s.Donations) b.Append("<div class=\"row\">").Append(Pf(d[0])).Append("<span class=\"u\">").Append(H(d[1])).Append("</span><span class=\"am\">").Append(H(d[2])).Append("</span></div>");
				b.Append("</div>");
			}
			if (s.Gifters.Count > 0)
			{
				b.Append("<h2>🎁 ").Append(T("Quién ha regalado subs")).Append("</h2><div class=\"box\">");
				foreach (var kv in s.Gifters.OrderByDescending(kv => kv.Value).Take(10))
				{
					string[] k = kv.Key.Split('\u001f');
					b.Append("<div class=\"row\">").Append(Pf(k[0])).Append("<span class=\"u\">").Append(H(k.Length > 1 ? k[1] : "")).Append("</span><span class=\"am\">").Append(Num(kv.Value)).Append("</span></div>");
				}
				b.Append("</div>");
			}
			if (s.Raids.Count > 0)
			{
				b.Append("<h2>🚀 ").Append(T("Raids recibidos")).Append("</h2><div class=\"box\">");
				foreach (string[] r in s.Raids) b.Append("<div class=\"row\">").Append(Pf(r[0])).Append("<span class=\"u\">").Append(H(r[1])).Append("</span><span class=\"am\">").Append(H(r[2])).Append(" ").Append(T("espectadores")).Append("</span></div>");
				b.Append("</div>");
			}

			b.Append(StoryCardHtml(png, telegram));
			b.Append("<div class=\"foot\">").Append(T("Hecho por Vortexs Live")).Append(" ").Append(H(Version)).Append(" · ").Append(T("Se guarda en")).Append(" ").Append(H(SummaryDir)).Append("</div>");
			b.Append("</div></body></html>");
			return b.ToString();
		}

		// ---------- Juegos del día y gráfica de espectadores ----------

		private static string Pct(double part, double whole)
		{
			return whole > 0.0 ? ((int)Math.Round(part * 100.0 / whole)).ToString(CultureInfo.InvariantCulture) + " %" : "";
		}

		// Colores de los juegos en la gráfica (en el orden en que se jugaron).
		private static readonly string[] GameColors = new string[6] { "#c09afd", "#ffd166", "#25f4ee", "#ff8fab", "#4ade80", "#ff9f43" };

		private string GamesAndChartHtml(LiveStats s)
		{
			StringBuilder b = new StringBuilder();
			double totalSecs = s.Games.Sum(g => g.Secs);
			b.Append("<h2>🎮 ").Append(T("Juegos de hoy")).Append("</h2><div class=\"box\">");
			if (s.Games.Count == 0)
			{
				b.Append("<div class=\"none\">").Append(T("Sin datos: el juego se lee de Twitch o Kick con el chat conjunto activado.")).Append("</div>");
			}
			else
			{
				GameStat best = s.Games.Where(g => g.Avg >= 0 && g.Secs >= 600).OrderByDescending(g => g.Avg).FirstOrDefault();
				b.Append("<div style=\"overflow-x:auto\"><table class=\"gt\"><tr><th></th><th>").Append(T("Juego")).Append("</th><th>").Append(T("Tiempo")).Append("</th><th>").Append(T("Media")).Append("</th><th>").Append(T("Pico")).Append("</th><th>").Append(T("Follows")).Append("</th><th>").Append(T("Subs")).Append("</th><th>").Append(T("Mensajes")).Append("</th></tr>");
				for (int i = 0; i < s.Games.Count; i++)
				{
					GameStat g = s.Games[i];
					b.Append("<tr><td><i class=\"dot\" style=\"background:").Append(GameColors[i % GameColors.Length]).Append("\"></i></td><td class=\"gn\">").Append(H(g.Name));
					if (g == best && s.Games.Count > 1) b.Append(" <span class=\"best\">★ ").Append(T("mejor media")).Append("</span>");
					b.Append("</td><td>").Append(H(Dur(TimeSpan.FromSeconds(g.Secs)))).Append(" <small>").Append(Pct(g.Secs, totalSecs)).Append("</small></td>");
					b.Append("<td><b>").Append(g.Avg >= 0 ? Num(g.Avg) : "—").Append("</b></td><td>").Append(g.Peak >= 0 ? Num(g.Peak) : "—").Append("</td>");
					b.Append("<td>").Append(Num(g.Follows)).Append("</td><td>").Append(Num(g.Subs)).Append("</td><td>").Append(Num(g.Msgs)).Append("</td></tr>");
				}
				b.Append("</table></div>");
			}
			b.Append("</div>");

			// Gráfica: espectadores minuto a minuto, con el fondo del color del juego.
			List<object[]> tl = s.Timeline.Where(p => (int)p[1] >= 0).ToList();
			if (tl.Count >= 3)
			{
				double maxMin = Math.Max(1.0, (s.End - s.Start).TotalMinutes);
				int maxV = Math.Max(1, tl.Max(p => (int)p[1]));
				const int W = 940, Hh = 220, top = 14, bottom = 28;
				Func<double, double> X = m => 8.0 + m / maxMin * (W - 16);
				Func<int, double> Y = v => top + (Hh - top - bottom) * (1.0 - v / (double)maxV);
				StringBuilder svg = new StringBuilder();
				svg.Append("<svg viewBox=\"0 0 ").Append(W).Append(" ").Append(Hh).Append("\" width=\"100%\" role=\"img\" aria-label=\"").Append(T("Espectadores durante el directo")).Append("\">");
				// Tramos de cada juego.
				for (int i = 0; i < s.Timeline.Count; i++)
				{
					string g = (string)s.Timeline[i][2];
					int gi = s.Games.FindIndex(x => x.Name == g);
					if (gi < 0) continue;
					double m0 = (double)s.Timeline[i][0];
					double m1 = (i + 1 < s.Timeline.Count) ? (double)s.Timeline[i + 1][0] : maxMin;
					svg.Append("<rect x=\"").Append(F1(X(m0))).Append("\" y=\"").Append(top).Append("\" width=\"").Append(F1(Math.Max(0.5, X(m1) - X(m0)))).Append("\" height=\"").Append(Hh - top - bottom).Append("\" fill=\"").Append(GameColors[gi % GameColors.Length]).Append("\" opacity=\".09\"/>");
				}
				StringBuilder pts = new StringBuilder();
				foreach (object[] p in tl) pts.Append(F1(X((double)p[0]))).Append(",").Append(F1(Y((int)p[1]))).Append(" ");
				svg.Append("<polyline points=\"").Append(pts.ToString().Trim()).Append("\" fill=\"none\" stroke=\"#c09afd\" stroke-width=\"2.5\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
				svg.Append("<text x=\"8\" y=\"").Append(top + 2).Append("\" fill=\"#a1a1aa\" font-size=\"12\" dominant-baseline=\"hanging\">").Append(Num(maxV)).Append("</text>");
				svg.Append("<text x=\"8\" y=\"").Append(Hh - 8).Append("\" fill=\"#71717a\" font-size=\"12\">").Append(H(s.Start.ToString("HH:mm", CultureInfo.InvariantCulture))).Append("</text>");
				svg.Append("<text x=\"").Append(W - 8).Append("\" y=\"").Append(Hh - 8).Append("\" fill=\"#71717a\" font-size=\"12\" text-anchor=\"end\">").Append(H(s.End.ToString("HH:mm", CultureInfo.InvariantCulture))).Append("</text>");
				svg.Append("</svg>");
				b.Append("<h2>📈 ").Append(T("Espectadores durante el directo")).Append("</h2><div class=\"box\" style=\"padding:14px 12px 8px\">").Append(svg);
				if (s.Games.Count > 1)
				{
					b.Append("<div style=\"display:flex;gap:14px;flex-wrap:wrap;padding:6px 6px 4px;color:var(--mu);font-size:13px\">");
					for (int i = 0; i < s.Games.Count; i++) b.Append("<span><i class=\"dot\" style=\"background:").Append(GameColors[i % GameColors.Length]).Append("\"></i> ").Append(H(s.Games[i].Name)).Append("</span>");
					b.Append("</div>");
				}
				b.Append("</div>");
			}
			return b.ToString();
		}

		private static string F1(double v)
		{
			return v.ToString("0.0", CultureInfo.InvariantCulture);
		}

		// ---------- Imagen para historias (TikTok, Instagram…) ----------

		// Cifras de la imagen para historias.
		private RecapStory.Data RecapData(LiveStats s, int avg, int peak)
		{
			CultureInfo ci = I18n.English ? CultureInfo.GetCultureInfo("en-GB") : CultureInfo.GetCultureInfo("es-ES");
			ChatHub h = chatHub;
			string channel = (h != null) ? (h.TwitchChannel ?? "").Trim().TrimStart('@') : "";
			RecapStory.Data d = new RecapStory.Data
			{
				Title = I18n.Tr("RESUMEN DEL DIRECTO"),
				Date = s.Start.ToString("d MMMM yyyy", ci),
				Duration = Dur(s.End - s.Start),
				Games = string.Join(" · ", s.Games.OrderByDescending(g => g.Secs).Take(3).Select(g => g.Name).ToArray()),
				Thanks = I18n.Tr("¡Gracias por estar!"),
				Channel = channel.Length > 0 ? "twitch.tv/" + channel : ""
			};
			d.Stats[0] = new string[2] { "+" + Num(s.Follows), I18n.Tr("seguidores nuevos") };
			d.Stats[1] = new string[2] { avg >= 0 ? Num(avg) : "—", I18n.Tr("media de espectadores") };
			d.Stats[2] = new string[2] { Num(s.Subs), I18n.Tr("suscripciones") };
			d.Stats[3] = new string[2] { peak > 0 ? Num(peak) : "—", I18n.Tr("pico de espectadores") };
			return d;
		}

		// Dibuja y guarda la imagen; devuelve la ruta (o null si no se pudo).
		private string MakeRecapPng(LiveStats s, string file)
		{
			try
			{
				int peak;
				s.Peak.TryGetValue("total", out peak);
				Directory.CreateDirectory(Path.GetDirectoryName(file));
				using (Stream st = typeof(MainForm).Assembly.GetManifestResourceStream("StreamDelay.logo.png"))
				using (System.Drawing.Image logo = (st != null) ? System.Drawing.Image.FromStream(st) : null)
				using (System.Drawing.Bitmap bmp = RecapStory.Make(RecapData(s, s.Avg("total"), peak), logo))
				{
					bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
				}
				return file;
			}
			catch (Exception ex)
			{
				LogLater("No se pudo crear la imagen del resumen: " + ex.Message);
				return null;
			}
		}

		// Al móvil por el bot de Telegram del Anuncio (si está conectado): se guarda en la galería desde ahí.
		private void SendRecapToTelegram(LiveStats s, string png)
		{
			string token = xpTgToken, chat = xpTgChat;
			if (png == null || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chat))
			{
				return;
			}
			int peak;
			s.Peak.TryGetValue("total", out peak);
			int avg = s.Avg("total");
			string caption = "📊 <b>" + XpTelegram.Esc(I18n.Tr("Resumen del directo")) + "</b> · " + XpTelegram.Esc(s.Start.ToString("d/M/yyyy")) + "\n" +
				"➕ " + Num(s.Follows) + " " + XpTelegram.Esc(I18n.Tr("seguidores nuevos")) + "\n" +
				"👀 " + (avg >= 0 ? Num(avg) : "—") + " " + XpTelegram.Esc(I18n.Tr("de media")) + " · " + (peak > 0 ? Num(peak) : "—") + " " + XpTelegram.Esc(I18n.Tr("de pico")) + "\n" +
				"⭐ " + Num(s.Subs) + " " + XpTelegram.Esc(I18n.Tr("suscripciones")) + "\n\n" +
				XpTelegram.Esc(I18n.Tr("Mantén pulsada la imagen → Guardar en la galería, y súbela a tus historias."));
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					XpTelegram.Photo(token, chat, png, caption);
					LogLater("Imagen del resumen enviada a Telegram.");
				}
				catch (Exception ex)
				{
					LogLater("No se pudo mandar la imagen del resumen a Telegram: " + ex.Message);
				}
			});
		}

		// En la página: la imagen (dentro del propio archivo) con Descargar y Copiar.
		private string StoryCardHtml(string png, bool telegram)
		{
			StringBuilder b = new StringBuilder();
			b.Append("<h2>📱 ").Append(T("Imagen para tus historias")).Append("</h2><div class=\"box story\">");
			string uri = null;
			try
			{
				if (png != null && File.Exists(png)) uri = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(png));
			}
			catch
			{
			}
			if (uri == null)
			{
				b.Append("<div class=\"none\">").Append(T("No se pudo crear la imagen.")).Append("</div></div>");
				return b.ToString();
			}
			b.Append("<img id=\"card\" src=\"").Append(uri).Append("\" alt=\"\"><div class=\"sx\"><p>");
			b.Append(telegram ? T("Te la hemos mandado a Telegram: en el móvil, mantén pulsada la imagen → Guardar en la galería.") : T("Para tenerla en el móvil automáticamente, conecta Telegram en Ajustes → Anuncio."));
			b.Append("</p><p>").Append(T("También está en")).Append(" <code>").Append(H(png)).Append("</code></p>");
			b.Append("<a id=\"dl\" class=\"btn\" download=\"").Append(H(Path.GetFileName(png))).Append("\" href=\"").Append(uri).Append("\">⬇ ").Append(T("Descargar imagen")).Append("</a>");
			b.Append("<button id=\"cp\" class=\"g\">⧉ ").Append(T("Copiar imagen")).Append("</button><span id=\"msg\"></span></div></div>");
			b.Append("<script>document.getElementById('cp').onclick=function(){var m=document.getElementById('msg');");
			b.Append("if(!navigator.clipboard||!window.ClipboardItem){m.textContent=").Append(Js(I18n.Tr("Este navegador no deja copiar imágenes: descárgala."))).Append(";return;}");
			b.Append("fetch(document.getElementById('card').src).then(function(r){return r.blob();}).then(function(bl){return navigator.clipboard.write([new ClipboardItem({'image/png':bl})]);})");
			b.Append(".then(function(){m.textContent=").Append(Js(I18n.Tr("Copiada: pégala donde quieras."))).Append(";},function(){m.textContent=").Append(Js(I18n.Tr("Este navegador no deja copiar imágenes: descárgala."))).Append(";});};</script>");
			return b.ToString();
		}
	}
}
