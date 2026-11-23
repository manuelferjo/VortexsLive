// Pestaña Chat: chat conjunto de Twitch, Kick, YouTube y TikTok, espectadores, follows y donaciones.
// También arranca la página local (127.0.0.1:8935…) que usan el chat, los overlays de OBS y el Mando.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// ---------- Construcción de la pestaña ----------

		private void BuildChat(int y0)
		{
			// Recuadro con los canales.
			Card card = new Card
			{
				Title = "Chat conjunto",
				Subtitle = "Twitch, Kick, YouTube y TikTok en un solo chat."
			};
			Place(this, card, 20f, y0, 420f, 276f, PageChat);
			tgChat = new Toggle
			{
				Text = "Activado"
			};
			Place(card, tgChat, 268f, 9f, 136f, 28f);
			tgChat.CheckedChanged += delegate
			{
				if (!loading)
				{
					if (tgChat.Checked)
					{
						StartChat(true);
					}
					else
					{
						StopChat();
					}
					SaveConfig();
				}
			};
			Label(card, "TWITCH · NOMBRE DEL CANAL", 20f, 58f, 300f);
			fldChatTw = new Field();
			Place(card, fldChatTw, 20f, 74f, 380f, 32f);
			Label(card, "KICK · NOMBRE DEL CANAL", 20f, 110f, 300f);
			fldChatKick = new Field();
			Place(card, fldChatKick, 20f, 126f, 380f, 32f);
			Label(card, "YOUTUBE · @CANAL O ENLACE DEL DIRECTO", 20f, 162f, 360f);
			fldChatYt = new Field();
			Place(card, fldChatYt, 20f, 178f, 380f, 32f);
			Label(card, "TIKTOK · @USUARIO", 20f, 214f, 200f);
			fldChatTk = new Field();
			Place(card, fldChatTk, 20f, 230f, 250f, 32f);
			// TikTok necesita la clave gratuita de Euler Stream: el botón dice si ya está puesta.
			VButton btnTkKey = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Clave Euler Stream",
				FontSize = 8.5f
			};
			Place(card, btnTkKey, 278f, 230f, 122f, 32f);
			btnTkKey.Click += delegate
			{
				TikTokKeyDialog();
				btnTkKey.Text = (tkKey.Length > 0) ? "Clave puesta ✓" : "Clave Euler Stream";
				btnTkKey.Invalidate();
			};
			tkKeyButton = btnTkKey;
			foreach (Field field in new Field[4] { fldChatTw, fldChatKick, fldChatYt, fldChatTk })
			{
				field.Box.TextChanged += delegate
				{
					if (!loading)
					{
						SaveConfig();
					}
				};
			}

			// Espectadores y estado de cada chat.
			chatViewers = Box(this, 20f, y0 + 284, 420f, 100f, PaintChatViewers, PageChat);

			// Botones.
			int buttonsTop = y0 + 392;
			btnChatOpen = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Abrir chat",
				FontSize = 9.5f
			};
			Place(this, btnChatOpen, 20f, buttonsTop, 136f, 36f, PageChat);
			btnChatOpen.Click += delegate
			{
				if (EnsureChatServer())
				{
					Http.OpenBrowser(chatServer.Url);
				}
			};
			btnChatDock = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "URL para OBS",
				FontSize = 9f
			};
			Place(this, btnChatDock, 162f, buttonsTop, 136f, 36f, PageChat);
			btnChatDock.Click += delegate
			{
				if (EnsureChatServer())
				{
					CopyText(chatServer.Url, btnChatDock, "URL para OBS");
				}
			};
			VButton btnMod = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Moderación",
				FontSize = 9f
			};
			Place(this, btnMod, 304f, buttonsTop, 136f, 36f, PageChat);
			btnMod.Click += delegate
			{
				AutoModDialog();
			};
			btnChatAlerts = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Follows y donaciones",
				FontSize = 9f
			};
			Place(this, btnChatAlerts, 20f, buttonsTop + 42, 420f, 34f, PageChat);
			btnChatAlerts.Click += delegate
			{
				AlertsDialog();
			};
			VButton btnExtras = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Avisos de delay y comandos del chat",
				FontSize = 9f
			};
			Place(this, btnExtras, 20f, buttonsTop + 82, 420f, 34f, PageChat);
			btnExtras.Click += delegate
			{
				AnnounceDialog();
			};
			// Cada segundo: espectadores y texto del botón "Follows y donaciones".
			System.Windows.Forms.Timer refreshTimer = new System.Windows.Forms.Timer
			{
				Interval = 1000
			};
			refreshTimer.Tick += delegate
			{
				if (chatViewers.Visible)
				{
					chatViewers.Invalidate();
					string summary = AlertsSummary();
					if (btnChatAlerts.Text != summary)
					{
						btnChatAlerts.Text = summary;
						btnChatAlerts.Invalidate();
					}
				}
			};
			refreshTimer.Start();

			// Al abrir la ventana: página local, Mando, chat (si estaba activado) y buscar actualizaciones.
			base.Shown += delegate
			{
				StartWebQuietly();
				UpdateMandoUrl();
				if (chatAutoStart)
				{
					StartChat(false);
				}
				StartUpdateCheck();
			};
			base.FormClosed += delegate
			{
				if (chatHub != null)
				{
					chatHub.Stop();
				}
				chatServer.Stop();
			};
		}

		// ---------- Página local (chat, overlays y Mando) ----------

		// Estado que lee el overlay de OBS cada medio segundo.
		private string DelayStateJson()
		{
			bool on = engine.Enabled && engine.TargetMs > 0;
			return "{\"on\":" + (on ? "true" : "false") + ",\"s\":" + engine.TargetMs / 1000 + ",\"live\":" + (engine.OutputActive ? "true" : "false") + ",\"replay\":" + (engine.Replaying ? "true" : "false") + ",\"hideOff\":" + (ovDelayHideOff ? "true" : "false") + TrPart() + ",\"k\":" + ovDelayScale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
		}

		// Transición "Activando delay" / "Delay quitado" del overlay, si hay una en marcha.
		private string TrPart()
		{
			string t = TransitionJson();
			return (t == null) ? "" : (",\"tr\":" + t);
		}

		private void CopyOverlayUrl()
		{
			if (!EnsureChatServer())
			{
				return;
			}
			string url = "http://127.0.0.1:" + chatServer.Port + "/delay";
			try
			{
				Clipboard.SetText(url);
			}
			catch
			{
				return;
			}
			overlayCopiedUntil = DateTime.Now.AddSeconds(2.0);
			tiles[2].Invalidate();
			AppendLog("Overlay de delay: " + url + " (en OBS: fuente Navegador de 1920x1080 con esa URL; sale arriba a la derecha).");
		}

		// El overlay tiene que funcionar aunque el chat esté apagado: se abre la página local al arrancar.
		// Usa el primer puerto libre entre 8935 y 8944. Si no hay ninguno, no avisa.
		private void StartWebQuietly()
		{
			if (chatServer.Running)
			{
				return;
			}
			for (int p = ChatPort; p < ChatPort + 10; p++)
			{
				try
				{
					chatServer.Log = delegate(string m)
					{
						LogLater(m);
					};
					chatServer.OnInfo = ChatSendInfo;
					chatServer.OnSend = ChatSendAll;
					chatServer.OnSendReply = ChatSendAll;
					chatServer.OnWidgets = WidgetsJson;
					chatServer.OnSound = AlertSoundFile;
					chatServer.OnImage = AlertImageFile;
					chatServer.OnDelayState = DelayStateJson;
					chatServer.OnViewersState = ViewersStateJson;
					WireServer();
					chatServer.Start(p, remoteLan);
					return;
				}
				catch
				{
				}
			}
		}

		// Igual que StartWebQuietly, pero si no puede abrir la página lo dice. Devuelve true si está abierta.
		private bool EnsureChatServer()
		{
			if (chatServer.Running)
			{
				return true;
			}
			for (int p = ChatPort; p < ChatPort + 10; p++)
			{
				try
				{
					chatServer.Log = delegate(string m)
					{
						LogLater(m);
					};
					chatServer.OnInfo = ChatSendInfo;
					chatServer.OnSend = ChatSendAll;
					chatServer.OnSendReply = ChatSendAll;
					chatServer.OnWidgets = WidgetsJson;
					chatServer.OnSound = AlertSoundFile;
					chatServer.OnImage = AlertImageFile;
					chatServer.OnDelayState = DelayStateJson;
					chatServer.OnViewersState = ViewersStateJson;
					WireServer();
					chatServer.Start(p, remoteLan);
					return true;
				}
				catch
				{
				}
			}
			Toast("No se pudo abrir la página del chat: los puertos " + ChatPort + "–" + (ChatPort + 9) + " están ocupados.", "error");
			return false;
		}

		// ---------- Escribir en los chats ----------

		// En qué plataformas se puede escribir (las conectadas en Directo → Título y categoría).
		private Dictionary<string, object> ChatSendInfo()
		{
			Dictionary<string, object> send = new Dictionary<string, object>
			{
				{ "twitch", twitch.Connected },
				{ "kick", kick.Connected },
				{ "youtube", youtube.Connected }
			};
			return new Dictionary<string, object> { { "send", send } };
		}

		// Envía un mensaje a varios chats a la vez (un hilo por plataforma, máximo 20 s)
		// y devuelve el resultado de cada una ("ok" o el error).
		private Dictionary<string, object> ChatSendAll(string text, string[] to)
		{
			return ChatSendAll(text, to, null);
		}

		// twitchReply: id del mensaje de Twitch al que se responde (botón "Responder" del chat).
		private Dictionary<string, object> ChatSendAll(string text, string[] to, string twitchReply)
		{
			string twCh = "";
			string kcCh = "";
			Invoke((Action)delegate
			{
				twCh = ChatHub.Clean(fldChatTw.Box.Text);
				kcCh = ChatHub.Clean(fldChatKick.Box.Text);
			});
			Dictionary<string, object> result = new Dictionary<string, object>();
			List<Thread> workers = new List<Thread>();
			foreach (string target in to)
			{
				string pid = target;
				Action send = ((pid == "twitch") ? ((Action)delegate
				{
					twitch.SendChat(twCh, text, twitchReply);
				}) : ((pid == "kick") ? ((Action)delegate
				{
					kick.SendChat(kcCh, text);
				}) : ((pid == "youtube") ? ((Action)delegate
				{
					youtube.SendChat(text);
				}) : null)));
				Platform plat = ((pid == "twitch") ? twitch : ((pid == "kick") ? ((Platform)kick) : ((Platform)((pid == "youtube") ? youtube : null))));
				if (send == null)
				{
					continue;
				}
				Thread worker = new Thread((ThreadStart)delegate
				{
					string value;
					if (!plat.Connected)
					{
						value = "no está conectado (Directo → Título y categoría)";
					}
					else
					{
						try
						{
							send();
							value = "ok";
						}
						catch (ApiException ex)
						{
							value = ex.Message;
						}
						catch (Exception ex2)
						{
							value = ex2.Message;
						}
					}
					lock (result)
					{
						result[pid] = value;
					}
				})
				{
					IsBackground = true
				};
				workers.Add(worker);
				worker.Start();
			}
			foreach (Thread worker in workers)
			{
				worker.Join(20000);
			}
			lock (result)
			{
				foreach (string key in to)
				{
					if (!result.ContainsKey(key))
					{
						result[key] = "sin respuesta";
					}
				}
				List<string> failures = new List<string>();
				foreach (KeyValuePair<string, object> item in result)
				{
					if ((string)item.Value != "ok")
					{
						failures.Add(item.Key + ": " + item.Value);
					}
				}
				LogLater("Mensaje enviado al chat (" + string.Join(", ", to) + ")" + ((failures.Count > 0) ? (". Fallos: " + string.Join("; ", failures.ToArray())) : "."));
				return new Dictionary<string, object>(result);
			}
		}

		// ---------- Encender y apagar el chat ----------

		private void StartChat(bool userAction)
		{
			// El chat conjunto es del plan Completo.
			if (plan != Plan.Full)
			{
				loading = true;
				tgChat.Checked = false;
				loading = false;
				AppendLog("Chat conjunto: es del plan Completo (" + License.PriceFull + "). Botón del plan, arriba.");
				return;
			}
			string tw = fldChatTw.Box.Text.Trim();
			string kc = fldChatKick.Box.Text.Trim();
			string yt = fldChatYt.Box.Text.Trim();
			string tk = fldChatTk.Box.Text.Trim();
			if (tw.Length + kc.Length + yt.Length + tk.Length == 0)
			{
				loading = true;
				tgChat.Checked = false;
				loading = false;
				if (userAction)
				{
					Toast("Escribe al menos un canal (Twitch, Kick, YouTube o TikTok) para activar el chat.", "warn");
				}
				return;
			}
			if (!EnsureChatServer())
			{
				loading = true;
				tgChat.Checked = false;
				loading = false;
				return;
			}
			if (chatHub != null)
			{
				chatHub.Stop();
			}
			chatHub = new ChatHub
			{
				TwitchChannel = tw,
				KickChannel = kc,
				YouTubeChannel = yt,
				TikTokChannel = tk,
				TikTokKey = tkKey,
				TwitchApi = twitch,
				StreamElementsToken = seToken,
				StreamlabsToken = slToken
			};
			chatHub.Log = delegate(string m)
			{
				LogLater(m);
			};
			chatStartedAt = DateTime.UtcNow;
			// Cada mensaje va a la página del chat y a los comandos (!delay…).
			chatHub.Item = delegate(ChatItem it)
			{
				// Moderación automática: lo filtrado no llega ni a la página ni a los comandos.
				if (!AutoModAllows(it))
				{
					return;
				}
				chatServer.Send(it);
				OnChatItem(it);
				WidgetsOnItem(it);
				SummaryOnItem(it);
				ActivityAdd(it);
			};
			chatHub.Start();
			if (tk.Length > 0 && tkKey.Length == 0)
			{
				AppendLog("Chat de TikTok: falta la clave de Euler Stream (botón «Clave Euler Stream» en la pestaña Chat).");
			}
			// Con el chat encendido no se pueden cambiar los canales.
			fldChatTk.Locked = true;
			fldChatYt.Locked = true;
			fldChatKick.Locked = true;
			fldChatTw.Locked = true;
			AppendLog("Chat conjunto activado. Página del chat: " + chatServer.Url);
		}

		private void StopChat()
		{
			if (chatHub != null)
			{
				chatHub.Stop();
			}
			chatHub = null;
			fldChatTk.Locked = false;
			fldChatYt.Locked = false;
			fldChatKick.Locked = false;
			fldChatTw.Locked = false;
			AppendLog("Chat conjunto desactivado.");
		}

		// Recuadro de espectadores: una columna por plataforma con su número y el estado del chat, y el total.
		private void PaintChatViewers(Graphics g, Rectangle r)
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
			ChatHub hub = chatHub;
			string[] names = new string[4] { "TWITCH", "KICK", "YOUTUBE", "TIKTOK" };
			string[] channels = new string[4]
			{
				fldChatTw.Box.Text.Trim(),
				fldChatKick.Box.Text.Trim(),
				fldChatYt.Box.Text.Trim(),
				fldChatTk.Box.Text.Trim()
			};
			// -1 = aún no se sabe, -2 = no está en directo.
			int[] viewers = new int[4]
			{
				(hub != null) ? hub.TwitchViewers : (-1),
				(hub != null) ? hub.KickViewers : (-1),
				(hub != null) ? hub.YouTubeViewers : (-1),
				(hub != null) ? hub.TikTokViewers : (-1)
			};
			string[] states = new string[4]
			{
				(hub != null) ? hub.TwitchState : "",
				(hub != null) ? hub.KickState : "",
				(hub != null) ? hub.YouTubeState : "",
				(hub != null) ? hub.TikTokState : ""
			};
			float colWidth = (float)r.Width / 4f;
			float top = S(26f);
			CultureInfo es = CultureInfo.GetCultureInfo("es-ES");
			int total = 0;
			bool anyViewers = false;
			Theme.DrawText(g, "ESPECTADORES", Theme.Mono(7f), Theme.Dim, new RectangleF(S(16f), S(8f), S(200f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			for (int i = 0; i < 4; i++)
			{
				float x = (float)i * colWidth;
				string count;
				string status;
				Color color;
				if (hub == null || channels[i].Length == 0)
				{
					count = "—";
					status = ((channels[i].Length == 0) ? "No configurado" : "Chat apagado");
					color = Theme.Dim;
				}
				else
				{
					count = ((viewers[i] >= 0) ? viewers[i].ToString("N0", es) : ((viewers[i] == -2) ? "Sin directo" : "—"));
					if (viewers[i] >= 0)
					{
						total += viewers[i];
						anyViewers = true;
					}
					if (states[i] == "conectado")
					{
						status = "Chat conectado";
						color = Theme.Green;
					}
					else if (states[i].StartsWith("error"))
					{
						// "error: …" → se enseña lo que va después.
						status = CapFirst(states[i].Substring(Math.Min(7, states[i].Length)));
						color = Theme.Red;
					}
					else
					{
						status = CapFirst((states[i].Length > 0) ? states[i] : "conectando") + "…";
						color = Theme.Yellow;
					}
				}
				using (SolidBrush brush2 = new SolidBrush(color))
				{
					g.FillEllipse(brush2, x + (float)S(16f), top + (float)S(9f), S(7f), S(7f));
				}
				Theme.DrawText(g, names[i], Theme.Mono(7f), Theme.Muted, new RectangleF(x + (float)S(29f), top + (float)S(3f), colWidth - (float)S(34f), S(18f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, count, Theme.BodyBold((count.Length > 7) ? 9.5f : 11.5f), Theme.Text, new RectangleF(x + (float)S(15f), top + (float)S(22f), colWidth - (float)S(20f), S(26f)), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, status, Theme.Body(7.25f), (color == Theme.Red) ? Color.FromArgb(255, 120, 130) : Theme.Muted, new RectangleF(x + (float)S(15f), top + (float)S(50f), colWidth - (float)S(20f), S(18f)), StringAlignment.Near, StringAlignment.Center);
				if (i > 0)
				{
					using (Pen pen2 = new Pen(Theme.Border))
					{
						g.DrawLine(pen2, x, top + (float)S(6f), x, r.Height - S(12f));
					}
				}
			}
			Theme.DrawText(g, anyViewers ? ("TOTAL " + total.ToString("N0", es)) : "", Theme.Mono(7f), Theme.Violet, new RectangleF(0f, S(8f), r.Width - S(16f), S(16f)), StringAlignment.Far, StringAlignment.Center);
		}

		// "conectando" → "Conectando".
		private static string CapFirst(string s)
		{
			s = (s ?? "").Trim();
			if (s.Length != 0)
			{
				return char.ToUpperInvariant(s[0]) + s.Substring(1);
			}
			return s;
		}

		// ---------- Follows y donaciones ----------

		// Texto del botón: "Follows y donaciones · 2 activos · revisar".
		private string AlertsSummary()
		{
			ChatHub hub = chatHub;
			string[] states = ((hub == null) ? new string[0] : new string[3] { hub.FollowsState, hub.StreamElementsState, hub.StreamlabsState });
			int active = 0;
			bool problem = false;
			foreach (string state in states)
			{
				if (state == "activo" || state == "conectado")
				{
					active++;
				}
				else if (state.StartsWith("error") || state.StartsWith("falta") || state.StartsWith("token") || state.StartsWith("solo") || state.StartsWith("sesión") || state.StartsWith("Twitch"))
				{
					problem = true;
				}
			}
			if (hub == null)
			{
				// Chat apagado: cuenta lo que está configurado.
				int configured = ((seToken.Length > 0) ? 1 : 0) + ((slToken.Length > 0) ? 1 : 0) + (twitch.Connected ? 1 : 0);
				if (configured != 0)
				{
					return "Follows y donaciones · " + configured + " configurado" + ((configured == 1) ? "" : "s");
				}
				return "Follows y donaciones · sin configurar";
			}
			return "Follows y donaciones · " + active + " activo" + ((active == 1) ? "" : "s") + (problem ? " · revisar" : "");
		}

		// Ventana "Follows y donaciones": follows de Twitch y tokens de StreamElements y Streamlabs.
		private void AlertsDialog()
		{
			Form f = DarkForm("Follows y donaciones", 460, 560);
			Card twitchCard = new Card
			{
				Title = "Follows de Twitch",
				Subtitle = "Con tu cuenta de Twitch de la Directo → Título y categoría."
			};
			Place(f, twitchCard, 20f, 18f, 420f, 176f);
			PaintBox info = new PaintBox
			{
				Painter = delegate(Graphics g, Rectangle r)
				{
					ChatHub hub = chatHub;
					string s = ((!twitch.Connected) ? "Twitch no está conectado. Conéctalo para ver los follows en el chat (tienes que ser el dueño del canal o moderador)." : ((hub == null || hub.FollowsState.Length <= 0) ? ("Cuenta: " + twitch.Account + ". Se activa al encender el chat (follows y canjes de puntos). Si conectaste Twitch antes de la 4.13, vuelve a conectarlo una vez para dar los permisos.") : ("Cuenta: " + twitch.Account + ". Estado: " + hub.FollowsState + ".")));
					Theme.DrawWrapped(g, s, Theme.Body(8.5f), Theme.Text, r);
				}
			};
			Place(twitchCard, info, 20f, 60f, 380f, 60f);
			VButton conn = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = (twitch.Connected ? "Volver a conectar Twitch" : "Conectar Twitch"),
				FontSize = 9f
			};
			Place(twitchCard, conn, 20f, 126f, 220f, 36f);
			conn.Click += delegate
			{
				if (twitch.Configured || SetupDialog(twitch))
				{
					SaveConfig();
					LoginDialog(twitch);
					SaveConfig();
					RefreshTitleUi();
					conn.Text = (twitch.Connected ? "Volver a conectar Twitch" : "Conectar Twitch");
					info.Invalidate();
					// Si el chat está encendido, se reinicia para usar la nueva conexión.
					if (chatHub != null && twitch.Connected)
					{
						StartChat(false);
					}
				}
			};
			Card donationsCard = new Card
			{
				Title = "Donaciones",
				Subtitle = "Pega el token del servicio que uses (o los dos)."
			};
			Place(f, donationsCard, 20f, 206f, 420f, 260f);
			Label(donationsCard, "STREAMELEMENTS · JWT TOKEN", 20f, 60f, 260f);
			VButton seHelp = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "¿Dónde está?",
				FontSize = 8.25f
			};
			Place(donationsCard, seHelp, 280f, 58f, 120f, 22f);
			seHelp.Click += delegate
			{
				Http.OpenBrowser("https://streamelements.com/dashboard/account/channels");
			};
			Field fSe = new Field();
			Place(donationsCard, fSe, 20f, 80f, 380f, 36f);
			fSe.Box.UseSystemPasswordChar = true;
			fSe.Box.Text = seToken;
			Label(donationsCard, "STREAMLABS · SOCKET API TOKEN", 20f, 126f, 260f);
			VButton slHelp = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "¿Dónde está?",
				FontSize = 8.25f
			};
			Place(donationsCard, slHelp, 280f, 124f, 120f, 22f);
			slHelp.Click += delegate
			{
				Http.OpenBrowser("https://streamlabs.com/dashboard#/settings/api-settings");
			};
			Field fSl = new Field();
			Place(donationsCard, fSl, 20f, 146f, 380f, 36f);
			fSl.Box.UseSystemPasswordChar = true;
			fSl.Box.Text = slToken;
			Box(donationsCard, 20f, 190f, 380f, 60f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "StreamElements: Cuenta → Canales → \"Mostrar secretos\" → JWT Token. Streamlabs: Ajustes → API Settings → API Tokens → \"Your Socket API Token\". Se guardan cifrados en VortexsLive.ini.", Theme.Body(7.75f), Theme.Muted, r);
			});
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
			Place(f, cancel, 20f, 482f, 130f, 42f);
			Place(f, save, 160f, 482f, 280f, 42f);
			cancel.Click += delegate
			{
				f.Close();
			};
			save.Click += delegate
			{
				seToken = fSe.Box.Text.Trim();
				slToken = fSl.Box.Text.Trim();
				SaveConfig();
				f.Close();
				if (chatHub != null)
				{
					StartChat(false);
				}
			};
			f.ShowDialog(this);
		}

		// ---------- TikTok: clave de Euler Stream ----------

		private VButton tkKeyButton;

		private void TikTokKeyDialog()
		{
			Form f = DarkForm("Chat de TikTok", 460, 452);
			Card c = new Card
			{
				Title = "Chat de TikTok",
				Subtitle = "Con Euler Stream, un servicio gratuito."
			};
			Place(f, c, 20f, 18f, 420f, 360f);
			Box(c, 20f, 56f, 380f, 112f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "TikTok no deja que otros programas lean el chat de un directo. Euler Stream se conecta a tu directo y se lo pasa a Vortexs Live. Necesitas una clave gratuita:\n1. Crea una cuenta en eulerstream.com (sin tarjeta).\n2. En el panel, copia tu API Key.\n3. Pégala aquí y escribe tu @usuario de TikTok en la pestaña Chat.", Theme.Body(8.25f), Theme.Text, r);
			});
			VButton web = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Abrir eulerstream.com",
				FontSize = 9f
			};
			Place(c, web, 20f, 176f, 380f, 34f);
			web.Click += delegate
			{
				Http.OpenBrowser("https://www.eulerstream.com");
			};
			Label(c, "EULER STREAM · API KEY", 20f, 220f, 300f);
			Field fKey = new Field();
			Place(c, fKey, 20f, 238f, 380f, 36f);
			fKey.Box.UseSystemPasswordChar = true;
			fKey.Box.Text = tkKey;
			Box(c, 20f, 282f, 380f, 70f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Llegan los mensajes, regalos, suscripciones, follows y espectadores. TikTok no deja escribir ni moderar desde otros programas. El plan gratis de Euler Stream sobra para tu directo. La clave se guarda cifrada en VortexsLive.ini.", Theme.Body(7.75f), Theme.Muted, r);
			});
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
			Place(f, cancel, 20f, 392f, 130f, 42f);
			Place(f, save, 160f, 392f, 280f, 42f);
			cancel.Click += delegate
			{
				f.Close();
			};
			save.Click += delegate
			{
				tkKey = fKey.Box.Text.Trim();
				SaveConfig();
				f.Close();
				// Si el chat está encendido con TikTok, se reinicia para usar la clave nueva.
				if (chatHub != null && fldChatTk.Box.Text.Trim().Length > 0)
				{
					StartChat(false);
				}
			};
			f.ShowDialog(this);
		}

		// ---------- Configuración (VortexsLive.ini) ----------

		// Además del chat, carga lo de los otros archivos (avisos, comandos, Mando, actualizaciones).
		private void LoadChatConfig(Dictionary<string, string> raw)
		{
			string value;
			Func<string, string> get = (string k) => (!raw.TryGetValue(k, out value)) ? "" : value;
			fldChatTw.Box.Text = get("chat_tw");
			fldChatKick.Box.Text = get("chat_kick");
			fldChatYt.Box.Text = get("chat_yt");
			fldChatTk.Box.Text = get("chat_tk");
			tkKey = Secret.Unprotect(get("chat_tk_key"));
			if (tkKeyButton != null)
			{
				tkKeyButton.Text = (tkKey.Length > 0) ? "Clave puesta ✓" : "Clave Euler Stream";
			}
			// Primera vez: los canales salen de las cuentas conectadas en Directo → Título y categoría.
			if (!raw.ContainsKey("chat_tw") && twitch.Account.Length > 0)
			{
				fldChatTw.Box.Text = twitch.Account.ToLowerInvariant();
			}
			if (!raw.ContainsKey("chat_kick") && kick.Account.Length > 0)
			{
				fldChatKick.Box.Text = kick.Account;
			}
			seToken = Secret.Unprotect(get("chat_se"));
			slToken = Secret.Unprotect(get("chat_sl"));
			chatAutoStart = get("chat_on") == "1";
			tgChat.Checked = chatAutoStart;
			LoadExtrasConfig(raw);
			LoadWidgetsConfig(raw);
			LoadCmdConfig(raw);
			LoadAutoModConfig(raw);
			LoadChatStyleConfig(raw);
			LoadRemoteConfig(raw);
			LoadUpdateConfig(raw);
		}

		private void SaveChatConfig(List<string> lines)
		{
			if (fldChatTw != null)
			{
				lines.Add("chat_on=" + (tgChat.Checked ? "1" : "0"));
				lines.Add("chat_tw=" + fldChatTw.Box.Text.Trim());
				lines.Add("chat_kick=" + fldChatKick.Box.Text.Trim());
				lines.Add("chat_yt=" + fldChatYt.Box.Text.Trim());
				lines.Add("chat_tk=" + fldChatTk.Box.Text.Trim());
				lines.Add("chat_tk_key=" + Secret.Protect(tkKey));
				lines.Add("chat_se=" + Secret.Protect(seToken));
				lines.Add("chat_sl=" + Secret.Protect(slToken));
				SaveExtrasConfig(lines);
				SaveWidgetsConfig(lines);
				SaveCmdConfig(lines);
				SaveAutoModConfig(lines);
				SaveChatStyleConfig(lines);
				SaveRemoteConfig(lines);
				SaveUpdateConfig(lines);
			}
		}
	}
}
