// Servidor RTMP local donde se conecta OBS (rtmp://127.0.0.1:1935/app).
// Escucha en IPv4 e IPv6 (solo en este PC, o en toda la red con "OBS en otro PC") y crea una
// RtmpSession con su propio hilo por cada conexión.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace StreamDelay
{
	internal class RtmpServer
	{
		public Action<string> Log = delegate
		{
		};

		// OBS empieza a emitir / llega un paquete (tipo, marca de tiempo, datos) / OBS termina.
		public Action<RtmpSession> PublishStart = delegate
		{
		};

		public Action<RtmpSession, byte, uint, byte[]> Packet = delegate
		{
		};

		public Action<RtmpSession> PublishEnd = delegate
		{
		};

		private readonly List<TcpListener> listeners = new List<TcpListener>();

		private readonly List<RtmpSession> sessions = new List<RtmpSession>();

		private volatile bool running;

		// lan = aceptar conexiones de otros PCs. Si no se puede abrir el puerto en IPv4, lanza IOException
		// (IPv6 es opcional).
		public void Start(int port, bool lan)
		{
			running = true;
			IPAddress[] addresses = (lan ? new IPAddress[2]
			{
				IPAddress.Any,
				IPAddress.IPv6Any
			} : new IPAddress[2]
			{
				IPAddress.Loopback,
				IPAddress.IPv6Loopback
			});
			Exception ipv4Error = null;
			foreach (IPAddress address in addresses)
			{
				try
				{
					TcpListener l = new TcpListener(address, port);
					if (address.AddressFamily == AddressFamily.InterNetworkV6)
					{
						// Solo IPv6 en este socket: el de IPv4 va aparte.
						l.Server.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, true);
					}
					Sockets.NoInherit(l);
					l.Start();
					listeners.Add(l);
					Thread worker = new Thread((ThreadStart)delegate
					{
						AcceptLoop(l);
					})
					{
						IsBackground = true,
						Name = "rtmp-accept"
					};
					worker.Start();
				}
				catch (Exception ex2)
				{
					if (address.AddressFamily == AddressFamily.InterNetwork)
					{
						ipv4Error = ex2;
					}
				}
			}
			if (ipv4Error != null)
			{
				Stop();
				throw new IOException("El puerto " + port + " está ocupado o bloqueado (" + ipv4Error.Message + ")");
			}
		}

		public void Stop()
		{
			running = false;
			foreach (TcpListener listener in listeners)
			{
				try
				{
					listener.Stop();
				}
				catch
				{
				}
			}
			listeners.Clear();
			RtmpSession[] open;
			lock (sessions)
			{
				open = sessions.ToArray();
				sessions.Clear();
			}
			foreach (RtmpSession session in open)
			{
				session.Close();
			}
		}

		// Hilo "rtmp-accept": acepta conexiones y lanza un hilo "rtmp-session" para cada una.
		private void AcceptLoop(TcpListener l)
		{
			while (running)
			{
				TcpClient client;
				try
				{
					client = l.AcceptTcpClient();
				}
				catch
				{
					if (running)
					{
						Thread.Sleep(200);
					}
					continue;
				}
				Sockets.NoInherit(client);
				client.NoDelay = true;
				RtmpSession s = new RtmpSession(this, client);
				lock (sessions)
				{
					sessions.Add(s);
				}
				Thread worker = new Thread((ThreadStart)delegate
				{
					s.Run();
					lock (sessions)
					{
						sessions.Remove(s);
					}
				})
				{
					IsBackground = true,
					Name = "rtmp-session"
				};
				worker.Start();
			}
		}
	}
}
