// Que los puertos de Vortexs Live (8935 de los overlays/Mando/chat, el RTMP de OBS…) no se queden
// "pillados" por otros procesos.
//
// En Windows, los sockets de .NET se heredan por defecto: cada proceso que lanza la app con
// UseShellExecute = false (ffmpeg, el reinicio tras actualizar) se lleva una copia del socket
// que escucha en 8935. Si ese proceso sigue vivo cuando la app se cierra o se reinicia, el puerto
// sigue ocupado y la app nueva tiene que usar el 8936: cambian las URL de los overlays en OBS.
//
// NoInherit: los sockets de la app ya no se heredan.
// CloseInherited: al arrancar tras una actualización, cierra los sockets que escuchan y que se
// han heredado de la versión anterior (las de antes de este arreglo sí los pasaban).
using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace StreamDelay
{
	internal static class Sockets
	{
		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern bool GetHandleInformation(IntPtr h, out uint flags);

		[DllImport("ws2_32.dll")]
		private static extern int WSAStartup(ushort version, byte[] data);

		[DllImport("ws2_32.dll")]
		private static extern int WSACleanup();

		[DllImport("ws2_32.dll")]
		private static extern int getsockname(IntPtr s, byte[] name, ref int len);

		[DllImport("ws2_32.dll")]
		private static extern int getsockopt(IntPtr s, int level, int name, ref int value, ref int len);

		[DllImport("ws2_32.dll")]
		private static extern int closesocket(IntPtr s);

		private const uint HANDLE_FLAG_INHERIT = 1;
		private const int SOL_SOCKET = 0xFFFF;
		private const int SO_ACCEPTCONN = 0x0002;

		public static void NoInherit(Socket s)
		{
			try
			{
				if (s != null)
				{
					SetHandleInformation(s.Handle, HANDLE_FLAG_INHERIT, 0);
				}
			}
			catch
			{
			}
		}

		public static void NoInherit(TcpListener l)
		{
			if (l != null)
			{
				NoInherit(l.Server);
			}
		}

		public static void NoInherit(TcpClient c)
		{
			if (c != null)
			{
				NoInherit(c.Client);
			}
		}

		// Cierra los sockets de este proceso que están escuchando. Solo se llama al principio de Main,
		// cuando la app todavía no ha abierto ninguno: los que haya son heredados. Devuelve cuántos.
		public static int CloseInherited()
		{
			int closed = 0;
			try
			{
				byte[] wsa = new byte[1024];
				if (WSAStartup(0x0202, wsa) != 0)
				{
					return 0;
				}
				try
				{
					byte[] addr = new byte[128];
					for (long v = 4; v <= 0x10000; v += 4)
					{
						IntPtr h = new IntPtr(v);
						uint flags;
						if (!GetHandleInformation(h, out flags))
						{
							continue;
						}
						int len = addr.Length;
						if (getsockname(h, addr, ref len) != 0)
						{
							continue;   // no es un socket
						}
						int listening = 0, olen = 4;
						if (getsockopt(h, SOL_SOCKET, SO_ACCEPTCONN, ref listening, ref olen) != 0 || listening == 0)
						{
							continue;
						}
						if (closesocket(h) == 0)
						{
							closed++;
						}
					}
				}
				finally
				{
					WSACleanup();
				}
			}
			catch
			{
			}
			return closed;
		}
	}
}
