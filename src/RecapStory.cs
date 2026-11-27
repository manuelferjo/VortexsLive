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
			// Foto de perfil del canal (redonda, arriba) y colores sacados de ella; null = logo y colores de Vortexs.
			public Image Avatar;
			public Palette Colors;
		}

		// Dos colores de acento (cifras, bordes, brillos) y el fondo.
		public class Palette
		{
			public Color A = Color.FromArgb(192, 154, 253);
			public Color B = Color.FromArgb(255, 209, 102);
			public Color Bg = Color.FromArgb(11, 11, 15);
		}

		// ---------- Colores de la foto de perfil ----------

		private static void ToHsl(Color c, out float h, out float s, out float l)
		{
			h = c.GetHue();
			s = c.GetSaturation();
			l = c.GetBrightness();
		}

		private static Color FromHsl(float h, float s, float l)
		{
			h = ((h % 360f) + 360f) % 360f;
			float c = (1f - Math.Abs(2f * l - 1f)) * s;
			float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
			float m = l - c / 2f;
			float r = 0f, g = 0f, b = 0f;
			if (h < 60f) { r = c; g = x; }
			else if (h < 120f) { r = x; g = c; }
			else if (h < 180f) { g = c; b = x; }
			else if (h < 240f) { g = x; b = c; }
			else if (h < 300f) { r = x; b = c; }
			else { r = c; b = x; }
			return Color.FromArgb(Clamp255((r + m) * 255f), Clamp255((g + m) * 255f), Clamp255((b + m) * 255f));
		}

		private static int Clamp255(float v)
		{
			return Math.Max(0, Math.Min(255, (int)Math.Round(v)));
		}

		// Que se lea bien sobre fondo oscuro: color vivo y claro.
		private static Color Readable(Color c)
		{
			float h, s, l;
			ToHsl(c, out h, out s, out l);
			return FromHsl(h, Math.Max(0.55f, Math.Min(0.95f, s)), Math.Max(0.64f, Math.Min(0.78f, l)));
		}

		// Los dos tonos con más peso de la foto (los grises, blancos y negros no cuentan). Si la foto es
		// casi en blanco y negro, se quedan los colores de Vortexs.
		public static Palette FromImage(Image img)
		{
			Palette p = new Palette();
			if (img == null)
			{
				return p;
			}
			const int N = 64, Bins = 24;
			double[] weight = new double[Bins], sr = new double[Bins], sg = new double[Bins], sb = new double[Bins];
			double total = 0.0;
			using (Bitmap small = new Bitmap(N, N, PixelFormat.Format32bppArgb))
			{
				using (Graphics g = Graphics.FromImage(small))
				{
					g.InterpolationMode = InterpolationMode.HighQualityBilinear;
					g.DrawImage(img, new Rectangle(0, 0, N, N));
				}
				for (int y = 0; y < N; y++)
				{
					for (int x = 0; x < N; x++)
					{
						Color c = small.GetPixel(x, y);
						if (c.A < 200) continue;
						float max = Math.Max(c.R, Math.Max(c.G, c.B)) / 255f, min = Math.Min(c.R, Math.Min(c.G, c.B)) / 255f;
						float sat = max <= 0f ? 0f : (max - min) / max;
						if (max < 0.18f || sat < 0.22f) continue;
						double w = sat * max;
						int bin = (int)(c.GetHue() / 360f * Bins) % Bins;
						weight[bin] += w; sr[bin] += c.R * w; sg[bin] += c.G * w; sb[bin] += c.B * w;
						total += w;
					}
				}
			}
			if (total < N * N * 0.03)
			{
				return p;
			}
			// Se suaviza con los vecinos para que un degradado no se parta en dos.
			double[] sm = new double[Bins];
			for (int i = 0; i < Bins; i++) sm[i] = weight[i] + 0.5 * (weight[(i + Bins - 1) % Bins] + weight[(i + 1) % Bins]);
			int a = 0;
			for (int i = 1; i < Bins; i++) if (sm[i] > sm[a]) a = i;
			int bBin = -1;
			for (int i = 0; i < Bins; i++)
			{
				int dist = Math.Min(Math.Abs(i - a), Bins - Math.Abs(i - a));
				if (dist >= 3 && sm[i] >= sm[a] * 0.15 && (bBin < 0 || sm[i] > sm[bBin])) bBin = i;
			}
			Func<int, Color> avg = delegate(int i)
			{
				double w = weight[i];
				if (w <= 0.0) { int j = (weight[(i + 1) % Bins] > weight[(i + Bins - 1) % Bins]) ? (i + 1) % Bins : (i + Bins - 1) % Bins; i = j; w = weight[i]; }
				return Color.FromArgb(Clamp255((float)(sr[i] / w)), Clamp255((float)(sg[i] / w)), Clamp255((float)(sb[i] / w)));
			};
			Color ca = avg(a);
			Color cb;
			if (bBin >= 0)
			{
				cb = avg(bBin);
			}
			else
			{
				// Un solo color: el segundo, el mismo tono girado un poco.
				float h, s, l;
				ToHsl(ca, out h, out s, out l);
				cb = FromHsl(h + 38f, s, l);
			}
			p.A = Readable(ca);
			p.B = Readable(cb);
			float ha, sa, la;
			ToHsl(ca, out ha, out sa, out la);
			p.Bg = FromHsl(ha, 0.3f, 0.05f);
			return p;
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
			Palette pal = d.Colors ?? new Palette();
			Color violet = pal.A;
			Color yellow = pal.B;
			return Draw(d, logo, pal, violet, yellow);
		}

		// violet / yellow: los dos colores de acento (con los de Vortexs, morado y amarillo).
		private static Bitmap Draw(Data d, Image logo, Palette pal, Color violet, Color yellow)
		{
			Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
				g.Clear(pal.Bg);
				Glow(g, 140f, 260f, 900f, Color.FromArgb(86, violet));
				Glow(g, 980f, 1700f, 900f, Color.FromArgb(60, yellow));
				Glow(g, 1000f, 560f, 520f, Color.FromArgb(30, violet));
				FontFamily disp = Display();
				FontFamily body = Body();
				if (d.Avatar != null)
				{
					// Foto de perfil redonda con un aro de los dos colores.
					RectangleF ring = new RectangleF(W / 2f - 86f, 244f, 172f, 172f);
					using (LinearGradientBrush rb = new LinearGradientBrush(ring, violet, yellow, LinearGradientMode.ForwardDiagonal))
					{
						g.FillEllipse(rb, ring);
					}
					RectangleF pic = new RectangleF(W / 2f - 78f, 252f, 156f, 156f);
					using (GraphicsPath cp = new GraphicsPath())
					{
						cp.AddEllipse(pic);
						g.SetClip(cp);
						g.DrawImage(d.Avatar, pic);
						g.ResetClip();
					}
				}
				else if (logo != null)
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
