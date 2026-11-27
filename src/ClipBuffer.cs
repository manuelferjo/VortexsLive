// Lo último que se ha emitido (lo que ya ha visto el público), para guardar clips sin volver a
// codificar: el motor le pasa cada paquete que sale (Engine.Fanout) y Save escribe un archivo FLV
// con la calidad principal y el audio principal, empezando en un fotograma clave.
using System;
using System.Collections.Generic;
using System.IO;

namespace StreamDelay
{
	internal class ClipBuffer
	{
		private struct Entry
		{
			public FlvPacket P;

			// Marca de tiempo con la que salió.
			public long OutTs;
		}

		// Se guarda un poco más de lo que dura un clip para poder empezar en un fotograma clave.
		public const long KeepMs = 65000L;

		private readonly object lk = new object();

		private readonly LinkedList<Entry> items = new LinkedList<Entry>();

		// Cabeceras de audio, vídeo y metadatos (las necesita el archivo al principio).
		private readonly Dictionary<int, FlvPacket> headers = new Dictionary<int, FlvPacket>();

		private RtmpSession source;

		private int track = -1;

		// Desde el hilo de salida del motor, con cada paquete que se envía a los destinos. Solo se guarda
		// la calidad principal (mainTrack) y el audio principal, que es lo que lleva el clip: con la
		// Transmisión mejorada (varias calidades) así se gasta una fracción de la memoria.
		public void Add(FlvPacket p, long outTs, int mainTrack)
		{
			lock (lk)
			{
				if (mainTrack != track)
				{
					// Ha cambiado la calidad principal: lo guardado era de otra.
					track = mainTrack;
					items.Clear();
				}
				if (p.Src != source)
				{
					// Otra conexión de OBS: sus cabeceras y su vídeo no encajan con lo anterior.
					headers.Clear();
					items.Clear();
					source = p.Src;
				}
				else if (items.Count > 0 && outTs + 1000 < items.Last.Value.OutTs)
				{
					// Empezó otra emisión (la línea de tiempo volvió a 0).
					items.Clear();
				}
				if (p.Seq)
				{
					if (p.SeqStart)
					{
						headers[(p.Type << 16) | ((p.SubType & 0xFF) << 8) | p.Track] = p;
					}
					return;
				}
				if ((p.Type == 9 && p.Track != mainTrack) || (p.Type == 8 && p.Track != 0))
				{
					return;
				}
				items.AddLast(new Entry
				{
					P = p,
					OutTs = outTs
				});
				while (items.First.Value.OutTs < outTs - KeepMs)
				{
					items.RemoveFirst();
				}
			}
		}

		// Escribe en path (FLV) los últimos `seconds` segundos de la pista de vídeo mainTrack y del
		// audio principal. Empieza en el último fotograma clave antes de ese punto (así dura al menos
		// eso, si hay). Devuelve los segundos guardados. Lanza una excepción si no hay nada que guardar.
		public double Save(string path, int seconds, int mainTrack)
		{
			List<Entry> list;
			List<KeyValuePair<int, FlvPacket>> heads;
			lock (lk)
			{
				list = new List<Entry>(items);
				heads = new List<KeyValuePair<int, FlvPacket>>(headers);
			}
			if (list.Count == 0)
			{
				throw new InvalidOperationException("aún no se ha emitido nada");
			}
			long end = list[list.Count - 1].OutTs;
			long from = end - (long)seconds * 1000L;
			int start = -1;
			for (int i = 0; i < list.Count; i++)
			{
				Entry e = list[i];
				if (e.P.Type != 9 || e.P.Track != mainTrack || !e.P.Key)
				{
					continue;
				}
				if (e.OutTs <= from || start < 0)
				{
					start = i;
				}
				if (e.OutTs > from)
				{
					break;
				}
			}
			if (start < 0)
			{
				throw new InvalidOperationException("todavía no ha salido ningún fotograma clave");
			}
			long baseTs = list[start].OutTs;
			// Las cabeceras en orden: metadatos, vídeo y audio (por su clave: tipo << 16…).
			heads.Sort(delegate(KeyValuePair<int, FlvPacket> a, KeyValuePair<int, FlvPacket> b)
			{
				int ra = Rank(a.Value.Type);
				int rb = Rank(b.Value.Type);
				return (ra != rb) ? ra.CompareTo(rb) : a.Key.CompareTo(b.Key);
			});
			using (FileStream fs = File.Create(path))
			{
				// Cabecera FLV: "FLV", versión 1, audio y vídeo, 9 bytes, y tamaño de etiqueta anterior 0.
				fs.Write(new byte[13] { 70, 76, 86, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0 }, 0, 13);
				foreach (KeyValuePair<int, FlvPacket> h in heads)
				{
					FlvPacket p = h.Value;
					if (p.Type == 18)
					{
						WriteTag(fs, 18, 0L, p.Data);
					}
					else if (p.Type == 9 && p.Track == mainTrack)
					{
						WriteTag(fs, 9, 0L, VideoData(p, mainTrack));
					}
					else if (p.Type == 8 && p.Track == 0)
					{
						WriteTag(fs, 8, 0L, p.Data);
					}
				}
				for (int i = start; i < list.Count; i++)
				{
					FlvPacket p = list[i].P;
					long ts = list[i].OutTs - baseTs;
					if (p.Type == 9 && p.Track == mainTrack)
					{
						WriteTag(fs, 9, ts, VideoData(p, mainTrack));
					}
					else if (p.Type == 8 && p.Track == 0 && list[i].OutTs >= baseTs)
					{
						WriteTag(fs, 8, ts, p.Data);
					}
				}
			}
			return (double)(end - baseTs) / 1000.0;
		}

		private static int Rank(byte type)
		{
			return (type == 18) ? 0 : ((type == 9) ? 1 : 2);
		}

		// Si la calidad principal va en una pista extra (multipista), se pasa al formato normal.
		private static byte[] VideoData(FlvPacket p, int mainTrack)
		{
			if (p.Track == 0)
			{
				return p.Data;
			}
			return VideoInfo.ToSingleTrack(p.Data);
		}

		// Etiqueta FLV: tipo, tamaño (3 bytes), marca de tiempo (3 + 1), stream 0, datos y tamaño total.
		private static void WriteTag(Stream s, byte type, long ts, byte[] data)
		{
			if (data == null)
			{
				return;
			}
			int length = data.Length;
			byte[] hdr = new byte[11]
			{
				type,
				(byte)(length >> 16),
				(byte)(length >> 8),
				(byte)length,
				(byte)(ts >> 16),
				(byte)(ts >> 8),
				(byte)ts,
				(byte)(ts >> 24),
				0,
				0,
				0
			};
			s.Write(hdr, 0, 11);
			s.Write(data, 0, length);
			int tagSize = length + 11;
			s.Write(new byte[4]
			{
				(byte)(tagSize >> 24),
				(byte)(tagSize >> 16),
				(byte)(tagSize >> 8),
				(byte)tagSize
			}, 0, 4);
		}
	}
}
