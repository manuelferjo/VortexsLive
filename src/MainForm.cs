// Ventana principal de Vortexs Live.
//
// La clase MainForm está repartida en varios archivos (partial class), uno por zona:
//   MainForm.cs     esta parte: variables, arranque de la ventana, cabecera, utilidades de diseño,
//                   pestaña Registro, atajos de teclado, cierre y VortexsLive.ini
//   LiveTab.cs      pestaña Directo (delay, Iniciar/Detener, recuadros de estado)
//   ChatTab.cs      pestaña Chat (chat conjunto, follows, donaciones) y la página local
//   Extras.cs       pestaña Overlays, delays guardados y avisos automáticos en el chat
//   ChatCmd.cs      comandos del chat (!delay…) y moderación
//   Remote.cs       pestaña Mando (móvil, Stream Deck, panel de OBS)
//   TitleTab.cs     pestaña Título (título y categoría, conectar plataformas)
//   SettingsTab.cs  pestaña Ajustes (destinos, OBS y opciones)
//   Updater.cs      actualización automática
//   LicenseUi.cs    plan (prueba, Básico, Completo), ventana Licencia y pestañas de pago
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm : Form
	{
		// Colores oscuros para los menús desplegables (DarkMenu).
		private class DarkColors : ProfessionalColorTable
		{
			public override Color ToolStripDropDownBackground
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color MenuBorder
			{
				get
				{
					return Theme.BorderHi;
				}
			}

			public override Color MenuItemBorder
			{
				get
				{
					return Color.FromArgb(120, Theme.Violet);
				}
			}

			public override Color MenuItemSelected
			{
				get
				{
					return Color.FromArgb(52, 44, 74);
				}
			}

			public override Color ImageMarginGradientBegin
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color ImageMarginGradientMiddle
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color ImageMarginGradientEnd
			{
				get
				{
					return Theme.Elevated;
				}
			}

			public override Color SeparatorDark
			{
				get
				{
					return Theme.Border;
				}
			}

			public override Color SeparatorLight
			{
				get
				{
					return Theme.Border;
				}
			}

			public override Color CheckBackground
			{
				get
				{
					return Color.FromArgb(60, Theme.Violet);
				}
			}

			public override Color CheckSelectedBackground
			{
				get
				{
					return Color.FromArgb(90, Theme.Violet);
				}
			}

			public override Color CheckPressedBackground
			{
				get
				{
					return Color.FromArgb(90, Theme.Violet);
				}
			}
		}

		// ---------- Constantes ----------

		private const string Version = "4.12";

		private const string TikTokName = "TikTok";

		private const string KickServer = "rtmps://fa723fc1b171.global-contribute.live-video.net/app";

		// Primer puerto de la página local (chat, overlays, Mando). Si está ocupado, prueba los 9 siguientes.
		private const int ChatPort = 8935;

		// Delay máximo en segundos.
		private const int MaxDelay = 600;

		// Páginas de la ventana (cada control se apunta a una con Place/Box). Las pestañas las
		// muestran en otro orden: ver TabPages.
		private const int PageLive = 0;

		private const int PageTitle = 1;

		private const int PageSettings = 2;

		private const int PageLog = 3;

		private const int PageChat = 4;

		private const int PageOverlays = 5;

		private const int PageMando = 6;

		// (La 7 es PageLocked, en LicenseUi.cs.)
		private const int PageClips = 8;

		// Modificadores de RegisterHotKey.
		private const uint MOD_ALT = 1u;

		private const uint MOD_CTRL = 2u;

		private const uint MOD_SHIFT = 4u;

		// Atajos con Ctrl+Alt+Mayús para el resto de acciones del Mando (para la acción «Tecla de
		// acceso rápido» del Stream Deck, que no abre el navegador). Id del atajo 5…: acción, tecla.
		private static readonly string[][] DeckHotkeys = new string[9][]
		{
			new string[2] { "on", "A" },
			new string[2] { "off", "Q" },
			new string[2] { "preset&n=1", "D1" },
			new string[2] { "preset&n=2", "D2" },
			new string[2] { "preset&n=3", "D3" },
			new string[2] { "preset&n=4", "D4" },
			new string[2] { "preset&n=5", "D5" },
			new string[2] { "preset&n=6", "D6" },
			new string[2] { "cortar", "X" }
		};

		// Texto del atajo de una acción del Mando ("" si no tiene).
		private static string HotkeyFor(string action)
		{
			switch (action)
			{
			case "toggle":
				return "Ctrl+Alt+D";
			case "mas":
				return "Ctrl+Alt+↑";
			case "menos":
				return "Ctrl+Alt+↓";
			case "clip":
				return "Ctrl+Alt+C";
			}
			foreach (string[] h in DeckHotkeys)
			{
				if (h[0] == action)
				{
					return "Ctrl+Alt+Mayús+" + h[1].Replace("D", "");
				}
			}
			return "";
		}

		private const uint MOD_NOREPEAT = 16384u;

		// ---------- Pestaña Título ----------

		private readonly TwitchPlatform twitch = new TwitchPlatform();

		private readonly YouTubePlatform youtube = new YouTubePlatform();

		private readonly KickPlatform kick = new KickPlatform();

		private Platform[] plats;

		private Field fldTitle;

		private Field fldCat;

		private VButton btnSearch;

		private VButton btnUpdateTitle;

		private VButton btnRecent;

		private VButton[] chips = new VButton[4];

		private Toggle[] tgPlat = new Toggle[4];

		private VButton[] btnConn = new VButton[4];

		private PaintBox titleCounter;

		private readonly List<string> recentTitles = new List<string>();

		private bool ttEnabled = true;

		// La categoría de YouTube la eligió el usuario (no se propone sola).
		private bool ytManual;

		private bool busyTitle;

		private bool titleRead;

		// ---------- Destinos (pestaña Ajustes) ----------

		private readonly Destination dTwitch = new Destination("twitch", "Twitch", true)
		{
			Server = "rtmp://live.twitch.tv/app",
			Enabled = true
		};

		private readonly Destination dKick = new Destination("kick", "Kick", false)
		{
			Server = KickServer
		};

		private readonly Destination dYouTube = new Destination("youtube", "YouTube", false)
		{
			Server = "rtmp://a.rtmp.youtube.com/live2"
		};

		// TikTok da un servidor y una clave distintos a cada cuenta (y a veces en cada directo): no hay servidor por defecto.
		private readonly Destination dTikTok = new Destination("tiktok", "TikTok", false);

		private readonly Destination dCustom = new Destination("custom", "Otro", false);

		private Destination[] dests;

		private readonly Toggle[] tgDest = new Toggle[5];

		private readonly VButton[] btnEditDest = new VButton[5];

		private PaintBox destStrip;

		// Página que muestra cada pestaña: Directo, Chat, Overlays, Mando, Título, Ajustes, Registro.
		private static readonly int[] TabPages = new int[8] { PageLive, PageChat, PageOverlays, PageMando, PageClips, PageTitle, PageSettings, PageLog };

		// ---------- Pestaña Chat ----------

		private readonly ChatServer chatServer = new ChatServer();

		private ChatHub chatHub;

		private Toggle tgChat;

		private Field fldChatTw;

		private Field fldChatKick;

		private Field fldChatYt;

		private Field fldChatTk;

		private PaintBox chatViewers;

		private VButton btnChatOpen;

		private VButton btnChatDock;

		private VButton btnChatAlerts;

		private bool chatAutoStart;

		// Tokens de StreamElements y Streamlabs (en el .ini van cifrados).
		private string seToken = "";

		private string slToken = "";

		// Clave de Euler Stream para leer el chat de TikTok (cifrada en VortexsLive.ini).
		private string tkKey = "";

		// ---------- Ventana y pestaña Directo ----------

		// El motor: recibe de OBS, guarda el delay y envía a los destinos.
		private readonly Engine engine = new Engine();

		private readonly string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VortexsLive.ini");

		// Mensajes del registro que llegan desde otros hilos (los pasa a la ventana RefreshStatus).
		private readonly Queue<string> pendingLog = new Queue<string>();

		// Controles de cada página, para enseñar solo los de la pestaña elegida.
		// La 8.ª (PageLocked) es el aviso de las pestañas del plan Completo (LicenseUi.cs).
		private readonly List<Control>[] pages = new List<Control>[9]
		{
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>(),
			new List<Control>()
		};

		private Tabs tabs;

		private PaintBox header;

		private PaintBox heroBox;

		private PaintBox footer;

		private PaintBox[] tiles = new PaintBox[3];

		private Toggle tgDelay;

		private Toggle tgReplay;

		private Toggle tgTop;

		private Toggle tgLan;

		private Toggle tgFfmpeg;

		private Slider slider;

		private VButton btnApply;

		private VButton btnStart;

		private VButton btnCut;

		private VButton btnCopyObs;

		private VButton[] presets;

		private Field fldObs;

		private Field fldPort;

		private TextBox tbLog;

		// Cada medio segundo pone al día la ventana (y hace parpadear el punto de "en directo").
		private System.Windows.Forms.Timer timer;

		private Image logo;

		// Fondo de la ventana ya dibujado (se rehace si cambia el tamaño).
		private Bitmap bg;

		// true mientras se cargan los ajustes: así los cambios de los controles no vuelven a guardar.
		private bool loading;

		private bool pulse;

		private string lastLine = "";

		// Puerto donde Vortexs Live espera a OBS.
		private int port = 1935;

		private DateTime overlayCopiedUntil = DateTime.MinValue;

		// ---------- Arranque ----------

		public MainForm()
		{
			Text = "Vortexs Live " + Version;
			Font = Theme.Body(9f);
			BackColor = Theme.Bg;
			ForeColor = Theme.Text;
			base.FormBorderStyle = FormBorderStyle.FixedSingle;
			base.MaximizeBox = false;
			base.AutoScaleMode = AutoScaleMode.None;
			base.ClientSize = new Size(S(460f), S(646f));
			base.StartPosition = FormStartPosition.CenterScreen;
			DoubleBuffered = true;
			try
			{
				base.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
			}
			catch
			{
			}
			try
			{
				Stream logoStream = typeof(MainForm).Assembly.GetManifestResourceStream("StreamDelay.logo.png");
				if (logoStream != null)
				{
					logo = Image.FromStream(logoStream);
				}
			}
			catch
			{
			}
			int formWidth = 460;
			int pageTop = 118;
			header = Box(this, 0f, 0f, formWidth, 70f, PaintHeader);
			tabs = new Tabs
			{
				Items = new string[8] { "Directo", "Chat", "Overlays", "Mando", "Clips", "Título", "Ajustes", "Registro" }
			};
			Place(this, tabs, 20f, 72f, 420f, 36f);
			tabs.SelectedChanged += delegate
			{
				ShowPage(TabPages[tabs.Selected]);
			};
			BuildLive(pageTop);
			BuildSettings(pageTop);
			BuildLog(pageTop);
			BuildTitle(pageTop);
			BuildChat(pageTop);
			engine.Log = delegate(string msg)
			{
				lock (pendingLog)
				{
					pendingLog.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + msg);
				}
			};
			LoadConfig();
			// Idioma: botón pequeño en la cabecera (English / Español).
			VButton langBtn = new VButton
			{
				Kind = ButtonKind.Link,
				Text = I18n.English ? "Español" : "English",
				FontSize = 8f
			};
			Place(header, langBtn, 206f, 14f, 90f, 22f);
			langBtn.Click += delegate
			{
				I18n.English = !I18n.English;
				langBtn.Text = I18n.English ? "Español" : "English";
				SaveConfig();
				// Todo se vuelve a dibujar en el otro idioma (el registro ya escrito se queda como está).
				Invalidate(true);
				AppendLog(I18n.English ? "Language: English. The chat, remote and overlay pages switch when they reload." : "Idioma: español. Las páginas del chat, el Mando y los overlays cambian al recargarlas.");
			};
			// Plan (prueba, Básico o Completo): botón debajo del idioma y aviso de las pestañas de pago.
			BuildLicense(pageTop);
			// Overlays y Mando se construyen después de cargar la configuración porque la necesitan.
			BuildOverlays(pageTop);
			BuildMando(pageTop);
			BuildClips(pageTop);
			RefreshPresetButtons();
			fldObs.Box.Text = ObsServer;
			ShowPage(PageLive);
			timer = new System.Windows.Forms.Timer
			{
				Interval = 500
			};
			timer.Tick += delegate
			{
				pulse = !pulse;
				RefreshStatus();
				CheckHealth();
				LicenseTick();
			};
			timer.Start();
			RefreshStatus();
			AppendLog("Vortexs Live " + Version + ". Atajos: Ctrl+Alt+D activar/desactivar · Ctrl+Alt+↑/↓ ±5 s · Ctrl+Alt+C guardar clip");
			AppendLog("Para varias calidades: en OBS usa el servicio Twitch con servidor personalizado " + ObsServer + " y activa Transmisión mejorada.");
		}

		// ---------- Pestañas y páginas ----------

		private int PageTab(int page)
		{
			return Array.IndexOf(TabPages, page);
		}

		private void GoPage(int page)
		{
			tabs.Selected = PageTab(page);
		}

		private void ShowPage(int p)
		{
			// Chat, Overlays y Mando sin el plan Completo: se enseña el aviso en su lugar.
			if (!PageAllowed(p))
			{
				lockedPage = p;
				p = PageLocked;
				if (lockBox != null)
				{
					lockBox.Invalidate();
				}
			}
			SuspendLayout();
			for (int i = 0; i < pages.Length; i++)
			{
				foreach (Control c in pages[i])
				{
					c.Visible = i == p;
				}
			}
			ResumeLayout();
			if (p == PageClips)
			{
				RefreshClipList();
			}
			if (p == PageLog)
			{
				tbLog.SelectionStart = tbLog.TextLength;
				tbLog.ScrollToCaret();
			}
		}

		// ---------- Utilidades de diseño ----------

		// Escala un tamaño de diseño (pensado a 96 ppp) a la pantalla.
		private static int S(float v)
		{
			return Theme.S(v);
		}

		// Coloca un control (medidas de diseño) dentro de otro. Con page, solo se ve en esa página.
		private void Place(Control parent, Control c, float x, float y, float w, float h)
		{
			Place(parent, c, x, y, w, h, -1);
		}

		private void Place(Control parent, Control c, float x, float y, float w, float h, int page)
		{
			c.SetBounds(S(x), S(y), S(w), S(h));
			parent.Controls.Add(c);
			if (page >= 0)
			{
				pages[page].Add(c);
			}
		}

		// Recuadro que se dibuja con la función painter.
		private PaintBox Box(Control parent, float x, float y, float w, float h, Action<Graphics, Rectangle> painter)
		{
			return Box(parent, x, y, w, h, painter, -1);
		}

		private PaintBox Box(Control parent, float x, float y, float w, float h, Action<Graphics, Rectangle> painter, int page)
		{
			PaintBox box = new PaintBox
			{
				Painter = painter
			};
			Place(parent, box, x, y, w, h, page);
			return box;
		}

		// Etiqueta pequeña en mayúsculas encima de un campo.
		private void Label(Control parent, string text, float x, float y, float w)
		{
			Box(parent, x, y, w, 18f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawText(g, text, Theme.Mono(7f), Theme.Muted, r, StringAlignment.Near, StringAlignment.Center);
			});
		}

		// Ventana de diálogo con el tema oscuro.
		private Form DarkForm(string title, int w, int h)
		{
			Form f = new Form
			{
				Text = I18n.Tr(title),
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MaximizeBox = false,
				MinimizeBox = false,
				ShowInTaskbar = false,
				StartPosition = FormStartPosition.CenterParent,
				BackColor = Theme.Bg,
				ForeColor = Theme.Text,
				Font = Theme.Body(9f),
				AutoScaleMode = AutoScaleMode.None,
				ClientSize = new Size(S(w), S(h)),
				TopMost = base.TopMost
			};
			try
			{
				f.Icon = base.Icon;
			}
			catch
			{
			}
			f.HandleCreated += delegate
			{
				Theme.DarkTitleBar(f.Handle);
			};
			return f;
		}

		// Menú desplegable con el tema oscuro (se libera solo al cerrarse).
		private ContextMenuStrip DarkMenu()
		{
			ContextMenuStrip m = new ContextMenuStrip
			{
				Renderer = new ToolStripProfessionalRenderer(new DarkColors()),
				BackColor = Theme.Elevated,
				ForeColor = Theme.Text,
				Font = Theme.Body(9f),
				ShowImageMargin = false,
				ShowCheckMargin = true
			};
			m.Closed += delegate
			{
				m.BeginInvoke((Action)delegate
				{
					m.Dispose();
				});
			};
			return m;
		}

		// Botón que pasa a "¡Copiado!" un momento después de copiar el texto.
		private void CopyText(string text, VButton b, string original)
		{
			try
			{
				Clipboard.SetText(text);
			}
			catch
			{
				return;
			}
			b.Text = "¡Copiado!";
			System.Windows.Forms.Timer t = new System.Windows.Forms.Timer
			{
				Interval = 1300
			};
			t.Tick += delegate
			{
				b.Text = original;
				t.Dispose();
			};
			t.Start();
		}

		// "EN DIRECTO" → "En directo".
		private static string Cap(string s)
		{
			if (s.Length != 0)
			{
				return s.Substring(0, 1) + s.Substring(1).ToLowerInvariant();
			}
			return s;
		}

		private void OpenGuide()
		{
			string guide = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GUIA.html");
			try
			{
				Process.Start(new ProcessStartInfo(guide)
				{
					UseShellExecute = true
				});
			}
			catch
			{
				I18n.Show(this, "No encuentro GUIA.html junto a VortexsLive.exe.", "Guía");
			}
		}

		// Hace work en otro hilo (para no congelar la ventana) y luego done(error o null) en la ventana.
		private void Background(Action work, Action<Exception> done)
		{
			ThreadPool.QueueUserWorkItem(delegate
			{
				Exception err = null;
				try
				{
					work();
				}
				catch (Exception ex)
				{
					err = ex;
				}
				try
				{
					BeginInvoke((Action)delegate
					{
						done(err);
					});
				}
				catch
				{
				}
			});
		}

		// Texto de un error para el registro.
		private static string Why(Exception ex)
		{
			if (ex is OperationCanceledException)
			{
				return "cancelado";
			}
			return ex.Message;
		}

		// ---------- Fondo y cabecera ----------

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			if (bg == null || bg.Size != base.ClientSize)
			{
				BuildBackground();
			}
			e.Graphics.DrawImageUnscaled(bg, 0, 0);
		}

		// Fondo: brillos violeta y amarillo difuminados y una rejilla de puntos en la cabecera.
		private void BuildBackground()
		{
			if (bg != null)
			{
				bg.Dispose();
			}
			bg = new Bitmap(Math.Max(1, base.ClientSize.Width), Math.Max(1, base.ClientSize.Height));
			using (Graphics graphics = Graphics.FromImage(bg))
			{
				Theme.Quality(graphics);
				graphics.Clear(Theme.Bg);
				float w = bg.Width;
				float h = bg.Height;
				Card.Glow(graphics, new RectangleF((0f - w) * 0.35f, (0f - h) * 0.22f, w * 1.1f, h * 0.55f), Color.FromArgb(48, Theme.Violet));
				Card.Glow(graphics, new RectangleF(w * 0.45f, (0f - h) * 0.05f, w * 0.9f, h * 0.5f), Color.FromArgb(30, Theme.Yellow));
				Card.Glow(graphics, new RectangleF((0f - w) * 0.2f, h * 0.7f, w * 1f, h * 0.45f), Color.FromArgb(22, Theme.Violet));
				// Puntos que se van apagando hacia abajo.
				float step = S(22f);
				float headerHeight = S(70f);
				for (float y = step / 2f; y < headerHeight; y += step)
				{
					for (float x = step / 2f; x < w; x += step)
					{
						int alpha = (int)(40f * (1f - y / headerHeight));
						using (SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(0, alpha), Theme.Violet)))
						{
							graphics.FillEllipse(brush, x - 1f, y - 1f, 2f, 2f);
						}
					}
				}
				using (Pen pen = new Pen(Color.FromArgb(18, 255, 255, 255)))
				{
					graphics.DrawLine(pen, 0f, headerHeight - 1f, w, headerHeight - 1f);
				}
			}
		}

		// Cabecera: logo, "Vortexs Live", versión y la etiqueta de estado (apagado, en directo…).
		private void PaintHeader(Graphics g, Rectangle r)
		{
			float left = S(20f);
			if (logo != null)
			{
				g.DrawImage(logo, new RectangleF(left, S(16f), S(38f), S(38f)));
			}
			float textX = left + (float)S(50f);
			Font font = Theme.Display(16f);
			float streamWidth = Theme.Measure(g, "Vortexs", font).Width + (float)S(7f);
			SizeF delaySize = Theme.Measure(g, "Live", font);
			using (SolidBrush brush = new SolidBrush(Theme.Text))
			{
				g.DrawString("Vortexs", font, brush, textX, S(10f), StringFormat.GenericTypographic);
			}
			using (LinearGradientBrush brush2 = Theme.Brand(new RectangleF(textX + streamWidth, S(10f), delaySize.Width, delaySize.Height), true))
			{
				g.DrawString("Live", font, brush2, textX + streamWidth, S(10f), StringFormat.GenericTypographic);
			}
			Theme.DrawText(g, "DELAY · MULTISTREAM · V" + Version, Theme.Mono(7f), Theme.Muted, new RectangleF(textX + 1f, S(39f), S(200f), S(16f)), StringAlignment.Near, StringAlignment.Center);
			bool filledDot = true;
			string state;
			Color color;
			if (!engine.Running)
			{
				state = "APAGADO";
				color = Theme.Dim;
				filledDot = false;
			}
			else if (engine.OutputActive)
			{
				int liveCount = engine.LiveCount + engineV.LiveCount;
				state = (engine.Replaying ? "REPITIENDO" : ((liveCount > 1) ? ("EN DIRECTO · " + liveCount) : "EN DIRECTO"));
				color = (engine.Replaying ? Theme.Violet : Theme.Red);
			}
			else if (engine.InputActive)
			{
				state = "CARGANDO DELAY";
				color = Theme.Yellow;
			}
			else
			{
				state = "ESPERANDO A OBS";
				color = Theme.Violet;
			}
			Font badgeFont = Theme.Mono(7.5f);
			float textWidth = Theme.Measure(g, state, badgeFont).Width;
			float badgeWidth = textWidth + (float)S(36f);
			float badgeHeight = S(28f);
			RectangleF badge = new RectangleF((float)(r.Width - S(20f)) - badgeWidth, S(21f), badgeWidth, badgeHeight);
			using (GraphicsPath path = Theme.Round(badge, badgeHeight / 2f))
			{
				using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(28, color)))
				{
					g.FillPath(brush3, path);
				}
				using (Pen pen = new Pen(Color.FromArgb(110, color)))
				{
					g.DrawPath(pen, path);
				}
			}
			float dot = S(8f);
			RectangleF dotRect = new RectangleF(badge.X + (float)S(12f), badge.Y + (badgeHeight - dot) / 2f, dot, dot);
			if (filledDot)
			{
				// En directo, el punto late (pulse cambia cada medio segundo).
				if (pulse && engine.OutputActive)
				{
					Card.Glow(g, RectangleF.Inflate(dotRect, dot, dot), Color.FromArgb(120, color));
				}
				using (SolidBrush brush4 = new SolidBrush(color))
				{
					g.FillEllipse(brush4, dotRect);
				}
			}
			else
			{
				using (Pen pen2 = new Pen(color, 1.5f))
				{
					g.DrawEllipse(pen2, dotRect);
				}
			}
			Theme.DrawText(g, state, badgeFont, filledDot ? Theme.Mix(color, Color.White, 0.35f) : Theme.Muted, new RectangleF(dotRect.Right + (float)S(7f), badge.Y, textWidth + (float)S(8f), badgeHeight), StringAlignment.Near, StringAlignment.Center);
		}

		// ---------- Pestaña Registro ----------

		private void BuildLog(int y0)
		{
			Card card = new Card
			{
				Title = "Registro",
				Subtitle = "Si pides ayuda, copia este texto: no incluye tu clave."
			};
			Place(this, card, 20f, y0, 420f, 440f, PageLog);
			PaintBox logFrame = Box(card, 16f, 62f, 388f, 362f, delegate(Graphics g, Rectangle r)
			{
				using (GraphicsPath path = Theme.Round(new RectangleF((float)r.X + 0.5f, (float)r.Y + 0.5f, (float)r.Width - 1.5f, (float)r.Height - 1.5f), S(12f)))
				{
					using (SolidBrush brush = new SolidBrush(Theme.Input))
					{
						g.FillPath(brush, path);
					}
					using (Pen pen = new Pen(Theme.Border))
					{
						g.DrawPath(pen, path);
					}
				}
			});
			tbLog = new TextBox
			{
				Multiline = true,
				ReadOnly = true,
				ScrollBars = ScrollBars.Vertical,
				BorderStyle = BorderStyle.None,
				BackColor = Theme.Input,
				ForeColor = Color.FromArgb(212, 212, 218),
				Font = new Font("Consolas", 11.333333f * Theme.Scale, GraphicsUnit.Pixel),
				WordWrap = true
			};
			logFrame.Controls.Add(tbLog);
			tbLog.SetBounds(S(12f), S(10f), S(370f), S(342f));
			VButton copy = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Copiar",
				FontSize = 9.5f
			};
			// Tres botones: copiar, guía y buscar actualizaciones (los clips tienen su pestaña).
			Place(this, copy, 20f, y0 + 452, 133f, 40f, PageLog);
			copy.Click += delegate
			{
				CopyText((tbLog.Text.Length > 0) ? tbLog.Text : " ", copy, "Copiar");
			};
			VButton guide = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Guía",
				FontSize = 9.5f
			};
			Place(this, guide, 163.5f, y0 + 452, 133f, 40f, PageLog);
			guide.Click += delegate
			{
				OpenGuide();
			};
			VButton upd = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Versión",
				FontSize = 9.5f
			};
			Place(this, upd, 307f, y0 + 452, 133f, 40f, PageLog);
			upd.Click += delegate
			{
				upd.Enabled = false;
				ThreadPool.QueueUserWorkItem(delegate
				{
					CheckForUpdate(true);
					try { BeginInvoke((Action)delegate { upd.Enabled = true; }); } catch { }
				});
			};
		}

		// Añade una línea al registro (solo desde la ventana; desde otros hilos, LogLater).
		// Guarda como mucho unos 40.000 caracteres.
		private void AppendLog(string s)
		{
			s = I18n.Tr(s);
			if (tbLog.TextLength > 40000)
			{
				tbLog.Text = tbLog.Text.Substring(20000);
			}
			tbLog.AppendText(s + Environment.NewLine);
			lastLine = s;
			if (footer != null)
			{
				footer.Invalidate();
			}
		}

		// Para otros hilos: la línea se pasa al registro en el siguiente RefreshStatus.
		private void LogLater(string m)
		{
			lock (pendingLog)
			{
				pendingLog.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + m);
			}
		}

		// ---------- Atajos de teclado (Ctrl+Alt+D, ↑, ↓, C y Ctrl+Alt+Mayús+… para el Stream Deck) ----------

		[DllImport("user32.dll")]
		private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

		[DllImport("user32.dll")]
		private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

		protected override void OnHandleCreated(EventArgs e)
		{
			base.OnHandleCreated(e);
			Theme.DarkTitleBar(base.Handle);
			uint mods = MOD_CTRL | MOD_ALT | MOD_NOREPEAT;
			lock (pendingLog)
			{
				if (!RegisterHotKey(base.Handle, 1, mods, (uint)Keys.D))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+D ya lo usa otro programa.");
				}
				if (!RegisterHotKey(base.Handle, 2, mods, (uint)Keys.Up))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+↑ ya lo usa otro programa.");
				}
				if (!RegisterHotKey(base.Handle, 3, mods, (uint)Keys.Down))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+↓ ya lo usa otro programa.");
				}
				if (!RegisterHotKey(base.Handle, 4, mods, (uint)Keys.C))
				{
					pendingLog.Enqueue("Aviso: Ctrl+Alt+C ya lo usa otro programa.");
				}
				for (int i = 0; i < DeckHotkeys.Length; i++)
				{
					Keys key = (Keys)Enum.Parse(typeof(Keys), DeckHotkeys[i][1]);
					if (!RegisterHotKey(base.Handle, 5 + i, mods | MOD_SHIFT, (uint)key))
					{
						pendingLog.Enqueue("Aviso: " + HotkeyFor(DeckHotkeys[i][0]) + " ya lo usa otro programa.");
					}
				}
			}
		}

		protected override void WndProc(ref Message msg)
		{
			const int WM_HOTKEY = 0x0312;
			if (msg.Msg == WM_HOTKEY)
			{
				switch (msg.WParam.ToInt32())
				{
				case 1:
					SetEnabled(!engine.Enabled);
					break;
				case 2:
					ApplyDelay(engine.TargetMs / 1000 + 5);
					break;
				case 3:
					ApplyDelay(engine.TargetMs / 1000 - 5);
					break;
				case 4:
					SaveClip("Ctrl+Alt+C", null);
					break;
				default:
				{
					// Ctrl+Alt+Mayús+…: como pulsar la acción en el Mando.
					int i = msg.WParam.ToInt32() - 5;
					if (i >= 0 && i < DeckHotkeys.Length)
					{
						string[] parts = DeckHotkeys[i][0].Split('&');
						Dictionary<string, string> q = new Dictionary<string, string>();
						if (parts.Length > 1)
						{
							q["n"] = parts[1].Substring(2);
						}
						RemoteAction(parts[0], q);
					}
					break;
				}
				}
			}
			base.WndProc(ref msg);
		}

		// ---------- Cierre ----------

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			if (engine.OutputActive && I18n.Show(this, "Se está transmitiendo. Cerrar corta la transmisión. ¿Seguir?", "Cerrar", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
			{
				e.Cancel = true;
				return;
			}
			for (int i = 1; i <= 4 + DeckHotkeys.Length; i++)
			{
				UnregisterHotKey(base.Handle, i);
			}
			SaveConfig();
			engine.Stop();
			StopVertical();
			base.OnFormClosing(e);
		}

		// ---------- Configuración (VortexsLive.ini) ----------
		// Una línea "clave=valor" por ajuste. Cada parte de la ventana lee y escribe las suyas:
		// aquí las generales, y luego LoadDestinations, LoadTitleConfig y LoadChatConfig (que a su
		// vez llama a las de Overlays, comandos, Mando y actualizaciones). Lo mismo al guardar.

		private void LoadConfig()
		{
			loading = true;
			I18n.English = I18n.DefaultEnglish;
			slider.Value = 30;
			engine.TargetMs = 30000;
			clipDirCustom = "";
			clipMods = false;
			clipUsers = "";
			tgDelay.Checked = true;
			Dictionary<string, string> raw = new Dictionary<string, string>();
			// Antes se llamaba StreamDelay: si aún no hay VortexsLive.ini, se leen los ajustes de StreamDelay.ini
			// (se guardan ya en el nuevo; el viejo se deja como está).
			string readPath = File.Exists(configPath) ? configPath : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StreamDelay.ini");
			if (File.Exists(readPath))
			{
				foreach (string line in File.ReadAllLines(readPath))
				{
					int eq = line.IndexOf('=');
					if (eq < 0)
					{
						continue;
					}
					string key = line.Substring(0, eq);
					string value = (raw[key] = line.Substring(eq + 1));
					try
					{
						switch (key)
						{
						case "delay":
						{
							int seconds = Math.Max(0, Math.Min(MaxDelay, int.Parse(value)));
							slider.Value = seconds;
							engine.TargetMs = seconds * 1000;
							break;
						}
						case "enabled":
							engine.Enabled = value == "1";
							tgDelay.Checked = engine.Enabled;
							break;
						case "topmost":
							tgTop.Checked = value == "1";
							break;
						case "replay":
							tgReplay.Checked = value == "1";
							engine.ReplayOnIncrease = tgReplay.Checked;
							break;
						case "port":
							port = Math.Max(1024, Math.Min(65535, int.Parse(value)));
							fldPort.Box.Text = port.ToString();
							break;
						case "lan":
							tgLan.Checked = value == "1";
							break;
						case "ffmpeg":
							tgFfmpeg.Checked = value == "1";
							break;
						case "lang":
							I18n.English = value == "en";
							break;
						case "tiktok_vertical":
							tiktokVertical = value == "1";
							break;
						case "clip_dir":
							clipDirCustom = value.Trim();
							break;
						case "clip_mods":
							clipMods = value == "1";
							break;
						case "clip_users":
							clipUsers = value.Trim();
							break;
						}
					}
					catch
					{
						// Un valor mal escrito se ignora y se queda el de por defecto.
					}
				}
			}
			base.TopMost = tgTop.Checked;
			LoadLicenseConfig(raw);
			LoadDestinations(raw);
			LoadTitleConfig(raw);
			LoadChatConfig(raw);
			loading = false;
		}

		private void SaveConfig()
		{
			try
			{
				List<string> lines = new List<string>
				{
					"delay=" + engine.TargetMs / 1000,
					"enabled=" + (engine.Enabled ? "1" : "0"),
					"topmost=" + (tgTop.Checked ? "1" : "0"),
					"replay=" + (tgReplay.Checked ? "1" : "0"),
					"port=" + port,
					"lan=" + (tgLan.Checked ? "1" : "0"),
					"ffmpeg=" + (tgFfmpeg.Checked ? "1" : "0"),
					"lang=" + I18n.Lang,
					"tiktok_vertical=" + (tiktokVertical ? "1" : "0"),
					"clip_dir=" + clipDirCustom,
					"clip_mods=" + (clipMods ? "1" : "0"),
					"clip_users=" + clipUsers
				};
				SaveDestinations(lines);
				SaveTitleConfig(lines);
				SaveChatConfig(lines);
				SaveLicenseConfig(lines);
				File.WriteAllLines(configPath, lines.ToArray());
			}
			catch
			{
			}
		}
	}
}
