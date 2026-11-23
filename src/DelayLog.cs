// Registro del delay real, para Vortexs Clips.
//
// Vortexs Clips recorta del VOD de Twitch las rondas de VALORANT; para saber en qué segundo del VOD
// cae cada jugada necesita el delay que tenía el directo en cada momento (también cuando se cambia
// en pleno directo). Aquí se apunta en %APPDATA%\VortexsLive\delay_log.csv una línea
// "milisegundos_desde_1970;delay_en_segundos" cada vez que el delay real cambia más de 0,2 s
// (y una vez por minuto aunque no cambie). Es muy ligero: se mira como mucho 4 veces por segundo
// y la escritura se hace en segundo plano, así que nunca frena el directo.
using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace StreamDelay
{
	internal static class DelayLog
	{
		private const int CheckEveryMs = 250;

		private const int MinChangeMs = 200;

		private const int HeartbeatMs = 60000;

		private const long MaxBytes = 20L * 1024L * 1024L;

		private static int lastCheck = int.MinValue;

		private static int lastWrite = int.MinValue;

		private static int lastDelay = int.MinValue;

		private static readonly object fileLock = new object();

		public static string FilePath
		{
			get
			{
				return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VortexsLive"), "delay_log.csv");
			}
		}

		// Se llama desde el hilo de salida con cada paquete emitido; casi siempre sale al momento.
		public static void Record(int delayMs)
		{
			int now = Environment.TickCount;
			if (lastCheck != int.MinValue && unchecked(now - lastCheck) < CheckEveryMs)
			{
				return;
			}
			lastCheck = now;
			bool changed = lastDelay == int.MinValue || Math.Abs(delayMs - lastDelay) >= MinChangeMs;
			bool heartbeat = lastWrite == int.MinValue || unchecked(now - lastWrite) >= HeartbeatMs;
			if (!changed && !heartbeat)
			{
				return;
			}
			lastDelay = delayMs;
			lastWrite = now;
			long epochMs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
			string line = epochMs.ToString(CultureInfo.InvariantCulture) + ";" + (delayMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + "\r\n";
			ThreadPool.QueueUserWorkItem(delegate
			{
				Append(line);
			});
		}

		private static void Append(string line)
		{
			try
			{
				lock (fileLock)
				{
					string path = FilePath;
					Directory.CreateDirectory(Path.GetDirectoryName(path));
					FileInfo info = new FileInfo(path);
					if (info.Exists && info.Length > MaxBytes)
					{
						string old = Path.Combine(Path.GetDirectoryName(path), "delay_log.old.csv");
						if (File.Exists(old))
						{
							File.Delete(old);
						}
						File.Move(path, old);
					}
					File.AppendAllText(path, line);
				}
			}
			catch
			{
				// El registro nunca debe molestar al directo.
			}
		}
	}
}
