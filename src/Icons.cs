// Iconos de línea de la ventana (pestañas, botones, avisos). Se dibujan con trazos sobre una
// rejilla de 24x24, así se ven nítidos a cualquier tamaño y toman el color del texto.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace StreamDelay
{
	internal static class Icons
	{
		public static void Draw(Graphics g, string name, RectangleF r, Color c)
		{
			if (string.IsNullOrEmpty(name))
			{
				return;
			}
			float k = Math.Min(r.Width, r.Height) / 24f;
			float ox = r.X + (r.Width - 24f * k) / 2f, oy = r.Y + (r.Height - 24f * k) / 2f;
			GraphicsState st = g.Save();
			g.SmoothingMode = SmoothingMode.AntiAlias;
			g.TranslateTransform(ox, oy);
			g.ScaleTransform(k, k);
			using (Pen p = new Pen(c, 1.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
			using (SolidBrush b = new SolidBrush(c))
			{
				switch (name)
				{
				case "live":   // punto con ondas
					g.FillEllipse(b, 9.5f, 9.5f, 5f, 5f);
					g.DrawArc(p, 5.5f, 5.5f, 13f, 13f, 140, 80); g.DrawArc(p, 5.5f, 5.5f, 13f, 13f, -40, 80);
					g.DrawArc(p, 1.8f, 1.8f, 20.4f, 20.4f, 145, 70); g.DrawArc(p, 1.8f, 1.8f, 20.4f, 20.4f, -35, 70);
					break;
				case "chat":
					using (GraphicsPath gp = Theme.Round(new RectangleF(3f, 4f, 18f, 13f), 4f)) g.DrawPath(p, gp);
					g.DrawLines(p, new[] { new PointF(8f, 17f), new PointF(7f, 21f), new PointF(12f, 17f) });
					g.DrawLine(p, 8f, 9f, 16f, 9f); g.DrawLine(p, 8f, 12.5f, 13f, 12.5f);
					break;
				case "layers":
					g.DrawPolygon(p, new[] { new PointF(12f, 3.5f), new PointF(21f, 8.5f), new PointF(12f, 13.5f), new PointF(3f, 8.5f) });
					g.DrawLines(p, new[] { new PointF(3f, 12.5f), new PointF(12f, 17.5f), new PointF(21f, 12.5f) });
					g.DrawLines(p, new[] { new PointF(3f, 16.5f), new PointF(12f, 21.5f), new PointF(21f, 16.5f) });
					break;
				case "remote":   // mando
					using (GraphicsPath gp = Theme.Round(new RectangleF(2.5f, 7f, 19f, 11f), 5.5f)) g.DrawPath(p, gp);
					g.DrawLine(p, 6.5f, 12.5f, 10.5f, 12.5f); g.DrawLine(p, 8.5f, 10.5f, 8.5f, 14.5f);
					g.FillEllipse(b, 14.6f, 10.2f, 2.2f, 2.2f); g.FillEllipse(b, 16.8f, 12.6f, 2.2f, 2.2f);
					break;
				case "film":
					using (GraphicsPath gp = Theme.Round(new RectangleF(3f, 4.5f, 18f, 15f), 2.5f)) g.DrawPath(p, gp);
					g.DrawLine(p, 7.5f, 4.5f, 7.5f, 19.5f); g.DrawLine(p, 16.5f, 4.5f, 16.5f, 19.5f);
					g.DrawLine(p, 3f, 12f, 7.5f, 12f); g.DrawLine(p, 16.5f, 12f, 21f, 12f);
					break;
				case "text":
					g.DrawLine(p, 5f, 5.5f, 19f, 5.5f); g.DrawLine(p, 12f, 5.5f, 12f, 19f);
					g.DrawLine(p, 9f, 19f, 15f, 19f);
					break;
				case "gear":
					g.DrawEllipse(p, 9f, 9f, 6f, 6f);
					for (int i = 0; i < 8; i++)
					{
						double a = i * Math.PI / 4.0;
						g.DrawLine(p, 12f + (float)Math.Cos(a) * 5.8f, 12f + (float)Math.Sin(a) * 5.8f, 12f + (float)Math.Cos(a) * 9f, 12f + (float)Math.Sin(a) * 9f);
					}
					g.DrawEllipse(p, 5.6f, 5.6f, 12.8f, 12.8f);
					break;
				case "list":
					for (int i = 0; i < 3; i++)
					{
						float y = 6.5f + i * 5.5f;
						g.FillEllipse(b, 3.6f, y - 1.2f, 2.4f, 2.4f);
						g.DrawLine(p, 9f, y, 20.5f, y);
					}
					break;
				case "copy":
					using (GraphicsPath gp = Theme.Round(new RectangleF(8.5f, 8.5f, 12f, 12f), 2.5f)) g.DrawPath(p, gp);
					g.DrawLines(p, new[] { new PointF(15.5f, 8.5f), new PointF(15.5f, 5.5f), new PointF(14f, 3.5f), new PointF(5.5f, 3.5f), new PointF(3.5f, 5.5f), new PointF(3.5f, 14f), new PointF(5.5f, 15.5f), new PointF(8.5f, 15.5f) });
					break;
				case "external":
					g.DrawLines(p, new[] { new PointF(10f, 4.5f), new PointF(5f, 4.5f), new PointF(4f, 5.5f), new PointF(4f, 19f), new PointF(5f, 20f), new PointF(18.5f, 20f), new PointF(19.5f, 19f), new PointF(19.5f, 14f) });
					g.DrawLine(p, 11f, 13f, 20f, 4f); g.DrawLines(p, new[] { new PointF(14f, 4f), new PointF(20f, 4f), new PointF(20f, 10f) });
					break;
				case "play":
					using (GraphicsPath gp = new GraphicsPath())
					{
						gp.AddPolygon(new[] { new PointF(8f, 5f), new PointF(19f, 12f), new PointF(8f, 19f) });
						g.FillPath(b, gp);
					}
					break;
				case "stop":
					using (GraphicsPath gp = Theme.Round(new RectangleF(6.5f, 6.5f, 11f, 11f), 2.5f)) g.FillPath(b, gp);
					break;
				case "sound":
					g.DrawPolygon(p, new[] { new PointF(3.5f, 9.5f), new PointF(7.5f, 9.5f), new PointF(12.5f, 5f), new PointF(12.5f, 19f), new PointF(7.5f, 14.5f), new PointF(3.5f, 14.5f) });
					g.DrawArc(p, 11f, 8f, 7f, 8f, -50, 100); g.DrawArc(p, 11f, 4.5f, 11f, 15f, -50, 100);
					break;
				case "check":
					g.DrawLines(p, new[] { new PointF(5f, 12.5f), new PointF(10f, 17.5f), new PointF(19.5f, 7f) });
					break;
				case "x":
					g.DrawLine(p, 6.5f, 6.5f, 17.5f, 17.5f); g.DrawLine(p, 17.5f, 6.5f, 6.5f, 17.5f);
					break;
				case "chart":
					g.DrawLine(p, 4f, 20f, 20f, 20f);
					g.DrawLine(p, 7f, 16f, 7f, 12f); g.DrawLine(p, 12f, 16f, 12f, 6f); g.DrawLine(p, 17f, 16f, 17f, 9f);
					break;
				case "book":
					g.DrawLines(p, new[] { new PointF(12f, 6.5f), new PointF(8f, 4.5f), new PointF(3f, 4.5f), new PointF(3f, 18f), new PointF(8f, 18f), new PointF(12f, 20f), new PointF(16f, 18f), new PointF(21f, 18f), new PointF(21f, 4.5f), new PointF(16f, 4.5f), new PointF(12f, 6.5f), new PointF(12f, 20f) });
					break;
				case "download":
					g.DrawLine(p, 12f, 3.5f, 12f, 15f); g.DrawLines(p, new[] { new PointF(7f, 10.5f), new PointF(12f, 15.5f), new PointF(17f, 10.5f) });
					g.DrawLines(p, new[] { new PointF(4f, 15.5f), new PointF(4f, 20f), new PointF(20f, 20f), new PointF(20f, 15.5f) });
					break;
				case "warning":
					g.DrawPolygon(p, new[] { new PointF(12f, 3.5f), new PointF(21.5f, 20f), new PointF(2.5f, 20f) });
					g.DrawLine(p, 12f, 9.5f, 12f, 14f); g.FillEllipse(b, 10.8f, 16f, 2.4f, 2.4f);
					break;
				case "info":
					g.DrawEllipse(p, 3f, 3f, 18f, 18f);
					g.DrawLine(p, 12f, 11f, 12f, 16.5f); g.FillEllipse(b, 10.8f, 6.8f, 2.4f, 2.4f);
					break;
				case "mini":   // ventana pequeña encima de otra
					using (GraphicsPath gp = Theme.Round(new RectangleF(3f, 4f, 18f, 14f), 2.5f)) g.DrawPath(p, gp);
					using (GraphicsPath gp = Theme.Round(new RectangleF(11.5f, 11f, 7.5f, 5f), 1.2f)) g.FillPath(b, gp);
					break;
				case "expand":
					g.DrawLines(p, new[] { new PointF(14f, 4f), new PointF(20f, 4f), new PointF(20f, 10f) });
					g.DrawLines(p, new[] { new PointF(10f, 20f), new PointF(4f, 20f), new PointF(4f, 14f) });
					g.DrawLine(p, 20f, 4f, 13.5f, 10.5f); g.DrawLine(p, 4f, 20f, 10.5f, 13.5f);
					break;
				case "minus":
					g.DrawLine(p, 6f, 12f, 18f, 12f);
					break;
				case "plus":
					g.DrawLine(p, 6f, 12f, 18f, 12f); g.DrawLine(p, 12f, 6f, 12f, 18f);
					break;
				case "eye":
					g.DrawBezier(p, 2.5f, 12f, 6f, 5.5f, 18f, 5.5f, 21.5f, 12f); g.DrawBezier(p, 2.5f, 12f, 6f, 18.5f, 18f, 18.5f, 21.5f, 12f);
					g.DrawEllipse(p, 9.3f, 9.3f, 5.4f, 5.4f);
					break;
				}
			}
			g.Restore(st);
		}
	}
}
