// Errores sin controlar: en vez de la ventana de .NET, Vortexs Live guarda un informe en
// %APPDATA%\VortexsLive\errores\ y enseña un aviso propio (con "Copiar informe" para mandártelo).
// Si falla nada más abrir una versión recién actualizada, vuelve sola a la anterior (el .exe.old que
// deja la actualización) y no la vuelve a instalar.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal static class Crash
	{
		// Segundos tras abrir en los que un error al arrancar cuenta como "esta versión no arranca".
		public const int StartupSeconds = 60;

		private static readonly Stopwatch upTime = Stopwatch.StartNew();

		private static DateTime lastDialog = DateTime.MinValue;

		private static int inHandler;

		public static string Version = "";

		public static string Dir
		{
			get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VortexsLive", "errores"); }
		}

		public static void Install()
		{
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
			{
				Handle(e.Exception, false);
			};
			AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
			{
				Handle(e.ExceptionObject as Exception, true);
			};
		}

		// Informe de texto: versión, Windows, .NET, tiempo abierto y el error completo.
		public static string Report(Exception ex)
		{
			StringBuilder b = new StringBuilder();
			b.AppendLine("Vortexs Live " + Version + " · informe de error");
			b.AppendLine("Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			b.AppendLine("Windows: " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " (64 bits)" : " (32 bits)"));
			b.AppendLine(".NET: " + Environment.Version);
			b.AppendLine("Abierto desde hace: " + (int)upTime.Elapsed.TotalSeconds + " s");
			b.AppendLine();
			b.AppendLine(ex == null ? "(error desconocido)" : ex.ToString());
			return b.ToString();
		}

		public static string Save(Exception ex)
		{
			try
			{
				Directory.CreateDirectory(Dir);
				string file = Path.Combine(Dir, "error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
				File.WriteAllText(file, Report(ex), Encoding.UTF8);
				// Se guardan los 20 últimos.
				string[] all = Directory.GetFiles(Dir, "error-*.txt");
				Array.Sort(all);
				for (int i = 0; i < all.Length - 20; i++)
				{
					try { File.Delete(all[i]); } catch { }
				}
				return file;
			}
			catch
			{
				return null;
			}
		}

		private static void Handle(Exception ex, bool fatal)
		{
			if (Interlocked.Exchange(ref inHandler, 1) == 1)
			{
				return;
			}
			try
			{
				string file = Save(ex);
				if (Program.SelfTest)
				{
					Console.Error.WriteLine(Report(ex));
					Environment.Exit(3);
				}
				// Versión recién actualizada que falla al arrancar: a la anterior.
				if (upTime.Elapsed.TotalSeconds < StartupSeconds && TryRollback())
				{
					Environment.Exit(2);
				}
				if (fatal)
				{
					// Se va a cerrar: al volver a abrir se avisa (MainForm mira errores\ultimo.txt).
					MarkLast(file);
					return;
				}
				if ((DateTime.UtcNow - lastDialog).TotalSeconds < 60)
				{
					return;
				}
				lastDialog = DateTime.UtcNow;
				Dialog(ex, file);
			}
			catch
			{
			}
			finally
			{
				Interlocked.Exchange(ref inHandler, 0);
			}
		}

		public static void MarkLast(string file)
		{
			try
			{
				if (file != null)
				{
					File.WriteAllText(Path.Combine(Dir, "ultimo.txt"), file);
				}
			}
			catch
			{
			}
		}

		// El informe del cierre anterior (si lo hubo) y se olvida.
		public static string TakeLast()
		{
			try
			{
				string mark = Path.Combine(Dir, "ultimo.txt");
				if (!File.Exists(mark))
				{
					return null;
				}
				string file = File.ReadAllText(mark).Trim();
				File.Delete(mark);
				return File.Exists(file) ? file : null;
			}
			catch
			{
				return null;
			}
		}

		// Si hay .exe.old (deja la actualización) se pone en su sitio, esta versión pasa a .fallida y se abre
		// la anterior diciéndole qué versión falló para que no la vuelva a instalar sola.
		private static bool TryRollback()
		{
			try
			{
				string exe = Application.ExecutablePath;
				string old = exe + ".old";
				// Solo si esta versión se instaló hace poco (el .old de una actualización de hace semanas no cuenta).
				if (!File.Exists(old) || (DateTime.Now - File.GetCreationTime(exe)).TotalDays > 7.0)
				{
					return false;
				}
				string bad = exe + ".fallida";
				if (File.Exists(bad))
				{
					File.Delete(bad);
				}
				File.Move(exe, bad);
				File.Move(old, exe);
				Process.Start(new ProcessStartInfo(exe, "--esperar=" + Process.GetCurrentProcess().Id + " --revertido=" + Version)
				{
					UseShellExecute = false,
					WorkingDirectory = Path.GetDirectoryName(exe)
				});
				return true;
			}
			catch
			{
				return false;
			}
		}

		// Aviso propio: el programa sigue abierto (si estás en directo, la emisión no se corta).
		private static void Dialog(Exception ex, string file)
		{
			string report = Report(ex);
			using (Form f = new Form())
			{
				f.Text = "Vortexs Live";
				f.FormBorderStyle = FormBorderStyle.FixedDialog;
				f.MaximizeBox = false;
				f.MinimizeBox = false;
				f.StartPosition = FormStartPosition.CenterScreen;
				f.TopMost = true;
				f.BackColor = Theme.Bg;
				f.ForeColor = Theme.Text;
				f.ClientSize = new Size(Theme.S(440f), Theme.S(250f));
				Label title = new Label
				{
					Text = I18n.Tr("Algo ha fallado en Vortexs Live"),
					Font = Theme.BodyBold(12f),
					ForeColor = Theme.Text,
					AutoSize = false,
					Bounds = new Rectangle(Theme.S(20f), Theme.S(18f), Theme.S(400f), Theme.S(28f))
				};
				Label text = new Label
				{
					Text = I18n.Tr("El programa sigue abierto y, si estás en directo, la emisión sigue. Hemos guardado un informe del error: mándalo para que se pueda arreglar (pulsa «Copiar informe» y pégalo en un mensaje)."),
					Font = Theme.Body(9f),
					ForeColor = Theme.Muted,
					AutoSize = false,
					Bounds = new Rectangle(Theme.S(20f), Theme.S(52f), Theme.S(400f), Theme.S(70f))
				};
				Label detail = new Label
				{
					Text = (ex == null) ? "" : (ex.GetType().Name + ": " + ex.Message),
					Font = Theme.Mono(7.5f),
					ForeColor = Theme.Dim,
					AutoSize = false,
					AutoEllipsis = true,
					Bounds = new Rectangle(Theme.S(20f), Theme.S(126f), Theme.S(400f), Theme.S(40f))
				};
				Button copy = new Button { Text = I18n.Tr("Copiar informe"), FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Surface, Bounds = new Rectangle(Theme.S(20f), Theme.S(186f), Theme.S(130f), Theme.S(40f)) };
				Button open = new Button { Text = I18n.Tr("Abrir carpeta"), FlatStyle = FlatStyle.Flat, ForeColor = Theme.Text, BackColor = Theme.Surface, Bounds = new Rectangle(Theme.S(158f), Theme.S(186f), Theme.S(120f), Theme.S(40f)) };
				Button ok = new Button { Text = I18n.Tr("Seguir"), FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Theme.Violet, Bounds = new Rectangle(Theme.S(286f), Theme.S(186f), Theme.S(134f), Theme.S(40f)), DialogResult = DialogResult.OK };
				copy.Click += delegate
				{
					try
					{
						Clipboard.SetText(report);
						copy.Text = I18n.Tr("¡Copiado!");
					}
					catch
					{
					}
				};
				open.Click += delegate
				{
					try
					{
						if (file != null)
						{
							Process.Start("explorer.exe", "/select,\"" + file + "\"");
						}
						else
						{
							Process.Start("explorer.exe", "\"" + Dir + "\"");
						}
					}
					catch
					{
					}
				};
				f.Controls.AddRange(new Control[] { title, text, detail, copy, open, ok });
				f.AcceptButton = ok;
				f.ShowDialog();
			}
		}
	}
}
