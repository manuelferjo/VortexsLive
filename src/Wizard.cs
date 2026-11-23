// Asistente de primera vez: 4 pasos dentro de la ventana (OBS, destinos, delay y comprobar).
// Sale solo la primera vez que se abre Vortexs Live en un PC sin configuración (wizard=1 en el
// .ini cuando se termina o se salta). Se puede volver a abrir desde la lista de atajos (?).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private const int WizSteps = 4;

		// true cuando ya se ha hecho o saltado (se guarda en el .ini).
		private bool wizardDone;

		// No había configuración al abrir: primera vez en este PC.
		private bool firstRun;

		private PaintBox wiz;

		private int wizStep;

		private readonly List<Control>[] wizCtl = new List<Control>[WizSteps]
		{
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>()
		};

		private VButton wizNext;

		private VButton wizBack;

		private VButton wizStart;

		private VButton[] wizDelayBtns;

		private Toggle[] wizDestTg;

		private VButton[] wizDestKey;

		private static readonly int[] WizDelays = new int[6] { 15, 30, 60, 120, 180, 300 };

		private static readonly string[] WizTitles = new string[WizSteps] { "Conecta OBS", "¿Dónde emites?", "Elige el delay", "Comprueba que funciona" };

		private void WizPlace(int step, Control c, float x, float y, float w, float h)
		{
			Place(wiz, c, x, y, w, h);
			if (step >= 0)
			{
				wizCtl[step].Add(c);
			}
		}

		private void BuildWizard()
		{
			wiz = new PaintBox
			{
				Painter = PaintWizard,
				Visible = false
			};
			wiz.SetBounds(0, S(72f), S(460f), S(WinH - 72f));
			Controls.Add(wiz);

			// Paso 1: OBS.
			Field obs = new Field();
			obs.Box.Text = ObsServer;
			obs.ReadOnly = true;
			WizPlace(0, obs, 20f, 214f, 292f, 36f);
			VButton copy = new VButton { Kind = ButtonKind.Ghost, Text = "Copiar", FontSize = 9f, Icon = "copy" };
			WizPlace(0, copy, 320f, 214f, 120f, 36f);
			copy.Click += delegate
			{
				obs.Box.Text = ObsServer;
				CopyText(ObsServer, copy, "Copiar");
			};

			// Paso 2: destinos (interruptor y botón de clave de cada uno).
			InitDestinations();
			wizDestTg = new Toggle[dests.Length];
			wizDestKey = new VButton[dests.Length];
			for (int i = 0; i < dests.Length; i++)
			{
				int idx = i;
				wizDestTg[i] = new Toggle { Text = dests[i].Name, Hint = "" };
				WizPlace(1, wizDestTg[i], 20f, 130f + i * 50f, 290f, 44f);
				wizDestTg[i].CheckedChanged += delegate
				{
					if (loading)
					{
						return;
					}
					dests[idx].Enabled = wizDestTg[idx].Checked;
					RefreshDestinationsUi();
					SaveConfig();
					WizRefresh();
				};
				wizDestKey[i] = new VButton { Kind = ButtonKind.Ghost, Text = "Clave…", FontSize = 9f };
				WizPlace(1, wizDestKey[i], 320f, 134f + i * 50f, 120f, 36f);
				wizDestKey[i].Click += delegate
				{
					EditDestination(dests[idx]);
					WizRefresh();
				};
			}

			// Paso 3: delay.
			wizDelayBtns = new VButton[WizDelays.Length];
			for (int i = 0; i < WizDelays.Length; i++)
			{
				int v = WizDelays[i];
				wizDelayBtns[i] = new VButton { Kind = ButtonKind.Pill, Text = (v < 60) ? (v + " s") : (v / 60 + " min"), FontSize = 10f };
				WizPlace(2, wizDelayBtns[i], 20f + (float)(i % 3) * 142f, 190f + (float)(i / 3) * 52f, 136f, 44f);
				wizDelayBtns[i].Click += delegate
				{
					SetEnabled(true);
					ApplyDelay(v);
					WizRefresh();
				};
			}

			// Paso 4: iniciar el servicio y ver que OBS llega.
			wizStart = new VButton { Kind = ButtonKind.Primary, Text = "Iniciar servicio", FontSize = 10.5f, Icon = "live" };
			WizPlace(3, wizStart, 20f, 176f, 420f, 46f);
			wizStart.Click += delegate
			{
				if (!engine.Running)
				{
					ToggleService();
				}
				WizRefresh();
			};

			// Abajo: Saltar · Atrás · Siguiente.
			VButton skip = new VButton { Kind = ButtonKind.Link, Text = "Saltar el asistente", FontSize = 8.5f };
			WizPlace(-1, skip, 20f, WinH - 72f - 62f, 150f, 40f);
			skip.Click += delegate
			{
				CloseWizard();
			};
			wizBack = new VButton { Kind = ButtonKind.Ghost, Text = "Atrás", FontSize = 10f };
			WizPlace(-1, wizBack, 186f, WinH - 72f - 64f, 100f, 44f);
			wizBack.Click += delegate
			{
				WizGo(wizStep - 1);
			};
			wizNext = new VButton { Kind = ButtonKind.Primary, Text = "Siguiente", FontSize = 10f };
			WizPlace(-1, wizNext, 294f, WinH - 72f - 64f, 146f, 44f);
			wizNext.Click += delegate
			{
				if (wizStep < WizSteps - 1)
				{
					WizGo(wizStep + 1);
				}
				else
				{
					CloseWizard();
				}
			};
		}

		// Abre el asistente (desde el paso 1).
		private void OpenWizard()
		{
			if (wiz == null)
			{
				return;
			}
			if (keysSheet != null && keysSheet.Visible)
			{
				keysSheet.Visible = false;
			}
			wiz.Visible = true;
			wiz.BringToFront();
			WizGo(0);
		}

		private void CloseWizard()
		{
			wiz.Visible = false;
			if (!wizardDone)
			{
				wizardDone = true;
				SaveConfig();
			}
			GoPage(PageLive);
		}

		private void WizGo(int step)
		{
			wizStep = Math.Max(0, Math.Min(WizSteps - 1, step));
			for (int i = 0; i < WizSteps; i++)
			{
				foreach (Control c in wizCtl[i])
				{
					c.Visible = i == wizStep;
				}
			}
			wizBack.Visible = wizStep > 0;
			wizNext.Text = (wizStep == WizSteps - 1) ? "Terminar" : "Siguiente";
			WizRefresh();
		}

		// Estado de los controles del paso (lo llama también el temporizador cada medio segundo).
		private void WizRefresh()
		{
			if (wiz == null || !wiz.Visible)
			{
				return;
			}
			if (wizStep == 1)
			{
				bool wasLoading = loading;
				loading = true;
				for (int i = 0; i < dests.Length; i++)
				{
					wizDestTg[i].Checked = dests[i].Enabled;
					wizDestTg[i].Hint = DestHint(dests[i]);
					wizDestTg[i].Enabled = !engine.Running;
					wizDestKey[i].Enabled = !engine.Running;
					wizDestTg[i].Invalidate();
				}
				loading = wasLoading;
			}
			if (wizStep == 2)
			{
				int now = engine.Enabled ? engine.TargetMs / 1000 : -1;
				for (int i = 0; i < wizDelayBtns.Length; i++)
				{
					wizDelayBtns[i].Selected = WizDelays[i] == now;
					wizDelayBtns[i].Invalidate();
				}
			}
			if (wizStep == 3)
			{
				wizStart.Enabled = !engine.Running;
				wizStart.Text = engine.Running ? "Servicio en marcha" : "Iniciar servicio";
				wizStart.Invalidate();
			}
			wiz.Invalidate();
		}

		private void PaintWizard(Graphics g, Rectangle r)
		{
			Theme.Quality(g);
			using (SolidBrush bg = new SolidBrush(Theme.Bg))
			{
				g.FillRectangle(bg, r);
			}
			float x = S(20f);
			float w = r.Width - S(40f);
			// Progreso: 4 rayas.
			float seg = (w - S(8f) * (WizSteps - 1)) / WizSteps;
			for (int i = 0; i < WizSteps; i++)
			{
				RectangleF bar = new RectangleF(x + i * (seg + S(8f)), S(10f), seg, S(4f));
				using (GraphicsPath p = Theme.Round(bar, S(2f)))
				{
					if (i <= wizStep)
					{
						using (LinearGradientBrush b = Theme.Brand(bar, true))
						{
							g.FillPath(b, p);
						}
					}
					else
					{
						using (SolidBrush b2 = new SolidBrush(Theme.Border))
						{
							g.FillPath(b2, p);
						}
					}
				}
			}
			string label = (wizStep == 0 && !wizardDone) ? "BIENVENIDO A VORTEXS LIVE" : "ASISTENTE DE INICIO";
			Theme.DrawText(g, I18n.Tr(label) + " · " + I18n.Tr("PASO") + " " + (wizStep + 1) + "/" + WizSteps, Theme.Mono(8f), Theme.Muted, new RectangleF(x, S(24f), w, S(20f)), StringAlignment.Near, StringAlignment.Center);
			Theme.DrawText(g, WizTitles[wizStep], Theme.Display(20f), Theme.Text, new RectangleF(x, S(46f), w, S(36f)), StringAlignment.Near, StringAlignment.Center);
			Font body = Theme.Body(9.5f);
			switch (wizStep)
			{
			case 0:
				Theme.DrawWrapped(g, "Vortexs Live se pone entre OBS y tus plataformas. OBS le envía la emisión y él la manda a todas, con el delay que elijas.", body, Theme.Muted, new RectangleF(x, S(88f), w, S(44f)));
				WizLine(g, x, S(136f), w, "1", "En OBS: Ajustes → Emisión. Servicio: Twitch.");
				WizLine(g, x, S(162f), w, "2", "Servidor: «Especifica un servidor personalizado…» y pega esto:");
				WizLine(g, x, S(262f), w, "3", "Clave: tu clave de Twitch (o «Conectar cuenta»). Para enviar varias calidades, activa «Transmisión mejorada».");
				Theme.DrawWrapped(g, "Si no emites en Twitch, usa el servicio «Personalizado» con este mismo servidor y la clave que quieras.", Theme.Body(8.5f), Theme.Dim, new RectangleF(x, S(318f), w, S(40f)));
				break;
			case 1:
				Theme.DrawWrapped(g, "Activa las plataformas en las que emites y pon su clave. En Twitch puedes dejarla vacía: se usa la de OBS.", body, Theme.Muted, new RectangleF(x, S(88f), w, S(40f)));
				break;
			case 2:
				Theme.DrawWrapped(g, "Lo que tarda tu directo en llegar al público. Lo puedes cambiar cuando quieras, también en pleno directo, sin cortes.", body, Theme.Muted, new RectangleF(x, S(88f), w, S(44f)));
				Theme.DrawWrapped(g, "Atajos en cualquier momento (también desde el juego): Ctrl+Alt+↑ / ↓ sube o baja 5 s y Ctrl+Alt+D lo activa o lo quita. El máximo son 5 minutos.", Theme.Body(8.5f), Theme.Dim, new RectangleF(x, S(304f), w, S(48f)));
				break;
			case 3:
			{
				Theme.DrawWrapped(g, "Inicia el servicio y dale a «Iniciar transmisión» en OBS. Tu directo sale a las plataformas cuando pasa el delay.", body, Theme.Muted, new RectangleF(x, S(88f), w, S(44f)));
				float y = S(240f);
				bool running = engine.Running;
				WizCheck(g, x, y, w, running, running ? "Servicio en marcha" : "Servicio parado", running ? (I18n.Tr("Esperando a OBS en ") + ObsServer) : "Pulsa «Iniciar servicio»");
				y += S(52f);
				bool obsOk = running && engine.InputActive;
				WizCheck(g, x, y, w, obsOk, obsOk ? "OBS conectado" : "OBS sin conectar", obsOk ? (engine.Qualities.Length > 0 ? engine.Qualities : "Recibiendo la emisión") : "En OBS: «Iniciar transmisión»");
				y += S(52f);
				List<string> missing = new List<string>();
				List<string> ready = new List<string>();
				foreach (Destination d in EnabledDestinations())
				{
					if (d.Server.Trim().Length == 0 || (d.Key.Trim().Length == 0 && !d.IsTwitch))
					{
						missing.Add(d.Name);
					}
					else
					{
						ready.Add(d.Name);
					}
				}
				bool destOk = ready.Count > 0 && missing.Count == 0;
				string destDetail = (missing.Count > 0) ? (I18n.Tr("Falta la clave de ") + string.Join(", ", missing.ToArray())) : ((ready.Count == 0) ? I18n.Tr("Ninguno activado (paso 2)") : string.Join(" + ", ready.ToArray()));
				WizCheck(g, x, y, w, destOk, "Destinos", destDetail);
				break;
			}
			}
		}

		// Línea numerada del paso 1.
		private void WizLine(Graphics g, float x, float y, float w, string n, string text)
		{
			RectangleF dot = new RectangleF(x, y + S(2f), S(20f), S(20f));
			using (SolidBrush b = new SolidBrush(Color.FromArgb(40, Theme.Violet)))
			{
				g.FillEllipse(b, dot);
			}
			Theme.DrawText(g, n, Theme.BodyBold(8.5f), Theme.VioletLight, dot, StringAlignment.Center, StringAlignment.Center);
			Theme.DrawWrapped(g, text, Theme.Body(9f), Theme.Text, new RectangleF(x + S(30f), y, w - S(30f), S(48f)));
		}

		// Fila de comprobación: ✓ verde o círculo amarillo, título y detalle.
		private void WizCheck(Graphics g, float x, float y, float w, bool ok, string title, string detail)
		{
			RectangleF row = new RectangleF(x, y, w, S(44f));
			Color c = ok ? Theme.Green : Theme.Yellow;
			using (GraphicsPath p = Theme.Round(row, S(12f)))
			{
				using (SolidBrush b = new SolidBrush(Color.FromArgb(18, c)))
				{
					g.FillPath(b, p);
				}
				using (Pen pen = new Pen(Color.FromArgb(90, c)))
				{
					g.DrawPath(pen, p);
				}
			}
			Theme.DrawText(g, ok ? "✓" : "…", Theme.BodyBold(12f), c, new RectangleF(row.X + S(8f), row.Y, S(28f), row.Height), StringAlignment.Center, StringAlignment.Center);
			Theme.DrawText(g, title, Theme.BodyBold(9.5f), Theme.Text, new RectangleF(row.X + S(42f), row.Y, S(150f), row.Height), StringAlignment.Near, StringAlignment.Center);
			Theme.DrawText(g, detail, Theme.Body(8.5f), Theme.Muted, new RectangleF(row.X + S(190f), row.Y, row.Width - S(202f), row.Height), StringAlignment.Far, StringAlignment.Center);
		}
	}
}
