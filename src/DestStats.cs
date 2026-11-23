// Ficha de destinos en directo: velocidad de subida, retraso de envío, tiempo en directo y
// reconexiones de cada plataforma. Se abre pulsando la franja de destinos de Directo mientras se emite.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private Form destStatsForm;

		// "6,2 Mb/s" o "850 kb/s".
		private static string RateText(int kbps)
		{
			if (kbps >= 1000)
			{
				return ((double)kbps / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " Mb/s";
			}
			return kbps + " kb/s";
		}

		private static string SecText(long ms)
		{
			return ((double)ms / 1000.0).ToString("0.0", CultureInfo.CurrentCulture) + " s";
		}

		private static string DurText(long ms)
		{
			long s = ms / 1000L;
			if (s >= 3600L)
			{
				return s / 3600L + ":" + (s / 60L % 60L).ToString("00") + ":" + (s % 60L).ToString("00");
			}
			return s / 60L + ":" + (s % 60L).ToString("00");
		}

		// Cada medio segundo (temporizador principal): velocidad de cada destino.
		private void SampleSinkRates()
		{
			foreach (Sink s in AllSinks())
			{
				try
				{
					s.SampleRate();
				}
				catch
				{
				}
			}
		}

		private void DestStatsDialog()
		{
			if (destStatsForm != null && !destStatsForm.IsDisposed)
			{
				destStatsForm.Activate();
				return;
			}
			Sink[] first = AllSinks();
			int rows = Math.Max(1, first.Length);
			Form f = DarkForm("Destinos en directo", 460, 96 + rows * 66 + 74);
			destStatsForm = f;
			PaintBox box = Box(f, 20f, 18f, 420f, 60 + rows * 66, PaintDestStats);
			Box(f, 20f, 86 + rows * 66, 420f, 64f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Retraso: lo que tarda en salir hacia la plataforma lo que ya tocaba enviar. Si pasa de 2–3 s, tu subida no da para todos los destinos: baja el bitrate en OBS o emite en menos sitios.", Theme.Body(7.5f), Theme.Muted, r);
			});
			Timer t = new Timer
			{
				Interval = 1000
			};
			t.Tick += delegate
			{
				box.Invalidate();
			};
			t.Start();
			f.FormClosed += delegate
			{
				t.Stop();
				t.Dispose();
				destStatsForm = null;
			};
			f.Show(this);
		}

		private void PaintDestStats(Graphics g, Rectangle r)
		{
			Theme.Quality(g);
			Sink[] sinks = AllSinks();
			Theme.DrawText(g, engine.Running ? "EMITIENDO" : "SERVICIO PARADO", Theme.Mono(7f), Theme.Muted, new RectangleF(0f, 0f, r.Width, S(20f)), StringAlignment.Near, StringAlignment.Center);
			int totalKbps = 0;
			foreach (Sink s in sinks)
			{
				totalKbps += s.RateKbps;
			}
			if (totalKbps > 0)
			{
				Theme.DrawText(g, I18n.Tr("SUBIDA TOTAL") + " " + RateText(totalKbps).ToUpperInvariant(), Theme.Mono(7f), Theme.Text, new RectangleF(0f, 0f, r.Width, S(20f)), StringAlignment.Far, StringAlignment.Center);
			}
			if (sinks.Length == 0)
			{
				Theme.DrawText(g, "Inicia el servicio para ver tus destinos aquí.", Theme.Body(8.5f), Theme.Muted, new RectangleF(0f, S(30f), r.Width, S(40f)), StringAlignment.Center, StringAlignment.Center);
				return;
			}
			float y = S(28f);
			float h = S(58f);
			foreach (Sink s in sinks)
			{
				RectangleF cell = new RectangleF(0.5f, y, (float)r.Width - 1.5f, h);
				bool live = s.State == SinkState.Live;
				long lag = live ? s.QueueAgeMs : 0L;
				bool slow = live && lag > 1500L;
				Color color;
				string state;
				switch (s.State)
				{
				case SinkState.Live:
					state = slow ? "VA CON RETRASO" : "EN DIRECTO";
					color = slow ? Theme.Yellow : Theme.Red;
					break;
				case SinkState.Connecting:
					state = "CONECTANDO";
					color = Theme.Yellow;
					break;
				case SinkState.Retrying:
					state = "REINTENTANDO";
					color = Color.FromArgb(255, 150, 60);
					break;
				case SinkState.NoKey:
					state = "FALTA CLAVE";
					color = Theme.Red;
					break;
				default:
					state = "ESPERANDO";
					color = Theme.Violet;
					break;
				}
				using (GraphicsPath path = Theme.Round(cell, S(12f)))
				{
					using (SolidBrush brush = new SolidBrush(live ? Color.FromArgb(22, color) : Theme.Input))
					{
						g.FillPath(brush, path);
					}
					using (Pen pen = new Pen(live ? Color.FromArgb(140, color) : Theme.Border))
					{
						g.DrawPath(pen, path);
					}
				}
				float dot = S(8f);
				RectangleF dotRect = new RectangleF(cell.X + (float)S(14f), cell.Y + (float)S(13f), dot, dot);
				using (SolidBrush brush2 = new SolidBrush(color))
				{
					g.FillEllipse(brush2, dotRect);
				}
				Theme.DrawText(g, s.D.Name, Theme.BodyBold(10f), Theme.Text, new RectangleF(dotRect.Right + (float)S(8f), cell.Y + (float)S(4f), S(220f), S(26f)), StringAlignment.Near, StringAlignment.Center);
				string right = state;
				if (live && s.Tracks > 1)
				{
					right = right + " · " + s.Tracks + " " + I18n.Tr("CAL.");
				}
				else if (live && s.Quality.Length > 0)
				{
					right = right + " · " + s.Quality.ToUpperInvariant();
				}
				else if (!live && s.Note.Length > 0)
				{
					right = s.Note.ToUpperInvariant();
				}
				Theme.DrawText(g, right, Theme.Mono(7f), Theme.Mix(color, Color.White, 0.25f), new RectangleF(cell.X + (float)S(160f), cell.Y + (float)S(4f), cell.Width - (float)S(174f), S(26f)), StringAlignment.Far, StringAlignment.Center);
				// Segunda línea: velocidad · retraso · tiempo · reconexiones.
				string info = live ? (RateText(s.RateKbps) + "   ·   " + I18n.Tr("retraso") + " " + SecText(lag) + "   ·   " + DurText(s.LiveForMs)) : "—";
				if (s.Reconnects > 0)
				{
					info = info + "   ·   " + s.Reconnects + " " + I18n.Tr((s.Reconnects == 1) ? "reconexión" : "reconexiones");
				}
				Theme.DrawText(g, info, Theme.Mono(7.5f), slow ? Theme.Yellow : Theme.Muted, new RectangleF(cell.X + (float)S(14f), cell.Y + (float)S(30f), cell.Width - (float)S(28f), S(20f)), StringAlignment.Near, StringAlignment.Center);
				y += h + (float)S(8f);
			}
		}
	}
}
