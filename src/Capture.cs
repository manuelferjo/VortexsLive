// Capturas de la interfaz para revisarla sin abrir el programa a mano: VortexsLive.exe --capturas=carpeta
// recorre el asistente, la lista de atajos y todas las pestañas, guarda un PNG de cada una y se cierra.
// Lo usa GitHub (Compilar) para que se pueda ver cómo queda cada versión. No toca la red ni la licencia.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private void StartCaptures()
		{
			string dir = Program.CaptureDir;
			Directory.CreateDirectory(dir);
			plan = Plan.Full;
			List<KeyValuePair<string, Action>> shots = new List<KeyValuePair<string, Action>>();
			for (int i = 0; i < WizSteps; i++)
			{
				int step = i;
				shots.Add(new KeyValuePair<string, Action>("01-asistente-" + (i + 1), delegate
				{
					if (!wiz.Visible)
					{
						OpenWizard();
					}
					WizGo(step);
				}));
			}
			shots.Add(new KeyValuePair<string, Action>("02-atajos", delegate
			{
				wiz.Visible = false;
				GoPage(PageLive);
				ToggleKeysSheet();
			}));
			shots.Add(new KeyValuePair<string, Action>("03-directo", delegate
			{
				ToggleKeysSheet();
				GoPage(PageLive);
			}));
			shots.Add(new KeyValuePair<string, Action>("04-directo-cortar-confirmar", delegate
			{
				ConfirmClick(btnStart, "Pulsa otra vez para detener", delegate { });
			}));
			shots.Add(new KeyValuePair<string, Action>("05-titulo", delegate { Disarm(btnStart); RefreshStatus(); GoPage(PageTitle); }));
			shots.Add(new KeyValuePair<string, Action>("06-chat", delegate { GoPage(PageChat); }));
			shots.Add(new KeyValuePair<string, Action>("07-overlays", delegate { GoPage(PageOverlays); }));
			shots.Add(new KeyValuePair<string, Action>("08-mando", delegate { GoPage(PageMando); }));
			shots.Add(new KeyValuePair<string, Action>("09-clips", delegate { GoPage(PageClips); }));
			shots.Add(new KeyValuePair<string, Action>("09b-overlays-alertas", delegate { ovPick = 3; ApplyOverlayPick(); GoPage(PageOverlays); }));
			for (int i = 0; i < 3; i++)
			{
				int pick = i;
				shots.Add(new KeyValuePair<string, Action>("1" + i + "-ajustes-" + (i + 1), delegate
				{
					setPick = pick;
					GoPage(PageSettings);
					ApplySettingsPick();
				}));
			}
			shots.Add(new KeyValuePair<string, Action>("13-registro", delegate { GoPage(PageLog); }));
			shots.Add(new KeyValuePair<string, Action>("14-aviso-deshacer", delegate
			{
				GoPage(PageLive);
				Toast("Clip a la papelera: clip-2026-11-23.mp4", "info", "Deshacer", delegate { });
			}));
			int n = 0;
			Timer t = new Timer
			{
				Interval = 900
			};
			bool pending = false;
			t.Tick += delegate
			{
				if (pending)
				{
					Snap(Path.Combine(dir, shots[n].Key + ".png"));
					pending = false;
					n++;
					if (n >= shots.Count)
					{
						t.Stop();
						Environment.Exit(0);
					}
					return;
				}
				try
				{
					shots[n].Value();
				}
				catch (Exception ex)
				{
					Console.WriteLine(shots[n].Key + ": " + ex);
				}
				pending = true;
			};
			t.Start();
		}

		// La ventana tal como se ve en pantalla (y si no se puede, dibujada en un bitmap).
		private void Snap(string path)
		{
			Rectangle r = RectangleToScreen(ClientRectangle);
			// Los avisos salen debajo de la ventana: se incluye un poco más por abajo si hay alguno.
			using (Bitmap b = new Bitmap(r.Width, r.Height))
			{
				bool ok = false;
				try
				{
					using (Graphics g = Graphics.FromImage(b))
					{
						g.CopyFromScreen(r.Location, Point.Empty, r.Size);
					}
					ok = b.GetPixel(r.Width / 2, r.Height / 2).ToArgb() != Color.Black.ToArgb() || b.GetPixel(S(30f), S(30f)).ToArgb() != Color.Black.ToArgb();
				}
				catch
				{
				}
				if (!ok)
				{
					DrawToBitmap(b, new Rectangle(0, 0, r.Width, r.Height));
				}
				b.Save(path, ImageFormat.Png);
			}
		}
	}
}
