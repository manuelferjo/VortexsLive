// Imagen del resumen para historias (1080×1920): fecha, juegos, duración y cuatro cifras (seguidores
// nuevos, media de espectadores, suscripciones y pico). La dibuja Vortexs Live al terminar el directo,
// la guarda en Imágenes\Vortexs Live\Resúmenes, la pone en la página del resumen y, si está conectado
// el bot de Telegram (Ajustes → Anuncio), la manda al móvil para guardarla en la galería.
// Todo lo importante queda entre y=250 e y=1670: arriba y abajo TikTok e Instagram ponen sus botones.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;

namespace StreamDelay
{
	internal static class RecapStory
	{
		public const int W = 1080;

		public const int H = 1920;

		public class Data
		{
			public string Title = "RESUMEN DEL DIRECTO";
			public string Date = "";
			public string Games = "";
			public string Duration = "";
			public string Thanks = "¡Gracias por estar!";
			public string Channel = "";
			// { cifra, etiqueta } × 4
			public string[][] Stats = new string[4][];
		}

		public static string Folder
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Vortexs Live", "Resúmenes"); }
		}

		private static FontFamily Display()
		{
			try { return Theme.Display(12f).FontFamily; } catch { return new FontFamily("Segoe UI"); }
		}

		private static FontFamily Body()
		{
			try { return Theme.BodyBold(12f).FontFamily; } catch { return new FontFamily("Segoe UI"); }
		}

		private static Font Fit(Graphics g, FontFamily fam, string s, float maxW, float size, float min)
		{
			FontStyle st = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
			while (true)
			{
				Font f = new Font(fam, size, st, GraphicsUnit.Pixel);
				if (size <= min || g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width <= maxW)
				{
					return f;
				}
				f.Dispose();
				size -= 2f;
			}
		}

		// Texto centrado en x con la línea base aproximada en y (arriba del texto = y - alto).
		private static void Center(Graphics g, string s, Font f, Brush b, float cx, float top)
		{
			StringFormat fmt = StringFormat.GenericTypographic;
			float w = g.MeasureString(s, f, PointF.Empty, fmt).Width;
			g.DrawString(s, f, b, cx - w / 2f, top, fmt);
		}

		private static void Glow(Graphics g, float cx, float cy, float r, Color c)
		{
			using (GraphicsPath p = new GraphicsPath())
			{
				p.AddEllipse(cx - r, cy - r, r * 2f, r * 2f);
				using (PathGradientBrush b = new PathGradientBrush(p))
				{
					b.CenterColor = c;
					b.SurroundColors = new Color[1] { Color.FromArgb(0, c) };
					g.FillPath(b, p);
				}
			}
		}

		public static Bitmap Make(Data d, Image logo)
		{
			Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
				g.Clear(Color.FromArgb(11, 11, 15));
				Glow(g, 140f, 260f, 900f, Color.FromArgb(86, 192, 154, 253));
				Glow(g, 980f, 1700f, 900f, Color.FromArgb(56, 255, 209, 102));
				Glow(g, 1000f, 560f, 520f, Color.FromArgb(30, 192, 154, 253));
				FontFamily disp = Display();
				FontFamily body = Body();
				Color violet = Color.FromArgb(192, 154, 253);
				Color yellow = Color.FromArgb(255, 209, 102);
				if (logo != null)
				{
					g.DrawImage(logo, new RectangleF(W / 2f - 80f, 250f, 160f, 160f));
				}
				using (Font f = new Font(body, 34f, FontStyle.Bold, GraphicsUnit.Pixel))
				using (SolidBrush b = new SolidBrush(Color.FromArgb(161, 161, 170)))
				{
					// Un poco de espacio entre letras.
					Center(g, string.Join(" ", d.Title.ToCharArray().Select(ch => ch.ToString())), f, b, W / 2f, 440f);
				}
				using (Font f = Fit(g, disp, d.Date, 900f, 88f, 40f))
				{
					SizeF sz = g.MeasureString(d.Date, f, PointF.Empty, StringFormat.GenericTypographic);
					RectangleF r = new RectangleF(W / 2f - sz.Width / 2f, 500f, Math.Max(1f, sz.Width), Math.Max(1f, sz.Height));
					using (LinearGradientBrush b = new LinearGradientBrush(r, violet, yellow, LinearGradientMode.Horizontal))
					{
						Center(g, d.Date, f, b, W / 2f, 500f);
					}
				}
				string line2 = d.Games.Length > 0 ? d.Games : d.Duration;
				using (Font f = Fit(g, body, line2, 900f, 42f, 24f))
				using (SolidBrush b = new SolidBrush(Color.FromArgb(212, 212, 216)))
				{
					Center(g, line2, f, b, W / 2f, 610f);
				}
				if (d.Games.Length > 0)
				{
					using (Font f = new Font(body, 34f, FontStyle.Regular, GraphicsUnit.Pixel))
					using (SolidBrush b = new SolidBrush(Color.FromArgb(113, 113, 122)))
					{
						Center(g, d.Duration, f, b, W / 2f, 664f);
					}
				}
				// Cuatro tarjetas: misma letra para las cuatro etiquetas (la que quepa en la más larga).
				float top = 745f, gw = 440f, gh = 360f, gap = 36f, left = (W - 2f * gw - gap) / 2f;
				float ls = 40f;
				for (int k = 0; k < 4; k++)
				{
					using (Font f = Fit(g, body, d.Stats[k][1], gw - 60f, ls, 22f))
					{
						ls = Math.Min(ls, f.Size);
					}
				}
				for (int i = 0; i < 4; i++)
				{
					float cx = left + (i % 2) * (gw + gap);
					float cy = top + (i / 2) * (gh + gap);
					bool v = i % 3 == 0;
					using (GraphicsPath p = Theme.Round(new RectangleF(cx, cy, gw, gh), 44f))
					{
						using (SolidBrush fill = new SolidBrush(Color.FromArgb(225, 21, 21, 28)))
						{
							g.FillPath(fill, p);
						}
						using (Pen pen = new Pen(v ? Color.FromArgb(140, violet) : Color.FromArgb(115, yellow), 3f))
						{
							g.DrawPath(pen, p);
						}
					}
					using (Font f = Fit(g, disp, d.Stats[i][0], gw - 60f, 140f, 50f))
					using (SolidBrush b = new SolidBrush(v ? violet : yellow))
					{
						Center(g, d.Stats[i][0], f, b, cx + gw / 2f, cy + 215f - f.Size * 0.92f);
					}
					using (Font f = new Font(body, ls, FontStyle.Bold, GraphicsUnit.Pixel))
					using (SolidBrush b = new SolidBrush(Color.FromArgb(228, 228, 231)))
					{
						Center(g, d.Stats[i][1], f, b, cx + gw / 2f, cy + 252f);
					}
				}
				using (Font f = Fit(g, disp, d.Thanks, 940f, 60f, 30f))
				using (SolidBrush b = new SolidBrush(Color.FromArgb(244, 244, 245)))
				{
					Center(g, d.Thanks, f, b, W / 2f, 1530f);
				}
				if (d.Channel.Length > 0)
				{
					using (Font f = Fit(g, body, d.Channel, 940f, 42f, 24f))
					using (SolidBrush b = new SolidBrush(violet))
					{
						Center(g, d.Channel, f, b, W / 2f, 1612f);
					}
				}
			}
			return bmp;
		}
	}
}
