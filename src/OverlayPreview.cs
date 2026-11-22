// Vista previa de los overlays dentro de la app (panel de la derecha con la ventana ancha):
// una pantalla 16:9 en miniatura con el overlay elegido, sus ajustes y los colores de
// "Personalizar". Es un dibujo aproximado: la página de verdad es la de OBS.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private static readonly string[] OvNames = new string[8] { "Delay", "Espectadores", "Chat", "Alertas", "Destacado", "Objetivo", "Eventos", "Encuesta" };
		private static readonly string[] OvPaths = new string[8] { "delay", "viewers", "?overlay=1", "alertas", "destacado", "objetivo", "eventos", "encuesta" };

		public string PickedOverlayName()
		{
			return I18n.Tr(OvNames[Math.Max(0, Math.Min(7, ovPick))]);
		}

		public void OpenPickedOverlay()
		{
			string u = OverlayUrl(OvPaths[Math.Max(0, Math.Min(7, ovPick))]);
			if (u != null)
			{
				Http.OpenBrowser(u);
			}
		}

		private static Color HexColor(string h, Color def)
		{
			try
			{
				if (!string.IsNullOrEmpty(h) && h.StartsWith("#") && h.Length >= 7)
				{
					return Color.FromArgb(int.Parse(h.Substring(1, 2), NumberStyles.HexNumber), int.Parse(h.Substring(3, 2), NumberStyles.HexNumber), int.Parse(h.Substring(5, 2), NumberStyles.HexNumber));
				}
			}
			catch
			{
			}
			return def;
		}

		// Recuadro con el estilo de los overlays (fondo, borde en degradado, esquinas).
		private void MockFrame(Graphics g, RectangleF r, float k, Color a1, Color a2, Color bgc, bool pill)
		{
			float rad = pill ? r.Height / 2f : (ovStyle.Shape == "rect" ? 4f * k : ovStyle.Shape == "pill" ? r.Height / 2f : 22f * k);
			rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
			using (GraphicsPath p = Theme.Round(r, Math.Max(1f, rad)))
			{
				if (ovStyle.Glow)
				{
					Card.Glow(g, RectangleF.Inflate(r, 18f * k, 18f * k), Color.FromArgb(40, a1));
				}
				using (SolidBrush b = new SolidBrush(bgc))
				{
					g.FillPath(b, p);
				}
				if (ovStyle.Border > 0)
				{
					using (LinearGradientBrush lb = new LinearGradientBrush(RectangleF.Inflate(r, 1f, 1f), a1, a2, LinearGradientMode.ForwardDiagonal))
					using (Pen pen = new Pen(lb, Math.Max(1f, ovStyle.Border * k * 1.4f)))
					{
						g.DrawPath(pen, p);
					}
				}
			}
		}

		private static void MockText(Graphics g, string s, float size, Color c, RectangleF r, StringAlignment h, bool bold)
		{
			if (size < 3f)
			{
				return;
			}
			using (Font f = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
			using (StringFormat sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
			using (SolidBrush b = new SolidBrush(c))
			{
				g.DrawString(s, f, b, r, sf);
			}
		}

		public void PaintOverlayMock(Graphics g, RectangleF cv)
		{
			float k = cv.Width / 1920f;
			Color a1 = HexColor(ovStyle.Accent, Theme.Red);
			Color a2 = ovStyle.Gradient ? HexColor(ovStyle.Accent2, a1) : a1;
			Color bgc = Color.FromArgb((int)(255 * Math.Max(0.2, Math.Min(1.0, ovStyle.Opacity / 100.0))), HexColor(ovStyle.Background, Color.FromArgb(17, 17, 22)));
			Color tx = HexColor(ovStyle.Text, Theme.Text);
			Color lbl = Color.FromArgb(160, tx);
			Func<float, float, float, float, RectangleF> R = (x, y, w, h) => new RectangleF(cv.X + x * k, cv.Y + y * k, w * k, h * k);
			switch (ovPick)
			{
			case 0:
			{
				// Delay: arriba a la derecha.
				float s = ovDelayScale;
				RectangleF r = R(1920 - 40 - 330 * s, 36, 330 * s, 84 * s);
				MockFrame(g, r, k, a1, a2, bgc, true);
				bool on = engine.Enabled && engine.TargetMs > 0;
				MockText(g, on ? "DELAY" : "EN DIRECTO", 20 * k * s, lbl, new RectangleF(r.X + 26 * k * s, r.Y, r.Width * 0.45f, r.Height), StringAlignment.Near, true);
				MockText(g, on ? (engine.TargetMs / 1000) + " s" : "Sin delay", 40 * k * s, tx, new RectangleF(r.X, r.Y, r.Width - 26 * k * s, r.Height), StringAlignment.Far, true);
				break;
			}
			case 1:
			{
				// Espectadores: una pastilla por plataforma elegida.
				string[] ids = new string[5] { "twitch", "kick", "youtube", "total", "tiktok" };
				Color[] cols = new Color[5] { Color.FromArgb(169, 112, 255), Color.FromArgb(83, 252, 24), Color.FromArgb(255, 59, 59), Theme.Red, Color.FromArgb(37, 244, 238) };
				ChatHub h = chatHub;
				int[] vals = new int[5]
				{
					h != null && h.TwitchViewers >= 0 ? h.TwitchViewers : 128, h != null && h.KickViewers >= 0 ? h.KickViewers : 42,
					h != null && h.YouTubeViewers >= 0 ? h.YouTubeViewers : 17, 0, h != null && h.TikTokViewers >= 0 ? h.TikTokViewers : 33
				};
				vals[3] = vals[0] + vals[1] + vals[2] + vals[4];
				int[] order = new int[5] { 3, 0, 1, 2, 4 };
				float kk = vwScale;
				List<int> show = new List<int>();
				foreach (int i in order) if (vwShow[i]) show.Add(i);
				float pw = 150 * kk, ph = 56 * kk, gap = 12 * kk;
				bool right = vwPos == "tr" || vwPos == "br", bottom = vwPos == "br" || vwPos == "bl";
				float totalW = vwVertical ? pw : show.Count * pw + Math.Max(0, show.Count - 1) * gap;
				float totalH = vwVertical ? show.Count * ph + Math.Max(0, show.Count - 1) * gap : ph;
				float x0 = right ? 1920 - 40 - totalW : 40, y0 = bottom ? 1080 - 36 - totalH : (vwPos == "tr" ? 108 * ovDelayScale + 20 : 36);
				for (int n = 0; n < show.Count; n++)
				{
					int i = show[n];
					RectangleF r = vwVertical ? R(x0, y0 + n * (ph + gap), pw, ph) : R(x0 + n * (pw + gap), y0, pw, ph);
					MockFrame(g, r, k, a1, a2, bgc, true);
					using (SolidBrush b = new SolidBrush(cols[i]))
					{
						g.FillEllipse(b, r.X + 16 * k * kk, r.Y + r.Height / 2f - 10 * k * kk, 20 * k * kk, 20 * k * kk);
					}
					MockText(g, vals[i].ToString("N0", CultureInfo.CurrentCulture), 28 * k * kk, tx, new RectangleF(r.X, r.Y, r.Width - 18 * k * kk, r.Height), StringAlignment.Far, true);
				}
				break;
			}
			case 2:
			{
				// Chat en pantalla: abajo a la izquierda.
				string[][] lines = new string[][] { new[] { "Laura", "¡Vaya jugada!" }, new[] { "KickFan", "saludos desde Kick" }, new[] { "ytfan", "¿qué me he perdido?" }, new[] { "Pepe", "GG, qué nivel" } };
				Color[] nc = new Color[4] { Color.FromArgb(255, 69, 0), Color.FromArgb(83, 252, 24), Color.FromArgb(255, 99, 99), Color.FromArgb(30, 144, 255) };
				for (int i = 0; i < lines.Length; i++)
				{
					RectangleF r = R(40, 1080 - 60 - (lines.Length - i) * 74, 620, 62);
					if (ovStyle.ChatBubbles) MockFrame(g, r, k, a1, a2, bgc, false);
					float nw = (lines[i][0].Length + 1) * 15 * k;
					MockText(g, lines[i][0] + ":", 26 * k, nc[i], new RectangleF(r.X + 16 * k, r.Y, nw + 20 * k, r.Height), StringAlignment.Near, true);
					MockText(g, lines[i][1], 26 * k, tx, new RectangleF(r.X + 26 * k + nw, r.Y, r.Width - nw - 30 * k, r.Height), StringAlignment.Near, false);
				}
				break;
			}
			case 3:
			{
				// Alerta.
				float y = alertPos == "center" ? 540 - 80 : alertPos == "bottom" ? 1080 - 90 - 160 : 70;
				RectangleF r = R(960 - 360, y, 720, 160);
				MockFrame(g, r, k, a1, a2, bgc, false);
				using (SolidBrush b = new SolidBrush(Color.FromArgb(60, a1)))
				{
					g.FillEllipse(b, r.X + 34 * k, r.Y + 36 * k, 88 * k, 88 * k);
				}
				Icons.Draw(g, "live", new RectangleF(r.X + 50 * k, r.Y + 52 * k, 56 * k, 56 * k), a1);
				MockText(g, "NUEVO SEGUIDOR", 18 * k, lbl, new RectangleF(r.X + 150 * k, r.Y + 24 * k, 400 * k, 28 * k), StringAlignment.Near, true);
				MockText(g, "Pepe", 50 * k, a1, new RectangleF(r.X + 150 * k, r.Y + 50 * k, 500 * k, 60 * k), StringAlignment.Near, true);
				MockText(g, "te sigue", 26 * k, tx, new RectangleF(r.X + 150 * k, r.Y + 106 * k, 400 * k, 34 * k), StringAlignment.Near, false);
				using (SolidBrush b = new SolidBrush(a2))
				{
					g.FillRectangle(b, r.X, r.Bottom - 5 * k, r.Width * 0.6f, 5 * k);
				}
				break;
			}
			case 4:
			{
				// Mensaje destacado.
				float w = 900, h = 130;
				float x = featPos == "bc" ? 960 - w / 2 : 60, y = featPos == "tl" ? 70 : 1080 - 80 - h;
				RectangleF r = R(x, y, w, h);
				MockFrame(g, r, k, a1, a2, bgc, false);
				MockText(g, "Laura", 28 * k, Color.FromArgb(255, 69, 0), new RectangleF(r.X + 30 * k, r.Y + 18 * k, 400 * k, 40 * k), StringAlignment.Near, true);
				MockText(g, "¿Cuál ha sido el juego que más te ha costado?", 32 * k, tx, new RectangleF(r.X + 30 * k, r.Y + 62 * k, r.Width - 60 * k, 48 * k), StringAlignment.Near, false);
				break;
			}
			case 5:
			{
				// Objetivo.
				int target, count;
				string title;
				lock (wgLock)
				{
					target = Math.Max(1, goalTarget);
					count = goalCount;
					title = goalTitle;
				}
				RectangleF r = R(960 - 410, goalPos == "bottom" ? 1080 - 40 - 110 : 40, 820, 110);
				MockFrame(g, r, k, a1, a2, bgc, false);
				MockText(g, title, 28 * k, tx, new RectangleF(r.X + 26 * k, r.Y + 14 * k, 520 * k, 40 * k), StringAlignment.Near, true);
				MockText(g, count + " / " + target, 30 * k, tx, new RectangleF(r.X, r.Y + 14 * k, r.Width - 26 * k, 40 * k), StringAlignment.Far, true);
				RectangleF bar = new RectangleF(r.X + 26 * k, r.Y + 66 * k, r.Width - 52 * k, 22 * k);
				using (GraphicsPath p = Theme.Round(bar, bar.Height / 2f))
				using (SolidBrush b = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
				{
					g.FillPath(b, p);
				}
				float pct = Math.Min(1f, (float)count / target);
				if (pct > 0f)
				{
					RectangleF fill = new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * pct), bar.Height);
					using (GraphicsPath p = Theme.Round(fill, bar.Height / 2f))
					using (LinearGradientBrush lb = new LinearGradientBrush(RectangleF.Inflate(fill, 1f, 1f), a1, a2, LinearGradientMode.Horizontal))
					{
						g.FillPath(lb, p);
					}
				}
				break;
			}
			case 6:
			{
				// Cinta de eventos.
				RectangleF r = R(0, tickPos == "top" ? 0 : 1080 - 58, 1920, 58);
				using (SolidBrush b = new SolidBrush(bgc))
				{
					g.FillRectangle(b, r);
				}
				RectangleF lab = new RectangleF(r.X, r.Y, 300 * k, r.Height);
				using (LinearGradientBrush lb = new LinearGradientBrush(RectangleF.Inflate(lab, 1f, 1f), a1, a2, LinearGradientMode.Horizontal))
				{
					g.FillRectangle(lb, lab);
				}
				MockText(g, "ÚLTIMOS EVENTOS", 17 * k, Color.FromArgb(17, 17, 17), lab, StringAlignment.Center, true);
				string[] ev = new string[] { "Pepe", "Ana · sub", "Laura · 500 bits", "Nocturno · raid 42", "ytfan · 5,00 €" };
				float x = lab.Right + 30 * k;
				foreach (string s in ev)
				{
					MockText(g, s, 24 * k, tx, new RectangleF(x, r.Y, 320 * k, r.Height), StringAlignment.Near, true);
					x += 330 * k;
				}
				break;
			}
			case 7:
			{
				// Encuesta (la de verdad si hay una; si no, un ejemplo).
				string q = "¿Qué jugamos después?";
				string[] opts = new string[] { "Minecraft", "Fortnite", "Valorant" };
				int[] votes = new int[] { 14, 9, 5 };
				string pos;
				lock (wgLock)
				{
					pos = pollPos;
					if (pollOpts.Count >= 2)
					{
						q = pollQ;
						opts = pollOpts.ToArray();
						votes = (int[])pollVotes.Clone();
					}
				}
				float h = 120 + opts.Length * 61 + 40;
				RectangleF r = R(pos == "right" ? 1920 - 50 - 560 : 50, 150, 560, h);
				MockFrame(g, r, k, a1, a2, bgc, false);
				MockText(g, q, 30 * k, tx, new RectangleF(r.X + 26 * k, r.Y + 40 * k, r.Width - 52 * k, 46 * k), StringAlignment.Near, true);
				int total = 0;
				foreach (int v in votes) total += v;
				for (int i = 0; i < opts.Length; i++)
				{
					RectangleF row = new RectangleF(r.X + 26 * k, r.Y + (110 + i * 61) * k, r.Width - 52 * k, 52 * k);
					using (GraphicsPath p = Theme.Round(row, 12 * k))
					using (SolidBrush b = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
					{
						g.FillPath(b, p);
					}
					float pct = total > 0 ? (float)votes[i] / total : 0f;
					if (pct > 0f)
					{
						RectangleF fill = new RectangleF(row.X, row.Y, row.Width * pct, row.Height);
						using (GraphicsPath p = Theme.Round(fill, Math.Min(12 * k, fill.Width / 2f)))
						using (SolidBrush b = new SolidBrush(Color.FromArgb(80, a1)))
						{
							g.FillPath(b, p);
						}
					}
					MockText(g, (i + 1) + "  " + opts[i], 22 * k, tx, new RectangleF(row.X + 16 * k, row.Y, row.Width * 0.7f, row.Height), StringAlignment.Near, true);
					MockText(g, (int)Math.Round(pct * 100) + " %", 22 * k, tx, new RectangleF(row.X, row.Y, row.Width - 16 * k, row.Height), StringAlignment.Far, true);
				}
				break;
			}
			}
		}
	}
}
