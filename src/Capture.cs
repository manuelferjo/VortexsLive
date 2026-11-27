// Capturas de la interfaz para revisarla sin abrir el programa a mano: VortexsLive.exe --capturas=carpeta
// recorre el asistente, la lista de atajos y todas las pestañas, guarda un PNG de cada una y se cierra.
// Lo usa GitHub (Compilar) para que se pueda ver cómo queda cada versión. No toca la red ni la licencia.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
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
			shots.Add(new KeyValuePair<string, Action>("09c-personalizar-alertas", delegate { AlertCustomDialog(); }));
			shots.Add(new KeyValuePair<string, Action>("09d-overlays-vertical", delegate
			{
				foreach (Form o in OwnedForms)
				{
					o.Close();
				}
				ovVertical = true;
				ovHint.Invalidate();
			}));
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
			// Un resumen de ejemplo (cifras inventadas) para revisar la página en GitHub.
			try
			{
				LiveStats demo = new LiveStats { Start = DateTime.Now.AddHours(-4.2), End = DateTime.Now, Follows = 148, Subs = 37, Bits = 2500, Msgs = 5321, Redemptions = 64, Clips = 6, MaxDelay = 60 };
				Random rnd = new Random(7);
				string[] gamesDemo = new string[3] { "Just Chatting", "VALORANT", "Just Chatting" };
				for (int m = 0; m < 252; m++)
				{
					string g = m < 25 ? gamesDemo[0] : (m < 220 ? gamesDemo[1] : gamesDemo[2]);
					int v = (int)(400 + Math.Min(m, 60) * 18 + Math.Sin(m / 9.0) * 120 + rnd.Next(80) + (m > 160 && m < 175 ? 900 : 0));
					demo.Timeline.Add(new object[3] { (double)m, v, g });
					GameStat gs = demo.Games.FirstOrDefault(x => x.Name == g);
					if (gs == null) { gs = new GameStat { Name = g }; demo.Games.Add(gs); }
					gs.Secs += 60; gs.VSum += v * 60.0; gs.VSecs += 60; gs.Peak = Math.Max(gs.Peak, v); gs.Msgs += 21; if (m % 2 == 0) gs.Follows++;
					demo.VSum["total"] = (demo.VSum.ContainsKey("total") ? demo.VSum["total"] : 0) + v * 60.0;
					demo.VSecs["total"] = (demo.VSecs.ContainsKey("total") ? demo.VSecs["total"] : 0) + 60.0;
					demo.VSum["twitch"] = (demo.VSum.ContainsKey("twitch") ? demo.VSum["twitch"] : 0) + v * 0.8 * 60.0;
					demo.VSecs["twitch"] = (demo.VSecs.ContainsKey("twitch") ? demo.VSecs["twitch"] : 0) + 60.0;
					demo.VSum["kick"] = (demo.VSum.ContainsKey("kick") ? demo.VSum["kick"] : 0) + v * 0.2 * 60.0;
					demo.VSecs["kick"] = (demo.VSecs.ContainsKey("kick") ? demo.VSecs["kick"] : 0) + 60.0;
				}
				demo.Games[0].Subs = 9; demo.Games[1].Subs = 25; demo.Games[1].Follows = 120;
				demo.Peak["total"] = 2410; demo.Peak["twitch"] = 1930; demo.Peak["kick"] = 520;
				demo.Chatters["twitch\u001fLaura"] = 312; demo.Chatters["twitch\u001fRafa"] = 201; demo.Chatters["kick\u001fPepe"] = 99;
				string demoPng = MakeRecapPng(demo, Path.Combine(dir, "resumen-demo.png"));
				File.WriteAllText(Path.Combine(dir, "resumen-demo.html"), SummaryHtml(demo, new string[9] { "", "", "2100", "120", "30", "", "", "", "1100" }, false, demoPng, true), new System.Text.UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				Console.WriteLine("resumen-demo: " + ex);
			}
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
			// Si hay una ventana abierta encima (por ejemplo Personalizar alertas), la foto es de esa.
			Form target = this;
			foreach (Form o in OwnedForms)
			{
				if (o.Visible && !(o is ToastForm))
				{
					target = o;
				}
			}
			Rectangle r = target.RectangleToScreen(target.ClientRectangle);
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
					target.DrawToBitmap(b, new Rectangle(0, 0, r.Width, r.Height));
				}
				b.Save(path, ImageFormat.Png);
			}
		}
	}
}
