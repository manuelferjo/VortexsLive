// Avisos automáticos en el chat al activar/desactivar el delay y overlay de espectadores para OBS.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// ---------- Avisos automáticos en el chat ----------

		private bool annOn = true;
		private bool annLiveOnly = true;
		private readonly bool[] annTo = new bool[3] { true, true, false };  // Twitch, Kick, YouTube
		private string annOnMsg = "⏱️ Delay activado: el directo va con {s} s de retraso.";
		private string annOffMsg = "🔴 Delay desactivado: ahora veis el directo en tiempo real.";
		private System.Windows.Forms.Timer annTimer;
		private int annLast = -1;  // último estado anunciado: 1 activado, 0 desactivado, -1 nada

		// Se llama al activar/desactivar el delay. Espera 3 s por si lo vuelves a cambiar enseguida
		// (así no se llena el chat) y manda solo el estado final.
		private void AnnounceDelaySoon()
		{
			if (!annOn)
			{
				return;
			}
			if (annTimer == null)
			{
				annTimer = new System.Windows.Forms.Timer();
				annTimer.Interval = 3000;
				annTimer.Tick += delegate
				{
					annTimer.Stop();
					AnnounceNow(false);
				};
			}
			annTimer.Stop();
			annTimer.Start();
		}

		private void AnnounceNow(bool test)
		{
			bool on = engine.Enabled && engine.TargetMs > 0;
			int state = on ? 1 : 0;
			if (!test && state == annLast)
			{
				return;
			}
			if (!test && annLiveOnly && !engine.OutputActive)
			{
				AppendLog("Aviso de delay en el chat: no se manda porque no estás en directo.");
				return;
			}
			string text = I18n.Tr(on ? annOnMsg : annOffMsg).Replace("{s}", (engine.TargetMs / 1000).ToString());
			List<string> to = new List<string>();
			string[] ids = new string[3] { "twitch", "kick", "youtube" };
			Platform[] plats = new Platform[3] { twitch, kick, youtube };
			for (int i = 0; i < 3; i++)
			{
				if (annTo[i])
				{
					if (plats[i].Connected)
					{
						to.Add(ids[i]);
					}
					else
					{
						AppendLog("Aviso de delay: " + plats[i].Name + " no está conectado en Directo → Título y categoría.");
					}
				}
			}
			if (to.Count == 0 || text.Trim().Length == 0)
			{
				return;
			}
			if (!test)
			{
				annLast = state;
			}
			string[] targets = to.ToArray();
			ThreadPool.QueueUserWorkItem(delegate
			{
				Dictionary<string, object> res;
				try
				{
					res = ChatSendAll(text, targets);
				}
				catch (Exception ex)
				{
					LogLater("Aviso de delay en el chat: no se pudo enviar (" + ex.Message + ").");
					return;
				}
				List<string> parts = new List<string>();
				foreach (string t in targets)
				{
					object v;
					parts.Add(t + ": " + ((res.TryGetValue(t, out v) && (v as string) == "ok") ? "enviado" : ("error (" + v + ")")));
				}
				LogLater("Aviso de delay en el chat (" + (on ? "activado" : "desactivado") + ") → " + string.Join(" · ", parts.ToArray()));
			});
		}

		// ---------- Overlay de espectadores ----------

		private readonly bool[] vwShow = new bool[5] { true, true, true, true, true };  // Twitch, Kick, YouTube, Total, TikTok

		private string ViewersStateJson()
		{
			ChatHub h = chatHub;
			int tw = (h != null && h.Running) ? h.TwitchViewers : -1;
			int kc = (h != null && h.Running) ? h.KickViewers : -1;
			int yt = (h != null && h.Running) ? h.YouTubeViewers : -1;
			int tk = (h != null && h.Running) ? h.TikTokViewers : -1;
			return "{\"chat\":" + ((h != null && h.Running) ? "true" : "false") +
				",\"show\":{\"twitch\":" + B(vwShow[0]) + ",\"kick\":" + B(vwShow[1]) + ",\"youtube\":" + B(vwShow[2]) + ",\"tiktok\":" + B(vwShow[4]) + ",\"total\":" + B(vwShow[3]) + "}" +
				",\"v\":{\"twitch\":" + tw + ",\"kick\":" + kc + ",\"youtube\":" + yt + ",\"tiktok\":" + tk + "}" +
				",\"pos\":\"" + vwPos + "\",\"vertical\":" + B(vwVertical) +
				",\"k\":" + vwScale.ToString(System.Globalization.CultureInfo.InvariantCulture) +
				",\"dk\":" + ovDelayScale.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
		}

		private static string B(bool b)
		{
			return b ? "true" : "false";
		}

		// ---------- Pestaña Overlays ----------

		// Estilo de los tres overlays (colores, forma, fuente…): ventana "Personalizar".
		private OverlayStyle ovStyle = new OverlayStyle();
		private bool ovDelayHideOff;
		private float ovDelayScale = 1f;
		private string vwPos = "tr";
		private bool vwVertical;
		private float vwScale = 1f;

		// Qué overlay se está viendo en la pestaña (selector de arriba) y su recuadro.
		private int ovPick;
		private Card[] ovCards = new Card[0];
		private VButton[] ovPickBtns = new VButton[0];

		// Solo se ve el recuadro del overlay elegido (ShowPage enseña todos los de la pestaña).
		private void ApplyOverlayPick()
		{
			for (int i = 0; i < ovCards.Length; i++)
			{
				if (ovCards[i] != null)
				{
					ovCards[i].Visible = i == ovPick;
				}
			}
			for (int i = 0; i < ovPickBtns.Length; i++)
			{
				ovPickBtns[i].Selected = i == ovPick;
				ovPickBtns[i].Invalidate();
			}
			if (ovHint != null) ovHint.Invalidate();
			if (side != null && side.Visible) side.Invalidate();
		}

		private string OverlayUrl(string path)
		{
			return EnsureChatServer() ? ("http://127.0.0.1:" + chatServer.Port + "/" + path) : null;
		}

		// Grupo de píldoras de las que solo una está elegida.
		private VButton[] Choice(Control parent, string[] labels, int selected, float x, float y, float w, Action<int> pick)
		{
			VButton[] b = new VButton[labels.Length];
			for (int i = 0; i < labels.Length; i++)
			{
				int idx = i;
				b[i] = new VButton { Kind = ButtonKind.Pill, Text = labels[i], Selected = i == selected, FontSize = 7.5f };
				Place(parent, b[i], x + (float)i * (w + 6f), y, w, 26f);
				b[i].Click += delegate
				{
					for (int j = 0; j < b.Length; j++)
					{
						b[j].Selected = j == idx;
						b[j].Invalidate();
					}
					pick(idx);
					SaveConfig();
				};
			}
			return b;
		}

		// Enlace "Personalizar →" arriba a la derecha de cada recuadro: abre la ventana del estilo.
		private void StyleLink(Card card)
		{
			VButton link = new VButton { Kind = ButtonKind.Link, Text = "Personalizar →", FontSize = 8.5f };
			Place(card, link, 230f, 13f, 170f, 24f);
			link.Click += delegate { OverlayStyleDialog(); };
		}

		private void UrlButtons(Card card, string path, string what, float y)
		{
			VButton copy = new VButton { Kind = ButtonKind.Primary, Text = "Copiar URL", FontSize = 9f, Icon = "copy" };
			Place(card, copy, 20f, y, 186f, 34f);
			copy.Click += delegate
			{
				string u = OverlayUrl(path);
				if (u != null)
				{
					CopyText(u, copy, "Copiar URL");
					AppendLog("Overlay de " + what + ": " + u + " (en OBS: fuente Navegador).");
				}
			};
			VButton see = new VButton { Kind = ButtonKind.Ghost, Text = "Vista previa", FontSize = 9f, Icon = "external" };
			Place(card, see, 214f, y, 186f, 34f);
			see.Click += delegate
			{
				string u = OverlayUrl(path);
				if (u != null)
				{
					Http.OpenBrowser(u);
				}
			};
		}

		private void BuildOverlays(int pageTop)
		{
			float[] scales = new float[3] { 1f, 1.3f, 1.6f };

			// Selector: los ocho overlays en dos filas; debajo, el recuadro del elegido.
			string[] picks = new string[8] { "Delay", "Espectadores", "Chat", "Alertas", "Destacado", "Objetivo", "Eventos", "Encuesta" };
			ovPickBtns = new VButton[8];
			for (int i = 0; i < 8; i++)
			{
				int idx = i;
				ovPickBtns[i] = new VButton { Kind = ButtonKind.Pill, Text = picks[i], Selected = i == ovPick, FontSize = 8f };
				Place(this, ovPickBtns[i], 20f + (float)(i % 4) * 106.5f, (float)pageTop + (float)(i / 4) * 32f, 100.5f, 28f, 5);
				ovPickBtns[i].Click += delegate
				{
					ovPick = idx;
					ApplyOverlayPick();
					SaveConfig();
				};
			}
			int y0 = pageTop + 70;

			// --- Delay ---
			Card c1 = new Card();
			c1.Title = "Delay";
			c1.Subtitle = "Arriba a la derecha: «DELAY 30 s» o «EN DIRECTO · Sin delay».";
			Place(this, c1, 20f, y0, 420f, 152f, 5);
			Label(c1, "TAMAÑO", 20f, 54f, 120f);
			int ks = Array.IndexOf(scales, ovDelayScale);
			Choice(c1, new string[3] { "Normal", "Grande", "Enorme" }, (ks < 0) ? 0 : ks, 20f, 72f, 78f, delegate(int i) { ovDelayScale = scales[i]; });
			Toggle tHide = new Toggle { Text = "Ocultar sin delay", Checked = ovDelayHideOff };
			Place(c1, tHide, 270f, 70f, 140f, 30f);
			tHide.CheckedChanged += delegate { ovDelayHideOff = tHide.Checked; SaveConfig(); };
			UrlButtons(c1, "delay", "delay", 108f);
			StyleLink(c1);

			// --- Espectadores ---
			int y2 = y0;
			Card c2 = new Card();
			c2.Title = "Espectadores";
			c2.Subtitle = "De cada plataforma y el total. Necesita el chat activado.";
			Place(this, c2, 20f, y2, 420f, 236f, 5);
			Label(c2, "QUÉ MOSTRAR (NINGUNO = NO SE VE NADA)", 20f, 54f, 380f);
			// Orden en pantalla; el número es la posición en vwShow (TikTok se añadió al final).
			string[] vnames = new string[5] { "Twitch", "Kick", "YouTube", "TikTok", "Total" };
			int[] vidx = new int[5] { 0, 1, 2, 4, 3 };
			for (int i = 0; i < 5; i++)
			{
				int idx = vidx[i];
				VButton p = new VButton { Kind = ButtonKind.Pill, Text = vnames[i], Selected = vwShow[idx], FontSize = 7.5f };
				Place(c2, p, 20f + (float)i * 77f, 72f, 72f, 26f);
				p.Click += delegate
				{
					vwShow[idx] = !vwShow[idx];
					p.Selected = vwShow[idx];
					p.Invalidate();
					SaveConfig();
				};
			}
			Label(c2, "POSICIÓN", 20f, 102f, 120f);
			string[] posIds = new string[4] { "tr", "tl", "br", "bl" };
			Choice(c2, new string[4] { "Arriba dcha.", "Arriba izq.", "Abajo dcha.", "Abajo izq." }, Math.Max(0, Array.IndexOf(posIds, vwPos)), 20f, 120f, 90f, delegate(int i) { vwPos = posIds[i]; });
			Toggle tVert = new Toggle { Text = "En columna", Checked = vwVertical };
			Place(c2, tVert, 20f, 154f, 130f, 30f);
			tVert.CheckedChanged += delegate { vwVertical = tVert.Checked; SaveConfig(); };
			Choice(c2, new string[2] { "Normal", "Grande" }, (vwScale > 1.1f) ? 1 : 0, 212f, 156f, 90f, delegate(int i) { vwScale = (i == 1) ? 1.3f : 1f; });
			UrlButtons(c2, "viewers", "espectadores", 192f);
			StyleLink(c2);

			// --- Chat en pantalla ---
			int y3 = y0;
			Card c3 = new Card();
			c3.Title = "Chat en pantalla";
			c3.Subtitle = "Twitch, Kick, YouTube y TikTok juntos, con su propio estilo.";
			Place(this, c3, 20f, y3, 420f, 100f, 5);
			UrlButtons(c3, "?overlay=1", "chat", 56f);
			// El chat tiene su propio estilo (fuente, tamaño, colores, fondo…).
			VButton chatLink = new VButton { Kind = ButtonKind.Link, Text = "Personalizar chat →", FontSize = 8.5f };
			Place(c3, chatLink, 230f, 13f, 170f, 24f);
			chatLink.Click += delegate { ChatStyleDialog(); };

			Card[] more = BuildWidgetCards(y0);
			ovCards = new Card[8] { c1, c2, c3, more[0], more[1], more[2], more[3], more[4] };

			ovHint = Box(this, 22f, 628f, 416f, 18f, delegate(Graphics g, Rectangle r)
			{
				// Sin el chat conjunto, los overlays (menos el de delay) no reciben nada: se avisa.
				if (ovPick != 0 && !ChatOn)
				{
					float isz = S(13f);
					Icons.Draw(g, "warning", new RectangleF(r.X, r.Y + (r.Height - isz) / 2f, isz, isz), Theme.Yellow);
					Theme.DrawText(g, "Chat conjunto apagado: este overlay no recibe nada. Pulsa para activarlo.", Theme.BodySemi(7.25f), Theme.Yellow, new RectangleF(r.X + isz + S(5f), r.Y, r.Width - isz - S(5f), r.Height), StringAlignment.Near, StringAlignment.Center);
					return;
				}
				Theme.DrawText(g, "En OBS: + → Navegador → pega la URL · 1920x1080 (chat: 500x700). Vista previa: pestaña › del borde.", Theme.Body(7.25f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			}, 5);
			ovHint.Click += delegate
			{
				if (ovPick != 0 && !ChatOn)
				{
					TurnChatOn();
					ovHint.Invalidate();
				}
			};
			ApplyOverlayPick();
		}

		// ---------- Ventana "Avisos de delay en el chat" ----------

		private void AnnounceDialog()
		{
			Form f = DarkForm("Avisos y comandos del chat", 460, 650);

			Card c1 = new Card();
			c1.Title = "Avisar en el chat del delay";
			c1.Subtitle = "Escribe en tus chats al activar o desactivar el delay.";
			Place(f, c1, 20f, 18f, 420f, 318f);
			Toggle tOn = new Toggle { Text = "Mandar el aviso automáticamente", Checked = annOn };
			Place(c1, tOn, 18f, 58f, 384f, 28f);
			Toggle tLive = new Toggle { Text = "Solo si estoy en directo", Checked = annLiveOnly };
			Place(c1, tLive, 18f, 88f, 384f, 28f);
			Label(c1, "EN QUÉ CHATS (CUENTAS DE LA PESTAÑA TÍTULO)", 20f, 122f, 380f);
			string[] names = new string[3] { "Twitch", "Kick", "YouTube" };
			Platform[] plats = new Platform[3] { twitch, kick, youtube };
			VButton[] pills = new VButton[3];
			for (int i = 0; i < 3; i++)
			{
				int idx = i;
				pills[i] = new VButton { Kind = ButtonKind.Pill, Text = names[i] + (plats[i].Connected ? "" : " (sin conectar)"), Selected = annTo[i], FontSize = 7.5f };
				Place(c1, pills[i], 20f + (float)i * 128f, 140f, 124f, 28f);
				pills[i].Click += delegate
				{
					annTo[idx] = !annTo[idx];
					pills[idx].Selected = annTo[idx];
					pills[idx].Invalidate();
				};
			}
			Label(c1, "AL ACTIVAR ({s} = SEGUNDOS)", 20f, 176f, 380f);
			Field fOn = new Field();
			Place(c1, fOn, 20f, 194f, 380f, 34f);
			fOn.Box.Text = annOnMsg;
			Label(c1, "AL DESACTIVAR", 20f, 234f, 380f);
			Field fOff = new Field();
			Place(c1, fOff, 20f, 252f, 380f, 34f);
			fOff.Box.Text = annOffMsg;
			Box(c1, 20f, 292f, 380f, 18f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, "Se espera 3 s por si lo cambias otra vez, y se manda solo el estado final.", Theme.Body(7.5f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});

			Card c2 = new Card();
			c2.Title = "Comandos del chat";
			c2.Subtitle = "Respuestas automáticas, una por línea: !comando = respuesta";
			Place(f, c2, 20f, 346f, 420f, 230f);
			Toggle tCmd = new Toggle { Text = "Contestar a los comandos", Checked = cmdOn };
			Place(c2, tCmd, 18f, 56f, 384f, 28f);
			// Los comandos van con el chat conjunto: plan Completo. Los avisos del delay, también el Básico.
			if (plan != Plan.Full)
			{
				c2.Subtitle = "Comandos del chat: plan Completo";
				tCmd.Enabled = false;
			}
			Field fCmd = new Field();
			fCmd.Box.Multiline = true;
			fCmd.Box.AcceptsReturn = true;
			fCmd.Box.ScrollBars = ScrollBars.Vertical;
			fCmd.Box.Text = cmdText;
			Place(c2, fCmd, 20f, 90f, 380f, 98f);
			fCmd.Box.ReadOnly = plan != Plan.Full;
			Box(c2, 20f, 194f, 380f, 30f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "{delay} = si hay delay y cuánto · {s} segundos · {user} quién pregunta. Contesta en la plataforma donde se escribe (como mucho una vez cada 20 s). !guardar (tú y quien permitas en la pestaña Clips) guarda los últimos 60 s en tu PC. !clip no es de Vortexs Live: déjaselo a tu bot de clips de Twitch.", Theme.Body(7.25f), Theme.Muted, r);
			});

			VButton test = new VButton { Kind = ButtonKind.Ghost, Text = "Probar aviso ahora", FontSize = 9.5f };
			VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "Guardar", FontSize = 10f };
			Place(f, test, 20f, 590f, 190f, 44f);
			Place(f, ok, 218f, 590f, 222f, 44f);
			Action save = delegate
			{
				annOn = tOn.Checked;
				annLiveOnly = tLive.Checked;
				annOnMsg = fOn.Box.Text.Trim();
				annOffMsg = fOff.Box.Text.Trim();
				cmdOn = tCmd.Checked;
				cmdText = fCmd.Box.Text.Trim();
				SaveConfig();
			};
			test.Click += delegate
			{
				save();
				if (I18n.Show(f, "Se enviará ahora a los chats elegidos el mensaje del estado actual (" + ((engine.Enabled && engine.TargetMs > 0) ? "delay activado" : "delay desactivado") + "). ¿Enviar?", "Probar aviso", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					AnnounceNow(true);
				}
			};
			ok.Click += delegate
			{
				save();
				f.Close();
			};
			f.FormClosing += delegate
			{
				save();
			};
			f.ShowDialog(this);
		}

		// ---------- Configuración ----------

		private void LoadExtrasConfig(Dictionary<string, string> raw)
		{
			string v;
			if (raw.TryGetValue("ann_on", out v)) annOn = v == "1";
			if (raw.TryGetValue("ann_live", out v)) annLiveOnly = v != "0";
			if (raw.TryGetValue("ann_to", out v) && v.Length == 3) for (int i = 0; i < 3; i++) annTo[i] = v[i] == '1';
			if (raw.TryGetValue("ann_msg_on", out v) && v.Length > 0) annOnMsg = Unb64(v, annOnMsg);
			if (raw.TryGetValue("ann_msg_off", out v) && v.Length > 0) annOffMsg = Unb64(v, annOffMsg);
			// 4 letras: versiones sin TikTok (TikTok se queda visible); 5: con TikTok.
			if (raw.TryGetValue("vw_show", out v) && (v.Length == 4 || v.Length == 5)) for (int i = 0; i < v.Length; i++) vwShow[i] = v[i] == '1';
			if (raw.TryGetValue("ov_delay_hide", out v)) ovDelayHideOff = v == "1";
			if (raw.TryGetValue("ov_style", out v) && v.Length > 0) ovStyle = OverlayStyle.Load(v);
			float f;
			if (raw.TryGetValue("ov_delay_k", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) ovDelayScale = f;
			if (raw.TryGetValue("vw_pos", out v) && v.Length == 2) vwPos = v;
			if (raw.TryGetValue("vw_vert", out v)) vwVertical = v == "1";
			if (raw.TryGetValue("vw_k", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) vwScale = f;
		}

		private void SaveExtrasConfig(List<string> lines)
		{
			lines.Add("ann_on=" + (annOn ? "1" : "0"));
			lines.Add("ann_live=" + (annLiveOnly ? "1" : "0"));
			lines.Add("ann_to=" + (annTo[0] ? "1" : "0") + (annTo[1] ? "1" : "0") + (annTo[2] ? "1" : "0"));
			lines.Add("ann_msg_on=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(annOnMsg)));
			lines.Add("ann_msg_off=" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(annOffMsg)));
			lines.Add("vw_show=" + (vwShow[0] ? "1" : "0") + (vwShow[1] ? "1" : "0") + (vwShow[2] ? "1" : "0") + (vwShow[3] ? "1" : "0") + (vwShow[4] ? "1" : "0"));
			lines.Add("ov_delay_hide=" + (ovDelayHideOff ? "1" : "0"));
			lines.Add("ov_style=" + ovStyle.Save());
			lines.Add("ov_delay_k=" + ovDelayScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
			lines.Add("vw_pos=" + vwPos);
			lines.Add("vw_vert=" + (vwVertical ? "1" : "0"));
			lines.Add("vw_k=" + vwScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		private static string Unb64(string s, string def)
		{
			try
			{
				return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(s));
			}
			catch
			{
				return def;
			}
		}
	}
}
