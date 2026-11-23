// Pestaña Directo: el delay (cifra grande, barra y botones rápidos), la franja de destinos,
// los recuadros de estado (OBS, calidades, overlays) y el botón Iniciar/Detener servicio.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// ---------- Construcción de la pestaña ----------

		private void BuildLive(int y0)
		{
			// Recuadro principal del delay.
			Card delayCard = new Card
			{
				Accent = true
			};
			Place(this, delayCard, 20f, y0, 420f, 256f, PageLive);
			Box(delayCard, 20f, 12f, 200f, 22f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "DELAY EN DIRECTO", Theme.Mono(7.5f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});
			tgDelay = new Toggle
			{
				Text = "Activado"
			};
			Place(delayCard, tgDelay, 268f, 9f, 136f, 28f);
			// Ventana mini (siempre encima, con lo esencial).
			VButton miniBtn = new VButton { Kind = ButtonKind.Ghost, Icon = "mini", Text = "", FontSize = 9f };
			Place(delayCard, miniBtn, 226f, 9f, 34f, 28f);
			miniBtn.Click += delegate { ShowMini(); };
			tip.SetToolTip(miniBtn, I18n.Tr("Ventana mini: siempre encima, con el delay, ±5 s y el botón de clip"));
			tgDelay.CheckedChanged += delegate
			{
				if (!loading && tgDelay.Checked != engine.Enabled)
				{
					SetEnabled(tgDelay.Checked);
				}
			};
			heroBox = Box(delayCard, 0f, 36f, 420f, 88f, PaintHero);
			// La barra solo elige los segundos: se aplican con el botón (o Intro).
			slider = new Slider
			{
				Maximum = MaxDelay
			};
			Place(delayCard, slider, 14f, 122f, 392f, 34f);
			slider.ValueChanged += delegate
			{
				UpdateApply();
				heroBox.Invalidate();
			};
			slider.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					ApplyDelay(slider.Value);
				}
			};
			// Botones de delays guardados (sus segundos y nombres los pone RefreshPresetButtons).
			int[] defaults = new int[6] { 10, 15, 30, 45, 60, 120 };
			presets = new VButton[defaults.Length];
			float presetWidth = 340f / (float)defaults.Length;
			for (int i = 0; i < defaults.Length; i++)
			{
				int v = defaults[i];
				VButton preset = new VButton
				{
					Kind = ButtonKind.Pill,
					Text = v + "s",
					FontSize = 8.5f
				};
				Place(delayCard, preset, 20f + (float)i * (presetWidth + 8f), 162f, presetWidth, 28f);
				int pi = i;
				preset.Click += delegate
				{
					ApplyPreset(pi);
				};
				presets[i] = preset;
			}
			btnApply = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Aplicar delay",
				FontSize = 10f,
				Icon = "check"
			};
			Place(delayCard, btnApply, 20f, 202f, 380f, 38f);
			btnApply.Click += delegate
			{
				ApplyDelay(slider.Value);
			};

			// Franja de destinos (se dibuja en SettingsTab.cs). Pulsarla lleva a Ajustes, o en directo abre
			// la ficha de destinos (velocidad, retraso y reconexiones de cada uno).
			int stripTop = y0 + 268;
			destStrip = Box(this, 20f, stripTop, 420f, 76f, PaintDestStrip, PageLive);
			destStrip.Cursor = Cursors.Hand;
			destStrip.Click += delegate
			{
				if (!engine.Running)
				{
					GoPage(PageSettings);
				}
				else
				{
					DestStatsDialog();
				}
			};

			// Tres recuadros de estado: OBS, calidades y overlays.
			float tileWidth = 133.33333f;
			for (int i = 0; i < 3; i++)
			{
				int idx = i;
				tiles[i] = Box(this, 20f + (float)i * (tileWidth + 10f), stripTop + 86, tileWidth, 58f, delegate(Graphics g, Rectangle r)
				{
					PaintTile(g, r, idx);
				}, PageLive);
			}
			// Tercer recuadro: lleva a la pestaña Overlays.
			tiles[2].Cursor = Cursors.Hand;
			tiles[2].Click += delegate
			{
				GoPage(PageOverlays);
			};

			// Botones Iniciar/Detener y Cortar ya.
			int buttonsTop = y0 + 422;
			btnStart = new VButton
			{
				Kind = ButtonKind.Primary,
				FontSize = 11f
			};
			Place(this, btnStart, 20f, buttonsTop, 232f, 46f, PageLive);
			btnStart.Click += delegate
			{
				ToggleService();
			};
			// Clip: guarda los últimos 60 s que ha visto el público (también con !clip y Ctrl+Alt+C).
			VButton clip = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "",
				FontSize = 10f,
				Icon = "film"
			};
			Place(this, clip, 260f, buttonsTop, 62f, 46f, PageLive);
			tip.SetToolTip(clip, I18n.Tr("Guardar un clip (últimos 60 s)") + " · Ctrl+Alt+C");
			clip.Click += delegate
			{
				if (!ClipsAllowed())
				{
					Toast("Los clips (últimos 60 s) son del plan Completo.", "info", "Ver planes", LicenseDialog);
					return;
				}
				if (SaveClip("botón Clip", null))
				{
					clip.Icon = "check";
					clip.Invalidate();
					System.Windows.Forms.Timer back = new System.Windows.Forms.Timer
					{
						Interval = 1500
					};
					back.Tick += delegate
					{
						back.Dispose();
						clip.Icon = "film";
						clip.Invalidate();
					};
					back.Start();
				}
				else
				{
					AppendLog("Se acaba de guardar un clip: espera unos segundos.");
				}
			};
			btnCut = new VButton
			{
				Kind = ButtonKind.Danger,
				Text = "Cortar ya",
				FontSize = 10f
			};
			Place(this, btnCut, 330f, buttonsTop, 110f, 46f, PageLive);
			btnCut.Click += delegate
			{
				// Dos clics seguidos (el primero pide confirmación en el mismo botón durante 3 s).
				ConfirmClick(btnCut, "¿Seguro?", delegate
				{
					engine.CutNow();
					CutVertical();
				});
			};

			// Pie: último evento del registro, o el aviso si algo falla. Pulsarlo abre el Registro.
			footer = Box(this, 20f, buttonsTop + 56, 420f, 38f, delegate(Graphics g, Rectangle r)
			{
				string prob = FirstProblem();
				if (prob != null)
				{
					// Algo falla ahora mismo: se queda en rojo hasta que se arregle.
					Theme.DrawText(g, "⚠ AVISO", Theme.Mono(6.75f), Theme.Red, new RectangleF(r.X, r.Y, r.Width, S(14f)), StringAlignment.Near, StringAlignment.Near);
					Theme.DrawText(g, prob, Theme.BodySemi(8.25f), Color.FromArgb(255, 120, 130), new RectangleF(r.X, r.Y + S(14f), r.Width, S(22f)), StringAlignment.Near, StringAlignment.Center);
					return;
				}
				Theme.DrawText(g, "ÚLTIMO EVENTO", Theme.Mono(6.75f), Theme.Dim, new RectangleF(r.X, r.Y, r.Width, S(14f)), StringAlignment.Near, StringAlignment.Near);
				Theme.DrawText(g, (lastLine.Length > 0) ? lastLine : "—", Theme.Body(8.25f), Theme.Muted, new RectangleF(r.X, r.Y + S(14f), r.Width, S(22f)), StringAlignment.Near, StringAlignment.Center);
			}, PageLive);
			footer.Cursor = Cursors.Hand;
			footer.Click += delegate
			{
				GoPage(PageLog);
			};
		}

		// ---------- Dibujo ----------

		// Cifra grande del delay, los segundos sin aplicar y la línea de estado de debajo.
		private void PaintHero(Graphics g, Rectangle r)
		{
			int seconds = engine.TargetMs / 1000;
			bool enabled = engine.Enabled;
			float left = S(18f);
			Font font = Theme.Display(40f);
			// La cifra cuenta hasta el valor nuevo en vez de saltar.
			string number = HeroValue(seconds).ToString();
			SizeF numberSize = Theme.Measure(g, number, font);
			RectangleF numberRect = new RectangleF(left, 0f, numberSize.Width, numberSize.Height);
			if (enabled)
			{
				using (LinearGradientBrush brush = Theme.Brand(numberRect, true))
				{
					g.DrawString(number, font, brush, left, 0f, StringFormat.GenericTypographic);
				}
			}
			else
			{
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(82, 82, 92)))
				{
					g.DrawString(number, font, brush2, left, 0f, StringFormat.GenericTypographic);
				}
			}
			// La "s" pequeña, alineada con la base de la cifra.
			Font unitFont = Theme.Display(17f);
			float unitX = left + numberSize.Width + (float)S(6f);
			SizeF unitSize = Theme.Measure(g, "s", unitFont);
			float unitY = Theme.Ascent(g, font) - Theme.Ascent(g, unitFont);
			using (SolidBrush brush3 = new SolidBrush(enabled ? Theme.Muted : Theme.Dim))
			{
				g.DrawString("s", unitFont, brush3, unitX, unitY, StringFormat.GenericTypographic);
			}
			// Si la barra marca otros segundos: "→ 45 s  SIN APLICAR".
			if (slider.Value != seconds)
			{
				float x = unitX + unitSize.Width + (float)S(18f);
				Font pendingFont = Theme.Display(15f);
				string pending = "→ " + slider.Value + " s";
				using (SolidBrush brush4 = new SolidBrush(Theme.Yellow))
				{
					g.DrawString(pending, pendingFont, brush4, x, S(10f), StringFormat.GenericTypographic);
				}
				Theme.DrawText(g, "SIN APLICAR", Theme.Mono(7f), Theme.Mix(Theme.Yellow, Theme.Bg, 0.35f), new RectangleF(x, S(36f), S(160f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			}
			string line;
			Color c;
			if (!enabled)
			{
				line = "DESACTIVADO · EL PÚBLICO VE EL DIRECTO SIN RETRASO";
				c = Theme.Red;
			}
			else if (engine.Replaying)
			{
				line = "REPITIENDO LO ÚLTIMO MIENTRAS SUBE EL DELAY";
				c = Theme.Violet;
			}
			else if (engine.OutputActive)
			{
				line = "DELAY REAL " + ((double)engine.CurrentDelayMs / 1000.0).ToString("0.0") + " S · BÚFER " + ((double)engine.QueuedBytes / 1048576.0).ToString("0") + " MB";
				c = Theme.Muted;
			}
			else
			{
				line = ((seconds >= 60) ? FormatMin(seconds) : "SEGUNDOS DE RETRASO");
				c = Theme.Dim;
			}
			Theme.DrawText(g, line, Theme.Mono(7.5f), c, new RectangleF(left + 2f, S(64f), (float)r.Width - left - (float)S(20f), S(20f)), StringAlignment.Near, StringAlignment.Center);
		}

		// 90 → "1 MIN 30 S DE RETRASO".
		private static string FormatMin(int s)
		{
			int min = s / 60;
			int sec = s % 60;
			return min + " MIN" + ((sec > 0) ? (" " + sec + " S") : "") + " DE RETRASO";
		}

		// Recuadros de estado: 0 = OBS, 1 = calidades que llegan de OBS, 2 = overlays.
		private void PaintTile(Graphics g, Rectangle r, int i)
		{
			RectangleF frame = new RectangleF(0.5f, 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f);
			using (GraphicsPath path = Theme.Round(frame, S(14f)))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(225, Theme.Surface)))
				{
					g.FillPath(brush, path);
				}
				using (Pen pen = new Pen(Theme.Border))
				{
					g.DrawPath(pen, path);
				}
			}
			string detail = null;
			bool running = engine.Running;
			string title;
			string value;
			Color color;
			switch (i)
			{
			case 0:
				title = "OBS";
				if (!running)
				{
					value = "—";
					color = Theme.Dim;
				}
				else if (engine.InputActive)
				{
					value = "Conectado";
					color = Theme.Green;
				}
				else
				{
					value = "Esperando…";
					color = Theme.Yellow;
				}
				break;
			case 1:
			{
				title = "CALIDADES";
				string qualities = engine.Qualities;
				int topHeight = engine.TopHeight;
				bool twitchOn = dTwitch.Enabled;
				int inputTracks = engine.InputTracks;
				if (qualities.Length == 0)
				{
					value = ((inputTracks == 0) ? "—" : (inputTracks + ((inputTracks == 1) ? " calidad" : " calidades")));
					color = Theme.Dim;
					break;
				}
				string[] list = qualities.Split(new string[1] { " · " }, StringSplitOptions.None);
				value = list[0] + ((list.Length > 1) ? (" +" + (list.Length - 1)) : "");
				if (topHeight < 1080)
				{
					color = Theme.Red;
					detail = "Sin 1080p: OBS → Vídeo";
				}
				else if (list.Length > 1)
				{
					color = Theme.Green;
					detail = string.Join(" · ", list, 1, list.Length - 1);
				}
				else if (twitchOn)
				{
					// Twitch no crea calidades: si OBS manda una sola, el público solo tiene esa.
					color = Theme.Yellow;
					detail = "Twitch: 1 sola calidad";
				}
				else
				{
					color = Theme.Green;
					detail = "Cada plataforma crea el resto";
				}
				break;
			}
			default:
				title = "OVERLAYS OBS";
				value = "Gestionar";
				detail = "Delay · espectadores · chat";
				color = chatServer.Running ? Theme.Violet : Theme.Dim;
				break;
			}
			float dot = S(7f);
			using (SolidBrush brush2 = new SolidBrush(color))
			{
				g.FillEllipse(brush2, S(16f), S(14f), dot, dot);
			}
			Theme.DrawText(g, title, Theme.Mono(7f), Theme.Muted, new RectangleF(S(30f), S(8f), r.Width - S(40f), S(18f)), StringAlignment.Near, StringAlignment.Center);
			float y = ((detail != null) ? S(23f) : S(27f));
			Theme.DrawText(g, value, Theme.BodyBold(10.5f), Theme.Text, new RectangleF(S(15f), y, r.Width - S(24f), S(24f)), StringAlignment.Near, StringAlignment.Center);
			if (detail != null)
			{
				Theme.DrawText(g, detail, Theme.Body(7.25f), (color == Theme.Red) ? Color.FromArgb(255, 120, 130) : ((color == Theme.Yellow) ? Theme.Yellow : Theme.Muted), new RectangleF(S(15f), S(43f), r.Width - S(24f), S(14f)), StringAlignment.Near, StringAlignment.Center);
			}
		}

		// ---------- Servicio y delay ----------

		// Botón Iniciar/Detener servicio.
		private void ToggleService()
		{
			if (engine.Running)
			{
				Action stop = delegate
				{
					engine.Stop();
					StopVertical();
					AppendLog("Servicio detenido.");
					RefreshStatus();
				};
				// En directo, detener corta la emisión: hay que pulsar dos veces (sin ventana de "¿Seguro?").
				if (engine.OutputActive)
				{
					ConfirmClick(btnStart, "Pulsa otra vez para detener", stop);
				}
				else
				{
					stop();
				}
				return;
			}
			// Hace falta un plan (la prueba de 3 días, Básico o Completo).
			if (!CanStream())
			{
				return;
			}
			CommitPort();
			if (!ValidateDestinations())
			{
				return;
			}
			engine.UseFfmpeg = tgFfmpeg.Checked;
			if (engine.UseFfmpeg)
			{
				// Modo clásico: busca un ffmpeg que funcione (y que admita rtmps:// si algún destino lo usa).
				bool needTls = false;
				foreach (Destination d in EnabledDestinations())
				{
					if (d.Server.Trim().StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
					{
						needTls = true;
					}
				}
				List<string> candidates = Engine.FfmpegCandidates();
				if (candidates.Count == 0)
				{
					OfferFfmpegInstall("El modo clásico necesita ffmpeg (gratuito) y no lo encuentra en este PC.");
					return;
				}
				string found = null;
				string firstError = null;
				foreach (string candidate in candidates)
				{
					string problem = Engine.CheckFfmpeg(candidate, needTls);
					if (problem == null)
					{
						found = candidate;
						break;
					}
					if (firstError == null)
					{
						firstError = candidate + "\n→ " + problem;
					}
				}
				if (found == null)
				{
					OfferFfmpegInstall("El ffmpeg que hay en este PC no sirve:\n" + firstError);
					return;
				}
				engine.FfmpegPath = found;
				AppendLog("Modo clásico: ffmpeg " + found + " (solo se envía la calidad principal).");
			}
			engine.InputPort = port;
			engine.AllowLan = tgLan.Checked;
			List<Destination> mainDests = EnabledDestinations();
			if (VerticalOn)
			{
				// TikTok va por el motor vertical (imagen de Aitum Vertical).
				mainDests.Remove(dTikTok);
			}
			engine.Destinations = mainDests;
			try
			{
				engine.Start();
				if (VerticalOn)
				{
					StartVertical();
				}
			}
			catch (Exception ex)
			{
				engine.Stop();
				StopVertical();
				AppendLog("No se puede iniciar: " + ex.Message);
				Toast("No se puede iniciar: puede que otro programa use ese puerto. Cámbialo en Ajustes → OBS (por ejemplo a 1936) y copia de nuevo el servidor en OBS.", "error", "Abrir OBS", delegate
				{
					setPick = 1;
					GoPage(PageSettings);
				});
				return;
			}
			SaveConfig();
			AppendLog("Esperando a OBS en " + ObsServer + ". Destinos: " + DestSummary() + ".");
			if (dTwitch.Enabled && dTwitch.Key.Trim().Length == 0)
			{
				AppendLog("Twitch sin clave guardada: se usará la que ponga OBS (servicio Twitch).");
			}
			if (tgLan.Checked)
			{
				AppendLog("Si Windows pregunta por el Firewall, permite el acceso en redes privadas.");
			}
			RefreshStatus();
		}

		// Ofrece instalar ffmpeg con winget (solo hace falta en el modo clásico).
		private void OfferFfmpegInstall(string reason)
		{
			DialogResult answer = I18n.Show(this, reason + "\n\n¿Quieres instalar ffmpeg ahora con winget? Se abrirá una ventana; acepta lo que pida y, cuando termine, vuelve a pulsar \"Iniciar servicio\".\n\n(También puedes desactivar el modo clásico: Vortexs Live ya no necesita ffmpeg.)", "Falta ffmpeg", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk);
			if (answer != DialogResult.Yes)
			{
				return;
			}
			try
			{
				Process.Start(new ProcessStartInfo("cmd.exe", "/c winget install -e --id Gyan.FFmpeg & echo. & echo Listo. Ya puedes cerrar esta ventana. & pause")
				{
					UseShellExecute = true
				});
			}
			catch (Exception ex)
			{
				I18n.Show(this, "No se pudo abrir winget (" + ex.Message + ").\n\nDescarga ffmpeg de gyan.dev/ffmpeg/builds y copia ffmpeg.exe junto a VortexsLive.exe.", "Falta ffmpeg");
			}
		}

		// Cambia el delay (0–300 s). En directo, al subirlo se repite o congela lo último y al bajarlo se salta.
		private void ApplyDelay(int seconds)
		{
			seconds = Math.Max(0, Math.Min(MaxDelay, seconds));
			int previous = engine.TargetMs / 1000;
			engine.TargetMs = seconds * 1000;
			SyncVertical();
			if (slider.Value != seconds)
			{
				slider.Value = seconds;
			}
			if (engine.OutputActive && engine.Enabled && seconds != previous)
			{
				AppendLog((seconds > previous) ? ("Delay ↑ a " + seconds + " s: el público verá " + (engine.ReplayOnIncrease ? "una repetición" : "una pausa") + " de " + (seconds - previous) + " s.") : ("Delay ↓ a " + seconds + " s: el público saltará " + (previous - seconds) + " s."));
			}
			else if (seconds != previous)
			{
				AppendLog("Delay: " + seconds + " s.");
			}
			if (engine.Enabled && seconds != previous && seconds > 0)
			{
				AnnounceDelaySoon();
			}
			SaveConfig();
			RefreshStatus();
		}

		// Activa o quita el delay (interruptor, Ctrl+Alt+D, Mando o comando del chat).
		private void SetEnabled(bool on)
		{
			engine.Enabled = on;
			SyncVertical();
			AppendLog(on ? "Delay activado." : "Delay desactivado (en directo).");
			AnnounceDelaySoon();
			SaveConfig();
			RefreshStatus();
		}

		// Botón "Aplicar N s" / "Delay aplicado".
		private void UpdateApply()
		{
			bool changed = slider.Value != engine.TargetMs / 1000;
			btnApply.Muted = !changed;
			btnApply.Text = (changed ? ("Aplicar " + slider.Value + " s") : "Delay aplicado");
			btnApply.Invalidate();
			RefreshPresetButtons();
		}

		// Se llama cada medio segundo: pasa al Registro lo que llegó de otros hilos y pone al día la ventana.
		private void RefreshStatus()
		{
			lock (pendingLog)
			{
				while (pendingLog.Count > 0)
				{
					AppendLog(pendingLog.Dequeue());
				}
			}
			if (engine.LowQualityAlert)
			{
				// OBS no manda 1080p: se avisa una vez, mientras aún está en el delay.
				engine.LowQualityAlert = false;
				GoPage(PageLive);
				timer.Stop();
				I18n.Show(this, "OBS está enviando como máximo " + engine.TopHeight + "p: tu público no tendrá 1080p.\n\nAún no ha salido nada en directo (está en el delay). Para arreglarlo:\n1. Detén la transmisión en OBS.\n2. OBS → Ajustes → Vídeo → Resolución de salida (escalada): 1920x1080.\n3. Si usas Transmisión mejorada: Ajustes → Emisión → Ancho de banda máximo: Auto.\n4. Vuelve a iniciar la transmisión.", "Sin calidad 1080p", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				timer.Start();
			}
			if (tgDelay.Checked != engine.Enabled)
			{
				loading = true;
				tgDelay.Checked = engine.Enabled;
				loading = false;
			}
			tgDelay.Text = (engine.Enabled ? "Activado" : "Desactivado");
			UpdateApply();
			bool running = engine.Running;
			int count = EnabledDestinations().Count;
			// Si está pidiendo "Pulsa otra vez…" (Confirm.cs) se deja como está.
			if (!IsArmed(btnStart))
			{
				btnStart.Text = (running ? "■  Detener servicio" : ((count == 0) ? "▶  Iniciar servicio" : ((count == 1) ? ("▶  Iniciar · " + DestSummary()) : ("▶  Iniciar en " + count + " destinos"))));
				btnStart.Kind = (running ? ButtonKind.Ghost : ButtonKind.Primary);
			}
			btnStart.Invalidate();
			btnCut.Enabled = running;
			// Con el servicio en marcha no se puede cambiar el puerto ni el modo.
			fldPort.Locked = running;
			tgFfmpeg.Enabled = !running;
			tgLan.Enabled = !running;
			RefreshDestinationsUi();
			header.Invalidate();
			heroBox.Invalidate();
			foreach (PaintBox tile in tiles)
			{
				tile.Invalidate();
			}
		}
	}
}
