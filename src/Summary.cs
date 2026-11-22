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
					ChatHub h = chatHub;
					if (h != null)
					{
						int total = 0;
						bool any = false;
						Peak("twitch", h.TwitchViewers, ref total, ref any);
						Peak("kick", h.KickViewers, ref total, ref any);
						Peak("youtube", h.YouTubeViewers, ref total, ref any);
						Peak("tiktok", h.TikTokViewers, ref total, ref any);
						if (any)
						{
							PeakSet("total", total);
						}
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

		private void Peak(string p, int v, ref int total, ref bool any)
		{
			if (v >= 0)
			{
				total += v;
				any = true;
				PeakSet(p, v);
			}
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
				if (it.t == "msg")
				{
					stats.Msgs++;
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
					break;
				case "sub":
				case "resub":
				case "member":
					stats.Subs++;
					break;
				case "gift":
					stats.Subs++;
					Add(stats.Gifters, p + "\u001f" + user, 1);
					break;
				case "giftbomb":
				case "giftmember":
				{
					int n = Math.Max(1, LeadingInt(it.amount));
					stats.Subs += n;
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
				File.WriteAllText(file, SummaryHtml(s, prev, false), new UTF8Encoding(false));
				AppendHistory(s);
				lastSummaryFile = file;
				LogLater("Resumen del directo guardado: " + file);
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
						File.WriteAllText(tmp, SummaryHtml(now, LastHistory(), true), new UTF8Encoding(false));
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
					I18n.Show(this, "Todavía no hay ningún resumen. Se crea solo al terminar cada directo (de 3 minutos o más) y se abre en el navegador.", "Resúmenes");
					return;
				}
				Http.OpenBrowser(last);
			}
			catch (Exception ex)
			{
				AppendLog("No se pudo abrir el resumen: " + ex.Message);
			}
		}

		// historial.csv: fecha;minutos;pico;follows;subs;bits;mensajes;personas
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
				File.WriteAllText(HistoryFile, "fecha;minutos;pico;follows;subs;bits;mensajes;personas\r\n", new UTF8Encoding(false));
			}
			int peak;
			s.Peak.TryGetValue("total", out peak);
			File.AppendAllText(HistoryFile, s.Start.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ";" + (int)(s.End - s.Start).TotalMinutes + ";" + peak + ";" + s.Follows + ";" + s.Subs + ";" + s.Bits + ";" + s.Msgs + ";" + s.Chatters.Count + "\r\n", new UTF8Encoding(false));
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

		private string SummaryHtml(LiveStats s, string[] prev, bool ongoing)
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
			b.Append(".none{color:var(--dim);padding:12px 0}.foot{margin-top:34px;color:var(--dim);font-size:12.5px}</style></head><body><div class=\"w\">");

			b.Append("<h1>📊 <b>").Append(T("Resumen del directo")).Append("</b>").Append(ongoing ? "<span class=\"live\">● " + T("EN DIRECTO") + "</span>" : "").Append("</h1>");
			b.Append("<div class=\"sub\">").Append(H(s.Start.ToString("dddd d MMMM yyyy", ci))).Append(" · ").Append(H(s.Start.ToString("HH:mm", ci))).Append("–").Append(H(s.End.ToString("HH:mm", ci))).Append(" · ").Append(H(Dur(dur)));
			if (s.MaxDelay > 0)
			{
				b.Append(" · ").Append(T("delay de hasta ")).Append(s.MaxDelay).Append(" s");
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
			b.Append("<h2>👀 ").Append(T("Pico de espectadores por plataforma")).Append("</h2><div class=\"box\">");
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
				b.Append("<div class=\"bar\" style=\"--c:").Append(PfColor[p]).Append("\"><span style=\"width:76px\">").Append(Pf(p)).Append("</span><span class=\"t\"><i style=\"width:").Append((v * 100 / maxPeak).ToString(CultureInfo.InvariantCulture)).Append("%\"></i></span><span class=\"am\">").Append(Num(v)).Append("</span></div>");
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

			b.Append("<div class=\"foot\">").Append(T("Hecho por Vortexs Live")).Append(" ").Append(H(Version)).Append(" · ").Append(T("Se guarda en")).Append(" ").Append(H(SummaryDir)).Append("</div>");
			b.Append("</div></body></html>");
			return b.ToString();
		}
	}
}
