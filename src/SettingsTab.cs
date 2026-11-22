// Pestaña Ajustes: destinos (Twitch, Kick, YouTube y otro servidor RTMP), datos para OBS
// y opciones generales. También dibuja la franja de destinos de la pestaña Directo.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// Dirección que hay que poner en OBS como "servidor personalizado".
		private string ObsServer
		{
			get
			{
				return "rtmp://" + (tgLan.Checked ? LanIp() : "127.0.0.1") + ":" + port + "/app";
			}
		}

		// ---------- Construcción de la pestaña ----------

		private void BuildSettings(int y0)
		{
			// Arriba, las secciones; debajo, el recuadro de la elegida.
			BuildSettingsPicker(y0);
			y0 += 38;
			BuildDestinations(y0);

			// Recuadro OBS: servidor para copiar en OBS, puerto y modo de 2 PCs.
			Card obsCard = new Card
			{
				Title = "OBS"
			};
			Place(this, obsCard, 20f, y0, 420f, 166f, PageSettings);
			VButton howTo = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Cómo configurarlo →",
				FontSize = 8.5f
			};
			Place(obsCard, howTo, 220f, 13f, 180f, 24f);
			howTo.Click += delegate
			{
				OpenGuide();
			};
			Label(obsCard, "SERVIDOR PERSONALIZADO PARA OBS", 20f, 42f, 300f);
			fldObs = new Field
			{
				ReadOnly = true
			};
			Place(obsCard, fldObs, 20f, 60f, 292f, 36f);
			btnCopyObs = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar",
				FontSize = 9f
			};
			Place(obsCard, btnCopyObs, 320f, 60f, 80f, 36f);
			btnCopyObs.Click += delegate
			{
				CopyText(ObsServer, btnCopyObs, "Copiar");
			};
			Label(obsCard, "PUERTO", 20f, 102f, 80f);
			fldPort = new Field();
			Place(obsCard, fldPort, 20f, 120f, 84f, 34f);
			fldPort.Box.Text = "1935";
			fldPort.Box.Leave += delegate
			{
				CommitPort();
			};
			fldPort.Box.KeyDown += delegate(object o, KeyEventArgs e)
			{
				if (e.KeyCode == Keys.Enter)
				{
					CommitPort();
					e.SuppressKeyPress = true;
				}
			};
			tgLan = new Toggle
			{
				Text = "OBS en otro PC de la red",
				Hint = "Configuración de 2 PCs"
			};
			Place(obsCard, tgLan, 120f, 116f, 280f, 44f);
			tgLan.CheckedChanged += delegate
			{
				fldObs.Box.Text = ObsServer;
				if (!loading)
				{
					SaveConfig();
				}
			};

			// Recuadro de opciones.
			Card optionsCard = new Card();
			Place(this, optionsCard, 20f, y0, 420f, 104f, PageSettings);
			setCards = new Card[3] { destCard, obsCard, optionsCard };
			tgReplay = new Toggle
			{
				Text = "Al subir el delay, repetir lo último (si no, se congela)"
			};
			Place(optionsCard, tgReplay, 18f, 10f, 384f, 28f);
			tgReplay.Checked = true;
			tgReplay.CheckedChanged += delegate
			{
				engine.ReplayOnIncrease = tgReplay.Checked;
				SyncVertical();
				if (!loading)
				{
					SaveConfig();
				}
			};
			tgTop = new Toggle
			{
				Text = "Ventana siempre visible"
			};
			Place(optionsCard, tgTop, 18f, 40f, 384f, 28f);
			tgTop.CheckedChanged += delegate
			{
				base.TopMost = tgTop.Checked;
				if (!loading)
				{
					SaveConfig();
				}
			};
			tgFfmpeg = new Toggle
			{
				Text = "Modo clásico con ffmpeg (solo una calidad)"
			};
			Place(optionsCard, tgFfmpeg, 18f, 70f, 384f, 28f);
			tgFfmpeg.CheckedChanged += delegate
			{
				if (!loading)
				{
					SaveConfig();
				}
			};
		}

		// Aplica el puerto escrito (1024–65535; si no es válido, vuelve al anterior).
		private void CommitPort()
		{
			int value;
			if (int.TryParse(fldPort.Box.Text.Trim(), out value) && value >= 1024 && value <= 65535)
			{
				port = value;
			}
			fldPort.Box.Text = port.ToString();
			fldObs.Box.Text = ObsServer;
			if (!loading)
			{
				SaveConfig();
			}
		}

		// ---------- Destinos ----------

		// Servidor RTMP de cada plataforma (botón "Por defecto").
		private static string DefaultServer(Destination d)
		{
			if (d.Id == "twitch")
			{
				return "rtmp://live.twitch.tv/app";
			}
			if (d.Id == "kick")
			{
				return KickServer;
			}
			if (d.Id == "youtube")
			{
				return "rtmp://a.rtmp.youtube.com/live2";
			}
			return "";
		}

		private void InitDestinations()
		{
			if (dests == null)
			{
				dests = new Destination[5] { dTwitch, dKick, dYouTube, dTikTok, dCustom };
			}
		}

		// Recuadro "Destinos": un interruptor y un botón Editar por destino.
		private Card destCard;

		private void BuildDestinations(int y0)
		{
			InitDestinations();
			Card card = new Card
			{
				Title = "Destinos"
			};
			Place(this, card, 20f, y0, 420f, 228f, PageSettings);
			destCard = card;
			Box(card, 160f, 14f, 240f, 24f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "Se emite a la vez en los activados", Theme.Body(7.75f), Theme.Muted, r, StringAlignment.Far, StringAlignment.Center);
			});
			for (int i = 0; i < dests.Length; i++)
			{
				int idx = i;
				tgDest[i] = new Toggle
				{
					Text = dests[i].Name,
					Hint = ""
				};
				Place(card, tgDest[i], 18f, 42 + i * 36, 276f, 36f);
				tgDest[i].CheckedChanged += delegate
				{
					dests[idx].Enabled = tgDest[idx].Checked;
					RefreshDestinationsUi();
					if (!loading)
					{
						SaveConfig();
					}
				};
				btnEditDest[i] = new VButton
				{
					Kind = ButtonKind.Ghost,
					Text = "Editar",
					FontSize = 8.5f
				};
				Place(card, btnEditDest[i], 300f, 44 + i * 36, 100f, 28f);
				btnEditDest[i].Click += delegate
				{
					EditDestination(dests[idx]);
				};
			}
		}

		// Texto bajo cada destino: servidor abreviado y si tiene clave.
		private string DestHint(Destination d)
		{
			if (d.Server.Trim().Length == 0)
			{
				return "Sin servidor · pulsa Editar";
			}
			string host = d.Server.Trim();
			int scheme = host.IndexOf("://", StringComparison.Ordinal);
			if (scheme >= 0)
			{
				host = host.Substring(scheme + 3);
			}
			int slash = host.IndexOf('/');
			if (slash > 0)
			{
				host = host.Substring(0, slash);
			}
			if (host.Length > 26)
			{
				host = host.Substring(0, 12) + "…" + host.Substring(host.Length - 12);
			}
			string keyState = ((d.Key.Trim().Length > 0) ? "clave guardada" : (d.IsTwitch ? "clave: la pone OBS" : "falta la clave"));
			if (d == dTikTok && tiktokVertical)
			{
				keyState += " · vertical";
			}
			return host + " · " + keyState;
		}

		// Pone al día los interruptores de destinos (no se pueden cambiar con el servicio en marcha).
		private void RefreshDestinationsUi()
		{
			if (dests == null || tgDest[0] == null)
			{
				return;
			}
			bool running = engine.Running;
			for (int i = 0; i < dests.Length; i++)
			{
				Destination d = dests[i];
				if (tgDest[i].Checked != d.Enabled)
				{
					bool wasLoading = loading;
					loading = true;
					tgDest[i].Checked = d.Enabled;
					loading = wasLoading;
				}
				tgDest[i].Hint = DestHint(d);
				tgDest[i].Enabled = !running;
				tgDest[i].Invalidate();
				btnEditDest[i].Enabled = !running;
				btnEditDest[i].Invalidate();
			}
			if (destStrip != null)
			{
				destStrip.Invalidate();
			}
		}

		private List<Destination> EnabledDestinations()
		{
			List<Destination> list = new List<Destination>();
			foreach (Destination d in dests)
			{
				if (d.Enabled)
				{
					list.Add(d);
				}
			}
			return list;
		}

		// "Twitch + Kick"…
		private string DestSummary()
		{
			List<string> names = new List<string>();
			foreach (Destination d in EnabledDestinations())
			{
				names.Add(d.Name);
			}
			return string.Join(" + ", names.ToArray());
		}

		// Antes de iniciar: al menos un destino, servidores rtmp(s):// de verdad y aviso si falta alguna clave.
		private bool ValidateDestinations()
		{
			List<Destination> enabled = EnabledDestinations();
			if (enabled.Count == 0)
			{
				GoPage(PageSettings);
				I18n.Show(this, "Activa al menos un destino en Ajustes → Destinos.", "Sin destinos");
				return false;
			}
			List<string> withoutKey = new List<string>();
			foreach (Destination d in enabled)
			{
				string server = d.Server.Trim();
				if (server.Length == 0 || (!server.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) && !server.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase)))
				{
					GoPage(PageSettings);
					I18n.Show(this, d.Name + ": el servidor debe empezar por rtmp:// o rtmps://", "Servidor no válido");
					return false;
				}
				if (server.Contains("127.0.0.1") || server.ToLowerInvariant().Contains("localhost"))
				{
					GoPage(PageSettings);
					I18n.Show(this, d.Name + ": en Destinos va el servidor de la plataforma, no la dirección local.\n\nLa dirección local (127.0.0.1) es la que se pone en OBS.", "Servidor no válido");
					return false;
				}
				if (!d.IsTwitch && d.Key.Trim().Length == 0)
				{
					withoutKey.Add(d.Name);
				}
			}
			if (withoutKey.Count > 0 && I18n.Show(this, string.Join(", ", withoutKey.ToArray()) + ((withoutKey.Count == 1) ? " no tiene" : " no tienen") + " clave guardada, así que no se emitirá ahí (salvo que OBS mande su clave).\n\n¿Iniciar igualmente?", "Falta la clave", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				GoPage(PageSettings);
				return false;
			}
			return true;
		}

		// Ventana "Destino": servidor y clave de transmisión.
		private void EditDestination(Destination d)
		{
			// TikTok tiene además la opción de imagen vertical (60 px más de ventana).
			bool isTikTok = d == dTikTok;
			float extra = isTikTok ? 60f : 0f;
			Form f = DarkForm("Destino: " + d.Name, 440, 392 + (int)extra);
			string note = (d.IsTwitch ? "Con la Transmisión mejorada de OBS (servicio Twitch con servidor personalizado) la clave la pone OBS y puedes dejarla vacía." : ((d.Id == "kick") ? "Copia el \"Stream URL\" y la \"Stream Key\" de tu panel de Kick (Settings → Stream URL & Key)." : ((d.Id == "youtube") ? "Copia la clave de YouTube Studio → Emitir en directo → Configuración de la emisión." : ((d.Id == "tiktok") ? "Si tu cuenta de TikTok tiene clave de emisión, está en TikTok LIVE Producer (en la web): copia el \"Server URL\" y la \"Stream Key\". TikTok suele dar una clave nueva en cada directo." : "Cualquier plataforma que admita RTMP: copia su URL de servidor y su clave."))));
			Card card = new Card
			{
				Title = d.Name,
				Subtitle = (d.IsTwitch ? "Recibe todas las calidades de OBS" : "Recibe la calidad principal y crea el resto")
			};
			Place(f, card, 20f, 18f, 400f, 292f + extra);
			Label(card, "SERVIDOR", 20f, 64f, 200f);
			VButton byDefault = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Por defecto",
				FontSize = 8f
			};
			if (DefaultServer(d).Length > 0)
			{
				Place(card, byDefault, 260f, 60f, 120f, 22f);
			}
			Field srv = new Field();
			Place(card, srv, 20f, 82f, 360f, 36f);
			srv.Box.Text = d.Server;
			byDefault.Click += delegate
			{
				srv.Box.Text = DefaultServer(d);
			};
			Label(card, "CLAVE DE TRANSMISIÓN", 20f, 126f, 200f);
			VButton show = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Mostrar",
				FontSize = 8f
			};
			Place(card, show, 260f, 122f, 120f, 22f);
			Field key = new Field();
			Place(card, key, 20f, 144f, 360f, 36f);
			key.Box.UseSystemPasswordChar = true;
			key.Box.Text = d.Key;
			show.Click += delegate
			{
				key.Box.UseSystemPasswordChar = !key.Box.UseSystemPasswordChar;
				show.Text = (key.Box.UseSystemPasswordChar ? "Mostrar" : "Ocultar");
			};
			Box(card, 20f, 190f, 360f, 90f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, note + " La clave se guarda cifrada en VortexsLive.ini.", Theme.Body(8f), Theme.Muted, r);
			});
			Toggle tVertical = null;
			if (isTikTok)
			{
				tVertical = new Toggle
				{
					Text = "Imagen vertical (Aitum Vertical)",
					Hint = "En Aitum Vertical: " + VerticalServer + " · clave tiktok",
					Checked = tiktokVertical
				};
				Place(card, tVertical, 18f, 288f, 364f, 44f);
			}
			VButton cancel = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Cancelar",
				FontSize = 10f
			};
			VButton save = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Guardar",
				FontSize = 10f
			};
			Place(f, cancel, 20f, 326f + extra, 130f, 44f);
			Place(f, save, 160f, 326f + extra, 260f, 44f);
			cancel.Click += delegate
			{
				f.Close();
			};
			save.Click += delegate
			{
				string server = srv.Box.Text.Trim().TrimEnd('/');
				if (server.Length > 0 && !server.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase) && !server.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase))
				{
					I18n.Show(f, "El servidor debe empezar por rtmp:// o rtmps://", "Servidor no válido");
				}
				else
				{
					d.Server = server;
					d.Key = key.Box.Text.Trim();
					if (tVertical != null)
					{
						tiktokVertical = tVertical.Checked;
					}
					// Con servidor y clave, se activa solo.
					if (server.Length > 0 && (d.Key.Length > 0 || d.IsTwitch))
					{
						d.Enabled = true;
					}
					SaveConfig();
					RefreshDestinationsUi();
					f.Close();
				}
			};
			f.ShowDialog(this);
		}

		// Franja "Se emitirá en / Emitiendo en" de la pestaña Directo: una casilla por destino activo
		// con su estado (preparado, conectando, en directo, reintentando, falta clave…).
		private void PaintDestStrip(Graphics g, Rectangle r)
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
			bool running = engine.Running;
			List<Destination> list = EnabledDestinations();
			int liveCount = engine.LiveCount + engineV.LiveCount;
			Theme.DrawText(g, running ? "EMITIENDO EN" : "SE EMITIRÁ EN", Theme.Mono(7f), Theme.Muted, new RectangleF(S(16f), S(6f), S(200f), S(20f)), StringAlignment.Near, StringAlignment.Center);
			string summary = ((!running) ? (list.Count + ((list.Count == 1) ? " DESTINO" : " DESTINOS")) : ((liveCount > 0) ? (liveCount + " EN DIRECTO") : "AÚN SIN DIRECTO"));
			Theme.DrawText(g, summary, Theme.Mono(7f), (liveCount > 0) ? Theme.Mix(Theme.Red, Color.White, 0.3f) : Theme.Dim, new RectangleF(S(200f), S(6f), r.Width - S(216f), S(20f)), StringAlignment.Far, StringAlignment.Center);
			if (list.Count == 0)
			{
				Theme.DrawText(g, "Ningún destino activo · actívalos en Ajustes", Theme.Body(8.5f), Theme.Yellow, new RectangleF(S(16f), S(28f), r.Width - S(32f), S(36f)), StringAlignment.Center, StringAlignment.Center);
				return;
			}
			float gap = S(8f);
			float x = S(12f);
			float y = S(28f);
			float height = r.Height - S(28f) - S(10f);
			float width = ((float)(r.Width - 2 * S(12f)) - gap * (float)(list.Count - 1)) / (float)list.Count;
			foreach (Destination item in list)
			{
				Sink sink = null;
				if (running)
				{
					foreach (Sink candidate in AllSinks())
					{
						if (candidate.D == item)
						{
							sink = candidate;
						}
					}
				}
				bool filledDot = true;
				string state;
				Color color;
				if (!running || sink == null)
				{
					state = "PREPARADO";
					color = Theme.Violet;
					filledDot = false;
				}
				else
				{
					switch (sink.State)
					{
					case SinkState.Live:
						state = (engine.Replaying ? "REPITIENDO" : ((sink.Tracks > 1) ? ("EN DIRECTO · " + sink.Tracks + " CAL.") : ((sink.Quality.Length > 0) ? ("EN DIRECTO · " + sink.Quality.ToUpperInvariant()) : "EN DIRECTO")));
						color = Theme.Red;
						break;
					case SinkState.Connecting:
						state = "CONECTANDO";
						color = Theme.Yellow;
						break;
					case SinkState.Retrying:
						state = "REINTENTANDO";
						color = Color.FromArgb(255, 150, 60);
						break;
					case SinkState.NoKey:
						state = ((sink.Note.Length > 0) ? sink.Note.ToUpperInvariant() : "FALTA CLAVE");
						color = Theme.Red;
						filledDot = false;
						break;
					default:
						// TikTok vertical espera a su propia señal (Aitum Vertical).
						bool inputOn = (item == dTikTok && VerticalOn) ? engineV.InputActive : engine.InputActive;
						state = (inputOn ? "CARGANDO DELAY" : "ESPERANDO A OBS");
						color = Theme.Violet;
						filledDot = false;
						break;
					}
				}
				RectangleF cell = new RectangleF(x, y, width, height);
				bool live = sink != null && sink.State == SinkState.Live;
				using (GraphicsPath path2 = Theme.Round(cell, S(10f)))
				{
					using (SolidBrush brush2 = new SolidBrush(live ? Color.FromArgb(26, Theme.Red) : Theme.Input))
					{
						g.FillPath(brush2, path2);
					}
					using (Pen pen2 = new Pen(live ? Color.FromArgb(150, Theme.Red) : Theme.Border))
					{
						g.DrawPath(pen2, path2);
					}
				}
				float dot = S(7f);
				RectangleF dotRect = new RectangleF(cell.X + (float)S(10f), cell.Y + (float)S(9f), dot, dot);
				if (filledDot)
				{
					if (live && pulse)
					{
						Card.Glow(g, RectangleF.Inflate(dotRect, dot, dot), Color.FromArgb(110, color));
					}
					using (SolidBrush brush3 = new SolidBrush(color))
					{
						g.FillEllipse(brush3, dotRect);
					}
				}
				else
				{
					using (Pen pen3 = new Pen(color, 1.4f))
					{
						g.DrawEllipse(pen3, dotRect);
					}
				}
				Theme.DrawText(g, item.Name, Theme.BodyBold(9f), Theme.Text, new RectangleF(dotRect.Right + (float)S(6f), cell.Y + (float)S(2f), cell.Width - (float)S(28f), S(20f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, state, Theme.Mono(6.5f), filledDot ? Theme.Mix(color, Color.White, 0.25f) : Theme.Muted, new RectangleF(cell.X + (float)S(10f), cell.Y + (float)S(21f), cell.Width - (float)S(14f), S(14f)), StringAlignment.Near, StringAlignment.Center);
				x += width + gap;
			}
		}

		// ---------- Configuración (VortexsLive.ini) ----------

		private void LoadDestinations(Dictionary<string, string> raw)
		{
			InitDestinations();
			string value;
			Func<string, string> get = (string k) => (!raw.TryGetValue(k, out value)) ? null : value;
			bool found = false;
			foreach (Destination d in dests)
			{
				string on = get("d_" + d.Id + "_on");
				if (on != null)
				{
					found = true;
					d.Enabled = on == "1";
					d.Server = get("d_" + d.Id + "_server") ?? DefaultServer(d);
					d.Key = Secret.Unprotect(get("d_" + d.Id + "_key") ?? "");
				}
			}
			if (!found)
			{
				// Configuración de versiones antiguas (un solo destino con "server" y "key"):
				// se adivina la plataforma por el servidor.
				string oldServer = get("server");
				string oldKey = get("key");
				if (!string.IsNullOrEmpty(oldServer))
				{
					string lower = oldServer.ToLowerInvariant();
					Destination target = lower.Contains("tiktok") ? dTikTok : ((lower.Contains("fa723fc1b171") || lower.Contains("kick")) ? dKick : (lower.Contains("youtube") ? dYouTube : ((lower.Contains("twitch") || lower.Contains("contribute.live-video.net")) ? dTwitch : dCustom)));
					foreach (Destination d in dests)
					{
						d.Enabled = false;
					}
					target.Enabled = true;
					target.Server = oldServer.Trim();
					target.Key = Secret.Unprotect(oldKey ?? "");
				}
			}
			RefreshDestinationsUi();
		}

		private void SaveDestinations(List<string> lines)
		{
			if (dests != null)
			{
				foreach (Destination d in dests)
				{
					lines.Add("d_" + d.Id + "_on=" + (d.Enabled ? "1" : "0"));
					lines.Add("d_" + d.Id + "_server=" + d.Server.Trim());
					lines.Add("d_" + d.Id + "_key=" + Secret.Protect(d.Key));
				}
			}
		}
	}
}
