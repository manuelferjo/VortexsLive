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
				else if (a.StartsWith("--actualizado="))
				{
					UpdatedTo = a.Substring(14);
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
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Theme.Init();
			Application.Run(new MainForm());
		}
	}
}
