// Delay largo sin gastar memoria: lo que esperaba en la cola por encima de Engine.MemoryBudget se guarda
// en un archivo temporal y vuelve a la memoria justo antes de enviarse. Como lo que entra en la cola sale
// en el mismo orden, el archivo es un anillo: se escribe detrás de lo último y se libera desde lo más
// antiguo, así que no crece más que lo que hay esperando. El archivo se borra solo al cerrar (también
// si el programa se cierra de golpe). Si algo falla con el disco, se deja de usar y todo sigue en memoria.
using System;
using System.Collections.Generic;
using System.IO;

namespace StreamDelay
{
	internal class Spool
	{
		// Tamaño máximo del archivo (si se llena, lo nuevo se queda en memoria).
		public long Capacity = 3L * 1024 * 1024 * 1024;

		private readonly object lk = new object();

		private FileStream fs;

		private bool broken;

		// Trozos guardados, del más antiguo al más nuevo: { posición, tamaño }.
		private readonly Queue<long[]> entries = new Queue<long[]>();

		private long writePos;

		public Action<string> Log = delegate { };

		// Bytes que hay ahora mismo en el archivo esperando.
		public long Bytes { get; private set; }

		private bool Open()
		{
			if (fs != null)
			{
				return true;
			}
			if (broken)
			{
				return false;
			}
			try
			{
				string path = Path.Combine(Path.GetTempPath(), "VortexsLive-delay-" + System.Diagnostics.Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".tmp");
				fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16, FileOptions.DeleteOnClose);
				return true;
			}
			catch (Exception ex)
			{
				broken = true;
				Log("Delay largo: no se pudo usar el disco (" + ex.Message + "); se queda todo en memoria.");
				return false;
			}
		}

		private long ReadPos
		{
			get { return entries.Count > 0 ? entries.Peek()[0] : writePos; }
		}

		// Guarda los datos del paquete en el archivo y los quita de la memoria. false = se quedan en memoria.
		public bool TrySpill(FlvPacket p)
		{
			lock (lk)
			{
				if (p.Data == null || p.SpoolPos >= 0 || !Open())
				{
					return false;
				}
				int len = p.Data.Length;
				if (entries.Count == 0)
				{
					writePos = 0;
				}
				long pos = -1;
				long rp = ReadPos;
				if (entries.Count == 0 || writePos > rp || (writePos == rp && entries.Count == 0))
				{
					if (writePos + len <= Capacity)
					{
						pos = writePos;
					}
					else if (len < rp)
					{
						pos = 0;   // vuelta al principio del anillo
					}
				}
				else if (writePos + len < rp)
				{
					pos = writePos;
				}
				if (pos < 0)
				{
					return false;   // lleno
				}
				try
				{
					fs.Position = pos;
					fs.Write(p.Data, 0, len);
				}
				catch (Exception ex)
				{
					broken = true;
					Log("Delay largo: error al escribir en el disco (" + ex.Message + "); lo nuevo se queda en memoria.");
					return false;
				}
				entries.Enqueue(new long[2] { pos, len });
				writePos = pos + len;
				Bytes += len;
				p.SpoolPos = pos;
				p.SpoolLen = len;
				p.Data = null;
				return true;
			}
		}

		// Saca de la cola del archivo el trozo de este paquete (siempre es el más antiguo).
		private bool Take(FlvPacket p)
		{
			if (entries.Count == 0)
			{
				return false;
			}
			long[] head = entries.Peek();
			if (head[0] != p.SpoolPos || head[1] != p.SpoolLen)
			{
				// No debería pasar (la cola sale en orden): se busca y se quita igualmente.
				List<long[]> rest = new List<long[]>(entries);
				int i = rest.FindIndex(e => e[0] == p.SpoolPos && e[1] == p.SpoolLen);
				if (i < 0)
				{
					return false;
				}
				rest.RemoveAt(i);
				entries.Clear();
				foreach (long[] e in rest)
				{
					entries.Enqueue(e);
				}
			}
			else
			{
				entries.Dequeue();
			}
			Bytes -= p.SpoolLen;
			return true;
		}

		// Vuelve a poner en memoria los datos de un paquete que va a salir.
		public void Load(FlvPacket p)
		{
			lock (lk)
			{
				if (p.SpoolPos < 0)
				{
					return;
				}
				byte[] data = new byte[p.SpoolLen];
				try
				{
					fs.Position = p.SpoolPos;
					int read = 0;
					while (read < data.Length)
					{
						int n = fs.Read(data, read, data.Length - read);
						if (n <= 0)
						{
							throw new EndOfStreamException();
						}
						read += n;
					}
				}
				catch (Exception ex)
				{
					Log("Delay largo: no se pudo leer del disco (" + ex.Message + ").");
					data = new byte[0];
				}
				Take(p);
				p.Data = data;
				p.SpoolPos = -1;
			}
		}

		// Un paquete del archivo que se tira sin enviarse (al bajar el delay).
		public void Release(FlvPacket p)
		{
			lock (lk)
			{
				if (p.SpoolPos < 0)
				{
					return;
				}
				Take(p);
				p.SpoolPos = -1;
			}
		}

		// Se vacía la cola (parar, cortar ya): el archivo se reutiliza desde el principio.
		public void Reset()
		{
			lock (lk)
			{
				entries.Clear();
				writePos = 0;
				Bytes = 0;
				try
				{
					if (fs != null && fs.Length > 64L * 1024 * 1024)
					{
						fs.SetLength(0);   // devolver el espacio al disco
					}
				}
				catch
				{
				}
			}
		}
	}
}
