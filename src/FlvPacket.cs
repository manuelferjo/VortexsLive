// Un paquete de OBS (audio, vídeo o metadatos) mientras pasa por el delay.
namespace StreamDelay
{
	internal class FlvPacket
	{
		// 8 = audio, 9 = vídeo, 18 = metadatos.
		public byte Type;

		// Marca de tiempo de entrada (ms, continua aunque OBS se reconecte).
		public long Ts;

		public byte[] Data;

		// Si está guardado en el archivo del delay largo (Spool.cs): dónde y cuánto ocupa; -1 = en memoria.
		public long SpoolPos = -1;

		public int SpoolLen;

		public int Size
		{
			get { return (Data != null) ? Data.Length : SpoolLen; }
		}

		// Cuándo llegó (reloj del motor): sale cuando han pasado los segundos de delay.
		public long Arrival;

		// Marca de tiempo con la que salió (para repetirlo al subir el delay).
		public long OutTs;

		// Fotograma clave (se puede empezar a ver desde aquí).
		public bool Key;

		// Cabecera o fin de secuencia (no es imagen ni sonido).
		public bool Seq;

		// Cabecera de inicio de secuencia (la que necesita quien empieza a ver).
		public bool SeqStart;

		// Pista (calidad) en la Transmisión mejorada; 0 = la principal.
		public int Track;

		// Tipo de paquete de Enhanced RTMP (-1 si no lo es).
		public int SubType;

		// Versión de una pista (formato normal) para los destinos que no reciben la Transmisión mejorada:
		// se calcula una vez por paquete y la usan todos (antes la sacaba cada destino). Se suelta al
		// terminar de repartirlo.
		public byte[] Single;

		public bool SingleDone;

		// Conexión de OBS de la que viene.
		public RtmpSession Src;
	}
}
