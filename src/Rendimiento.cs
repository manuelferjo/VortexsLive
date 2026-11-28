// Rendimiento: una ventana (Ajustes → Registro → Rendimiento) con lo que gasta Vortexs Live (CPU,
// memoria, limpiezas de memoria de .NET), el delay (cuánto hay en memoria y en el disco), lo que llega
// de OBS y la cola de cada destino, con una frase de qué pasa y «Copiar» para mandarlo si pides ayuda.
// Además, mientras estás en directo, el programa va con prioridad «por encima de lo normal» en Windows
// (un juego exigente no le quita tiempo al envío) y .NET evita las pausas largas al limpiar memoria;
// al terminar se vuelve a lo de antes y se ordena la memoria una vez.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private Form perfForm;

		private bool perfTuned;

		private ProcessPriorityClass perfPrevPriority = ProcessPriorityClass.Normal;

		private bool perfRaised;

		// Muestras para calcular la CPU y la entrada de OBS entre dos repintados.
		private TimeSpan perfLastCpu;

		private DateTime perfLastAt = DateTime.MinValue;

		private long perfLastIn = -1;

		private double perfCpu;

		private int perfInKbps;

		private int[] perfGcStart = new int[3];

		private DateTime perfOpenedAt;

		// Cada medio segundo (temporizador principal): prioridad y modo de memoria según si estás en directo.
		private void PerfTick()
		{
			bool live = false;
			try
			{
				live = engine.OutputActive || (engineV != null && engineV.Running && engineV.OutputActive);
			}
			catch
			{
			}
			if (live == perfTuned)
			{
				return;
			}
			perfTuned = live;
			LiveTuning(live);
		}

		private void LiveTuning(bool live)
		{
			try
			{
				using (Process p = Process.GetCurrentProcess())
				{
					if (live)
					{
						perfPrevPriority = p.PriorityClass;
						// Solo se sube si estaba en lo normal (si la has puesto tú más alta, se respeta).
						if (perfPrevPriority == ProcessPriorityClass.Normal)
						{
							p.PriorityClass = ProcessPriorityClass.AboveNormal;
							perfRaised = true;
							AppendLog("En directo: Vortexs Live va con prioridad alta en Windows para que el envío no se resienta.");
						}
					}
					else if (perfRaised)
					{
						p.PriorityClass = perfPrevPriority;
						perfRaised = false;
					}
				}
			}
			catch
			{
			}
			try
			{
				SetGcLatency(live);
			}
			catch
			{
			}
			if (!live)
			{
				// Terminado el directo: se ordena la memoria de los trozos de vídeo grandes una vez, sin prisa.
				ThreadPool.QueueUserWorkItem(delegate
				{
					try
					{
						Thread.Sleep(3000);
						CompactMemory();
					}
					catch
					{
					}
				});
			}
		}

		// En métodos aparte: si el .NET del PC fuera muy antiguo y no los tuviera, falla solo la llamada
		// (dentro de su try) y no el método que la usa.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void SetGcLatency(bool live)
		{
			GCSettings.LatencyMode = live ? GCLatencyMode.SustainedLowLatency : GCLatencyMode.Interactive;
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void CompactMemory()
		{
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect();
		}

		private void PerfDialog()
		{
			if (perfForm != null && !perfForm.IsDisposed)
			{
				perfForm.Activate();
				return;
			}
			int rows = Math.Max(1, AllSinks().Length);
			int boxH = 318 + rows * 22;
			Form f = DarkForm("Rendimiento", 460, boxH + 96);
			perfForm = f;
			perfLastAt = DateTime.MinValue;
			perfLastIn = -1;
			perfOpenedAt = DateTime.UtcNow;
			for (int i = 0; i < 3; i++)
			{
				perfGcStart[i] = GC.CollectionCount(i);
			}
			PerfSample();
			PaintBox box = Box(f, 20f, 16f, 420f, boxH, PaintPerf);
			VButton copy = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar",
				FontSize = 9.5f,
				Icon = "copy"
			};
			Place(f, copy, 20f, boxH + 32, 205f, 40f);
			copy.Click += delegate
			{
				CopyText(PerfReport(), copy, "Copiar");
			};
			VButton close = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Cerrar",
				FontSize = 9.5f
			};
			Place(f, close, 235f, boxH + 32, 205f, 40f);
			close.Click += delegate
			{
				f.Close();
			};
			System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
			{
				Interval = 1000
			};
			t.Tick += delegate
			{
				PerfSample();
				box.Invalidate();
			};
			t.Start();
			f.FormClosed += delegate
			{
				t.Stop();
				t.Dispose();
				perfForm = null;
			};
			f.Show(this);
		}

		// CPU del programa (en % del total del PC) y lo que llega de OBS, desde la muestra anterior.
		private void PerfSample()
		{
			DateTime now = DateTime.UtcNow;
			try
			{
				using (Process p = Process.GetCurrentProcess())
				{
					TimeSpan cpu = p.TotalProcessorTime;
					if (perfLastAt != DateTime.MinValue)
					{
						double wall = (now - perfLastAt).TotalMilliseconds;
						if (wall > 0.0)
						{
							perfCpu = (cpu - perfLastCpu).TotalMilliseconds / wall / Environment.ProcessorCount * 100.0;
						}
					}
					perfLastCpu = cpu;
				}
			}
			catch
			{
			}
			long inBytes = engine.InputBytes;
			if (perfLastIn >= 0 && perfLastAt != DateTime.MinValue)
			{
				double secs = (now - perfLastAt).TotalSeconds;
				if (secs > 0.0)
				{
					perfInKbps = (int)Math.Round((inBytes - perfLastIn) * 8.0 / 1000.0 / secs);
				}
			}
			perfLastIn = inBytes;
			perfLastAt = now;
		}

		private static string MbText(long bytes)
		{
			return ((double)bytes / 1048576.0).ToString((bytes < 10485760) ? "0.0" : "0", CultureInfo.CurrentCulture) + " MB";
		}

		// { etiqueta, valor } de cada línea, por apartados (etiqueta null = título de apartado).
		private List<string[]> PerfLines(out string verdict, out bool warn)
		{
			List<string[]> l = new List<string[]>();
			long ws = 0L;
			int threads = 0;
			try
			{
				using (Process p = Process.GetCurrentProcess())
				{
					ws = p.WorkingSet64;
					threads = p.Threads.Count;
				}
			}
			catch
			{
			}
			double mins = Math.Max(1.0 / 60.0, (DateTime.UtcNow - perfOpenedAt).TotalMinutes);
			int gen2 = GC.CollectionCount(2) - perfGcStart[2];
			int gen0 = GC.CollectionCount(0) - perfGcStart[0];
			l.Add(new string[] { null, "PROGRAMA" });
			l.Add(new string[] { "CPU", perfCpu.ToString("0.0", CultureInfo.CurrentCulture) + " %  ·  " + Environment.ProcessorCount + " " + I18n.Tr("núcleos") });
			l.Add(new string[] { "Memoria", MbText(ws) + "  ·  .NET " + MbText(GC.GetTotalMemory(false)) });
			l.Add(new string[] { "Limpiezas de memoria", gen0 + " " + I18n.Tr("rápidas") + " · " + gen2 + " " + I18n.Tr("completas") + " (" + (gen2 / mins).ToString("0.0", CultureInfo.CurrentCulture) + "/min)" });
			l.Add(new string[] { "Hilos", threads.ToString() });
			bool raised = false;
			try
			{
				using (Process p = Process.GetCurrentProcess())
				{
					raised = p.PriorityClass != ProcessPriorityClass.Normal;
				}
			}
			catch
			{
			}
			l.Add(new string[] { "Prioridad en Windows", raised ? I18n.Tr("alta (en directo)") : I18n.Tr("normal") });
			l.Add(new string[] { null, "DELAY" });
			long queued = engine.QueuedBytes;
			long spooled = engine.SpooledBytes;
			l.Add(new string[] { "Delay real", engine.OutputActive ? SecText(engine.CurrentDelayMs) : "—" });
			l.Add(new string[] { "Esperando", engine.Running ? (MbText(Math.Max(0L, queued - spooled)) + " " + I18n.Tr("en memoria") + " · " + MbText(spooled) + " " + I18n.Tr("en el disco")) : "—" });
			l.Add(new string[] { null, "OBS" });
			l.Add(new string[] { "Entrada", engine.InputActive ? (RateText(perfInKbps) + ((engine.InputTracks > 1) ? (" · " + engine.InputTracks + " " + I18n.Tr("calidades")) : "")) : I18n.Tr("sin señal") });
			l.Add(new string[] { null, "DESTINOS" });
			Sink[] sinks = AllSinks();
			bool slow = false;
			int totalKbps = 0;
			foreach (Sink s in sinks)
			{
				bool live = s.State == SinkState.Live;
				long lag = live ? s.QueueAgeMs : 0L;
				slow |= live && lag > 1500L;
				totalKbps += s.RateKbps;
				l.Add(new string[] { s.D.Name, live ? (RateText(s.RateKbps) + " · " + I18n.Tr("retraso") + " " + SecText(lag) + ((s.Reconnects > 0) ? (" · " + s.Reconnects + " " + I18n.Tr("cortes")) : "")) : I18n.Tr("sin emitir") });
			}
			if (sinks.Length == 0)
			{
				l.Add(new string[] { "—", I18n.Tr("Inicia el servicio para verlos") });
			}
			warn = true;
			if (slow)
			{
				verdict = "Tu subida no da para todos los destinos: baja el bitrate en OBS o emite en menos sitios.";
			}
			else if (perfCpu > 30.0)
			{
				verdict = "Vortexs Live está usando mucha CPU. Si te pasa siempre, pulsa «Copiar» y mándalo.";
			}
			else if (gen2 / mins > 6.0 && mins > 1.0)
			{
				verdict = "La memoria se está limpiando muy a menudo. Si notas tirones, pulsa «Copiar» y mándalo.";
			}
			else
			{
				warn = false;
				verdict = engine.OutputActive ? "Todo en orden: el programa va sobrado." : "Todo en orden.";
			}
			return l;
		}

		private void PaintPerf(Graphics g, Rectangle r)
		{
			Theme.Quality(g);
			string verdict;
			bool warn;
			List<string[]> lines = PerfLines(out verdict, out warn);
			float y = 0f;
			Font label = Theme.Body(8.5f);
			Font value = Theme.Mono(8f);
			Font head = Theme.Mono(7f);
			foreach (string[] line in lines)
			{
				if (line[0] == null)
				{
					y += (y > 0f) ? S(8f) : 0f;
					Theme.DrawText(g, line[1], head, Theme.Muted, new RectangleF(0f, y, r.Width, S(20f)), StringAlignment.Near, StringAlignment.Center);
					y += S(22f);
					continue;
				}
				Theme.DrawText(g, line[0], label, Theme.Muted, new RectangleF(0f, y, S(150f), S(22f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, line[1], value, Theme.Text, new RectangleF(S(150f), y, r.Width - S(150f), S(22f)), StringAlignment.Far, StringAlignment.Center);
				y += S(22f);
			}
			y += S(10f);
			RectangleF box = new RectangleF(0.5f, y, r.Width - 1.5f, Math.Max(S(40f), r.Height - y - 1f));
			using (System.Drawing.Drawing2D.GraphicsPath path = Theme.Round(box, S(12f)))
			using (SolidBrush b = new SolidBrush(Color.FromArgb(28, warn ? Theme.Yellow : Theme.Green)))
			using (Pen pen = new Pen(Color.FromArgb(90, warn ? Theme.Yellow : Theme.Green)))
			{
				g.FillPath(b, path);
				g.DrawPath(pen, path);
			}
			Theme.DrawWrapped(g, verdict, Theme.Body(8.5f), warn ? Theme.Yellow : Theme.Green, new Rectangle((int)(box.X + S(14f)), (int)(box.Y + S(8f)), (int)(box.Width - S(28f)), (int)(box.Height - S(14f))));
		}

		// Texto para pegar en un mensaje si pides ayuda.
		private string PerfReport()
		{
			string verdict;
			bool warn;
			List<string[]> lines = PerfLines(out verdict, out warn);
			StringBuilder b = new StringBuilder();
			b.AppendLine("Vortexs Live " + Version + " · " + I18n.Tr("rendimiento") + " · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			b.AppendLine("Windows: " + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " (64 bits)" : " (32 bits)") + " · .NET " + Environment.Version);
			foreach (string[] line in lines)
			{
				if (line[0] == null)
				{
					b.AppendLine();
					b.AppendLine("[" + line[1] + "]");
				}
				else
				{
					b.AppendLine(I18n.Tr(line[0]) + ": " + line[1]);
				}
			}
			b.AppendLine();
			b.AppendLine(I18n.Tr(verdict));
			return b.ToString();
		}
	}
}
