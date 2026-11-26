using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamDelay
{
	internal static class Program
	{
		[DllImport("user32.dll")]
		private static extern bool SetProcessDPIAware();

		// Versión a la que se acaba de actualizar (la pasa el actualizador al reiniciar); vacío si no.
		internal static string UpdatedTo = "";

		// --capturas=carpeta: guarda capturas de la interfaz y se cierra (ver Capture.cs).
		internal static string CaptureDir;

		// --autoprueba: abre el programa, comprueba que arranca sin errores y se cierra (0 = bien). Lo
		// usa GitHub antes de crear el ZIP y el instalador: una versión que no abre no se publica.
		internal static bool SelfTest;

		// --revertido=4.25: esa versión falló al abrir y se ha vuelto a esta (ver Crash.cs).
		internal static string RevertedFrom = "";

		// --fallo-de-prueba: falla nada más abrir (GitHub comprueba así la vuelta a la versión anterior).
		internal static bool FailOnPurpose;

		// Sin red, licencia ni actualizaciones (capturas y autoprueba de GitHub).
		internal static bool Headless
		{
			get { return CaptureDir != null || SelfTest; }
		}

		[STAThread]
		private static void Main(string[] args)
		{
			foreach (string a in args)
			{
				// Tras una actualización: esperar a que la versión anterior se cierre del todo
				// (libera el puerto del Mando y el de OBS) antes de arrancar.
				if (a.StartsWith("--esperar="))
				{
					int pid;
					if (int.TryParse(a.Substring(10), out pid))
					{
						try { Process.GetProcessById(pid).WaitForExit(20000); } catch { }
					}
					// Las versiones de antes de la 4.12 pasaban sus puertos a la nueva: se sueltan
					// para que el chat, los overlays y el Mando sigan en el 8935.
					Sockets.CloseInherited();
				}
				else if (a.StartsWith("--capturas="))
				{
					CaptureDir = a.Substring(11);
				}
				else if (a.StartsWith("--actualizado="))
				{
					UpdatedTo = a.Substring(14);
				}
				else if (a == "--fallo-de-prueba")
				{
					FailOnPurpose = true;
				}
				else if (a == "--autoprueba")
				{
					SelfTest = true;
				}
				else if (a.StartsWith("--revertido="))
				{
					RevertedFrom = a.Substring(12);
				}
			}
			try
			{
				if (Environment.OSVersion.Version.Major >= 6)
				{
					SetProcessDPIAware();
				}
			}
			catch
			{
			}
			Crash.Install();
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Theme.Init();
			Application.Run(new MainForm());
		}
	}
}
