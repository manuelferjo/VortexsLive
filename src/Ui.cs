// Interfaz de la 4.21:
//  · Avisos propios (toasts) abajo, que entran y se van solos.
//  · Transición al cambiar de pestaña: la página nueva entra con un fundido y un deslizamiento.
//  · La cifra grande del delay cuenta hasta el valor nuevo en vez de saltar.
//  · Ventana que se ensancha: a la derecha, la actividad del chat o la vista previa del overlay.
//  · Ventana mini siempre encima (delay, ±5 s, clip, estado del directo).
//  · Ajustes por secciones (Destinos · OBS · Opciones).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamDelay
{
	// ---------- Aviso (ventanita sin bordes, no roba el foco) ----------
	internal class ToastForm : Form
	{
		public string Msg = "";
		public string IconName = "info";
		public Color Accent = Theme.Violet;
		public float Age;          // segundos
		public float Life = 4.2f;  // segundos visible
		public float Y;            // posición actual (pantalla)
		public bool Placed;

		public ToastForm()
		{
			FormBorderStyle = FormBorderStyle.None;
			ShowInTaskbar = false;
			StartPosition = FormStartPosition.Manual;
			BackColor = Theme.Elevated;
			DoubleBuffered = true;
			Opacity = 0.0;
		}

		protected override bool ShowWithoutActivation
		{
			get { return true; }
		}

		protected override CreateParams CreateParams
		{
			get
			{
				CreateParams cp = base.CreateParams;
				cp.ExStyle |= 0x08000000 | 0x80;   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
				return cp;
			}
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			using (GraphicsPath p = Theme.Round(new RectangleF(0f, 0f, Width, Height), Theme.S(12f)))
			{
				Region = new Region(p);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			Theme.Quality(g);
			RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
			using (GraphicsPath p = Theme.Round(r, Theme.S(12f)))
			{
				using (LinearGradientBrush bg = new LinearGradientBrush(r, Color.FromArgb(30, 30, 40), Theme.Elevated, LinearGradientMode.Vertical))
				{
					g.FillPath(bg, p);
				}
				using (Pen pen = new Pen(Color.FromArgb(150, Accent)))
				{
					g.DrawPath(pen, p);
				}
			}
			float pad = Theme.S(14f), isz = Theme.S(18f);
			// Círculo suave con el icono.
			RectangleF ic = new RectangleF(pad, (Height - isz - Theme.S(10f)) / 2f, isz + Theme.S(10f), isz + Theme.S(10f));
			using (SolidBrush b = new SolidBrush(Color.FromArgb(40, Accent)))
			{
				g.FillEllipse(b, ic);
			}
			Icons.Draw(g, IconName, RectangleF.Inflate(ic, -Theme.S(5f), -Theme.S(5f)), Accent);
			RectangleF tr = new RectangleF(ic.Right + Theme.S(10f), Theme.S(6f), Width - ic.Right - Theme.S(24f), Height - Theme.S(12f));
			Theme.DrawWrapped(g, Msg, Theme.BodySemi(8.75f), Theme.Text, tr);
			// Barra fina del tiempo que le queda.
			float left = Math.Max(0f, 1f - Age / Life);
			using (SolidBrush b = new SolidBrush(Color.FromArgb(120, Accent)))
			{
				g.FillRectangle(b, Theme.S(12f), Height - Theme.S(3f), (Width - Theme.S(24f)) * left, Theme.S(2f));
			}
		}
	}

	// ---------- Capa de la transición entre pestañas ----------
	internal class TransitionLayer : Control
	{
		public Bitmap From, To;
		public float T;     // 0 → 1
		public int Dir = 1; // 1: entra desde la derecha; -1: desde la izquierda

		public TransitionLayer()
		{
			SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Opaque, true);
			TabStop = false;
		}

		private static void DrawAlpha(Graphics g, Bitmap b, float x, float alpha)
		{
			if (b == null || alpha <= 0f)
			{
				return;
			}
			using (ImageAttributes ia = new ImageAttributes())
			{
				ColorMatrix cm = new ColorMatrix { Matrix33 = Math.Min(1f, alpha) };
				ia.SetColorMatrix(cm);
				g.DrawImage(b, new Rectangle((int)x, 0, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			float t = T * T * (3f - 2f * T);
			float slide = Theme.S(18f);
			e.Graphics.Clear(Theme.Bg);
			if (From != null)
			{
				e.Graphics.DrawImageUnscaled(From, 0, 0);
			}
			// La nueva aparece encima, cada vez más opaca y llegando a su sitio.
			DrawAlpha(e.Graphics, To, Dir * slide * (1f - t), t);
		}
	}

	internal partial class MainForm
	{
		[DllImport("user32.dll")]
		private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);

		private System.Windows.Forms.Timer uiTimer;
		private DateTime uiLast = DateTime.UtcNow;

		// Textos de ayuda al pasar el ratón (botones que son solo un icono).
		private readonly ToolTip tip = new ToolTip();

		// Animaciones de la ventana: se mueven solo mientras hay algo que mover.
		private void UiAnimate()
		{
			if (uiTimer == null)
			{
				uiTimer = new System.Windows.Forms.Timer { Interval = 15 };
				uiTimer.Tick += delegate { UiTick(); };
			}
			if (!uiTimer.Enabled)
			{
				uiLast = DateTime.UtcNow;
				uiTimer.Start();
			}
		}

		private void UiTick()
		{
			DateTime now = DateTime.UtcNow;
			float dt = (float)Math.Min(0.05, (now - uiLast).TotalSeconds);
			uiLast = now;
			bool more = false;
			more |= TickToasts(dt);
			more |= TickTransition(dt);
			more |= TickHero(dt);
			if (!more)
			{
				uiTimer.Stop();
			}
		}

		// ======================= Avisos =======================

		private readonly List<ToastForm> toasts = new List<ToastForm>();
		private string lastToastProblem;

		// kind: ok · info · warn · error · clip
		public void Toast(string text, string kind)
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			if (InvokeRequired)
			{
				try
				{
					BeginInvoke((Action)delegate { Toast(text, kind); });
				}
				catch
				{
				}
				return;
			}
			Form anchor = (mini != null && mini.Visible) ? (Form)mini : this;
			if (!anchor.Visible || anchor.WindowState == FormWindowState.Minimized)
			{
				return;
			}
			ToastForm t = new ToastForm { Msg = text };
			switch (kind)
			{
			case "ok": t.IconName = "check"; t.Accent = Theme.Green; break;
			case "warn": t.IconName = "warning"; t.Accent = Theme.Yellow; t.Life = 6f; break;
			case "error": t.IconName = "x"; t.Accent = Theme.Red; t.Life = 6f; break;
			case "clip": t.IconName = "film"; t.Accent = Color.FromArgb(255, 138, 61); break;
			default: t.IconName = "info"; t.Accent = Theme.Violet; break;
			}
			int w = Math.Min(S(380f), anchor.ClientSize.Width - S(24f));
			Size need = TextRenderer.MeasureText(I18n.Tr(text), Theme.BodySemi(8.75f), new Size(w - S(70f), 1000), TextFormatFlags.WordBreak);
			t.ClientSize = new Size(w, Math.Max(S(48f), need.Height + S(28f)));
			// Como mucho tres a la vez: el más antiguo se va.
			if (toasts.Count >= 3)
			{
				toasts[0].Age = Math.Max(toasts[0].Age, toasts[0].Life);
			}
			toasts.Add(t);
			t.Show(anchor);
			UiAnimate();
		}

		private bool TickToasts(float dt)
		{
			if (toasts.Count == 0)
			{
				return false;
			}
			Form anchor = (mini != null && mini.Visible) ? (Form)mini : this;
			Rectangle area = anchor.RectangleToScreen(anchor.ClientRectangle);
			int colW = anchor == this ? S(460f) : area.Width;
			float bottom = area.Bottom - S(16f);
			for (int i = toasts.Count - 1; i >= 0; i--)
			{
				ToastForm t = toasts[i];
				t.Age += dt;
				float fadeIn = Math.Min(1f, t.Age / 0.18f);
				float fadeOut = t.Age > t.Life ? Math.Max(0f, 1f - (t.Age - t.Life) / 0.3f) : 1f;
				float target = bottom - t.Height;
				bottom = target - S(8f);
				if (!t.Placed)
				{
					t.Y = target + S(14f);
					t.Placed = true;
				}
				t.Y += (target - t.Y) * Math.Min(1f, dt * 14f);
				t.Location = new Point(area.X + (colW - t.Width) / 2, (int)Math.Round(t.Y));
				t.Opacity = 0.97 * Math.Min(fadeIn, fadeOut);
				t.Invalidate();
				if (fadeOut <= 0f)
				{
					toasts.RemoveAt(i);
					t.Close();
					t.Dispose();
				}
			}
			return toasts.Count > 0;
		}

		// Cada medio segundo: si aparece un problema nuevo, también sale como aviso.
		private void UiSlowTick()
		{
			string prob = FirstProblem();
			if (prob != null && prob != lastToastProblem)
			{
				Toast(prob, "warn");
			}
			lastToastProblem = prob;
			if (ovHint != null && ovHint.Visible)
			{
				ovHint.Invalidate();
			}
			if (side != null && side.Visible)
			{
				side.Invalidate();
			}
			if (mini != null && mini.Visible)
			{
				mini.RefreshMini();
			}
		}

		// ======================= Transición entre pestañas =======================

		private TransitionLayer trLayer;
		private int trFromPage = -1;

		private Rectangle PageArea()
		{
			int top = S(126f);
			return new Rectangle(0, top, S(460f), ClientSize.Height - top);
		}

		// Lo que se ve ahora en la zona de la página (desde la pantalla).
		private Bitmap GrabPageArea()
		{
			try
			{
				Rectangle a = PageArea();
				using (Bitmap full = new Bitmap(ClientSize.Width, ClientSize.Height))
				{
					using (Graphics g = Graphics.FromImage(full))
					{
						IntPtr dc = g.GetHdc();
						bool ok = PrintWindow(Handle, dc, 3);   // solo la zona de dentro, con todo dibujado
						g.ReleaseHdc(dc);
						if (!ok)
						{
							return null;
						}
					}
					return full.Clone(a, PixelFormat.Format32bppPArgb);
				}
			}
			catch
			{
				return null;
			}
		}

		// La página nueva dibujada en un bitmap: el fondo de la ventana y sus recuadros encima.
		private Bitmap RenderPage(int p)
		{
			try
			{
				Rectangle a = PageArea();
				Bitmap b = new Bitmap(a.Width, a.Height, PixelFormat.Format32bppPArgb);
				using (Graphics g = Graphics.FromImage(b))
				{
					if (bg != null)
					{
						g.DrawImage(bg, new Rectangle(0, 0, a.Width, a.Height), a, GraphicsUnit.Pixel);
					}
					else
					{
						g.Clear(Theme.Bg);
					}
				}
				foreach (Control c in pages[p])
				{
					if (!c.Visible || c.Width <= 0 || c.Height <= 0 || c.Parent != this)
					{
						continue;
					}
					Rectangle cr = new Rectangle(c.Left - a.X, c.Top - a.Y, c.Width, c.Height);
					if (cr.Right <= 0 || cr.Bottom <= 0 || cr.X >= a.Width || cr.Y >= a.Height)
					{
						continue;
					}
					using (Bitmap cb = new Bitmap(c.Width, c.Height, PixelFormat.Format32bppPArgb))
					{
						using (Graphics g = Graphics.FromImage(cb))
						{
							g.DrawImage(bg ?? new Bitmap(1, 1), new Rectangle(0, 0, c.Width, c.Height), new Rectangle(c.Left, c.Top, c.Width, c.Height), GraphicsUnit.Pixel);
						}
						c.DrawToBitmap(cb, new Rectangle(0, 0, c.Width, c.Height));
						using (Graphics g = Graphics.FromImage(b))
						{
							g.DrawImageUnscaled(cb, cr.X, cr.Y);
						}
					}
				}
				return b;
			}
			catch
			{
				return null;
			}
		}

		// Se llama al principio de ShowPage (con la página de antes aún a la vista).
		private Bitmap TransitionBegin(int from, int to)
		{
			if (!IsHandleCreated || !Visible || from < 0 || from == to || WindowState == FormWindowState.Minimized)
			{
				return null;
			}
			Bitmap old = GrabPageArea();
			if (old == null)
			{
				return null;
			}
			if (trLayer == null)
			{
				trLayer = new TransitionLayer();
				Controls.Add(trLayer);
			}
			trLayer.Bounds = PageArea();
			if (trLayer.From != null) trLayer.From.Dispose();
			if (trLayer.To != null) trLayer.To.Dispose();
			trLayer.From = old;
			trLayer.To = null;
			trLayer.T = 0f;
			trLayer.Dir = PageTab(to) >= PageTab(from) ? 1 : -1;
			trLayer.Visible = true;
			trLayer.BringToFront();
			trLayer.Update();
			return old;
		}

		// Al final de ShowPage: ya se ven los controles nuevos (debajo de la capa).
		private void TransitionEnd(int to)
		{
			if (trLayer == null || !trLayer.Visible || trLayer.To != null)
			{
				return;
			}
			trLayer.To = RenderPage(to);
			if (trLayer.To == null)
			{
				trLayer.Visible = false;
				return;
			}
			trLayer.BringToFront();
			UiAnimate();
		}

		private bool TickTransition(float dt)
		{
			if (trLayer == null || !trLayer.Visible || trLayer.To == null)
			{
				return false;
			}
			trLayer.T = Math.Min(1f, trLayer.T + dt / 0.2f);
			trLayer.Invalidate();
			if (trLayer.T >= 1f)
			{
				trLayer.Visible = false;
				trLayer.From.Dispose(); trLayer.From = null;
				trLayer.To.Dispose(); trLayer.To = null;
				return false;
			}
			return true;
		}

		// ======================= Cifra del delay que cuenta =======================

		private float heroShown = -1f;

		private int HeroValue(int seconds)
		{
			if (heroShown < 0f || !IsHandleCreated)
			{
				heroShown = seconds;
			}
			if (Math.Abs(heroShown - seconds) > 0.01f)
			{
				UiAnimate();
			}
			return (int)Math.Round(heroShown);
		}

		private bool TickHero(float dt)
		{
			float target = engine.TargetMs / 1000;
			if (heroShown < 0f || Math.Abs(heroShown - target) <= 0.01f)
			{
				return false;
			}
			float step = (target - heroShown) * (1f - (float)Math.Exp(-dt * 9.0));
			// Siempre avanza algo, para que no se quede a medias.
			if (Math.Abs(step) < dt * 6f) step = Math.Sign(target - heroShown) * Math.Min(Math.Abs(target - heroShown), dt * 6f);
			heroShown += step;
			if (Math.Abs(heroShown - target) < 0.02f) heroShown = target;
			if (heroBox != null) heroBox.Invalidate();
			if (mini != null && mini.Visible) mini.RefreshMini();
			return true;
		}

		// ======================= Ventana ancha: panel lateral =======================

		private SidePanel side;
		private PanelHandle panelHandle;
		private int wideWidth = 960;        // ancho al abrir el panel (el último que usaste)
		private bool panelTipShown;         // el aviso de la pestaña ya salió una vez
		private PaintBox ovHint;
		private int winWidth = 460;   // ancho guardado (medida de diseño)
		private readonly List<ChatItem> activity = new List<ChatItem>();
		private bool activityDirty;

		private void ActivityAdd(ChatItem it)
		{
			if (it == null || (it.t != "msg" && it.t != "ev"))
			{
				return;
			}
			lock (activity)
			{
				activity.Add(it);
				if (activity.Count > 80)
				{
					activity.RemoveAt(0);
				}
				activityDirty = true;
			}
		}

		public List<ChatItem> ActivitySnapshot()
		{
			lock (activity)
			{
				activityDirty = false;
				return new List<ChatItem>(activity);
			}
		}

		public bool ChatOn
		{
			get { return tgChat != null && tgChat.Checked; }
		}

		public void TurnChatOn()
		{
			if (tgChat != null && !tgChat.Checked)
			{
				if (!PageAllowed(PageChat))
				{
					GoPage(PageChat);
					return;
				}
				tgChat.Checked = true;
				Toast("Chat conjunto activado.", "ok");
			}
		}

		public int CurrentPage
		{
			get { return TabPages[Math.Max(0, tabs.Selected)]; }
		}

		private void UiInit()
		{
			// La ventana se puede ensanchar (no hacer más alta): a la derecha sale el panel.
			FormBorderStyle = FormBorderStyle.Sizable;
			MaximizeBox = false;
			Size min = SizeFromClientSize(new Size(S(460f), S(654f)));
			MinimumSize = min;
			MaximumSize = new Size(SizeFromClientSize(new Size(S(1180f), S(654f))).Width, min.Height);
			side = new SidePanel(this);
			Controls.Add(side);
			side.Visible = false;
			panelHandle = new PanelHandle();
			Controls.Add(panelHandle);
			panelHandle.Click += delegate { TogglePanel(); };
			tip.SetToolTip(panelHandle, I18n.Tr("Abrir o cerrar el panel de la derecha: actividad del chat y vista previa de los overlays"));
			// La primera vez se explica con un aviso.
			if (!panelTipShown)
			{
				System.Windows.Forms.Timer once = new System.Windows.Forms.Timer { Interval = 2500 };
				once.Tick += delegate
				{
					once.Stop();
					once.Dispose();
					if (!panelTipShown && side != null && !side.Visible)
					{
						panelTipShown = true;
						SaveConfig();
						Toast("Nuevo: pulsa la pestaña del borde derecho para ver el chat y la vista previa de los overlays.", "info");
					}
				};
				once.Start();
			}
			Resize += delegate { LayoutWide(); };
			if (winWidth > 460)
			{
				ClientSize = new Size(S(Math.Min(1180, winWidth)), S(654f));
			}
			LayoutWide();
			// El tiempo de directo, los avisos y el panel se refrescan con el temporizador de siempre.
		}

		private void LayoutWide()
		{
			if (header != null)
			{
				header.Width = ClientSize.Width;
			}
			bool wide = ClientSize.Width >= S(760f);
			if (panelHandle != null)
			{
				panelHandle.Bounds = new Rectangle(S(443f), S(250f), S(16f), S(150f));
				panelHandle.Open = wide;
				panelHandle.BringToFront();
				panelHandle.Invalidate();
			}
			if (side != null)
			{
				side.Visible = wide;
				if (wide)
				{
					side.Bounds = new Rectangle(S(460f), S(72f), ClientSize.Width - S(480f), ClientSize.Height - S(92f));
					side.Invalidate();
				}
			}
			int w = (int)Math.Round(ClientSize.Width / Theme.Scale);
			if (w != winWidth && IsHandleCreated)
			{
				winWidth = w;
			}
			if (wide)
			{
				wideWidth = Math.Max(760, Math.Min(1180, w));
			}
		}

		// Pestaña del borde: abre el panel (con el último ancho) o lo cierra.
		private void TogglePanel()
		{
			panelTipShown = true;
			if (ClientSize.Width >= S(760f))
			{
				ClientSize = new Size(S(460f), ClientSize.Height);
			}
			else
			{
				ClientSize = new Size(S((float)Math.Max(760, wideWidth)), ClientSize.Height);
				// Que no se salga de la pantalla.
				Rectangle wa = Screen.FromControl(this).WorkingArea;
				if (Right > wa.Right)
				{
					Left = Math.Max(wa.Left, wa.Right - Width);
				}
			}
			SaveConfig();
		}

		// ======================= Ventana mini =======================

		private MiniWindow mini;

		public void ShowMini()
		{
			if (mini == null || mini.IsDisposed)
			{
				mini = new MiniWindow(this);
			}
			Point p = new Point(Right - mini.Width, Top);
			Rectangle wa = Screen.FromControl(this).WorkingArea;
			mini.Location = new Point(Math.Max(wa.Left, Math.Min(p.X, wa.Right - mini.Width)), Math.Max(wa.Top, p.Y));
			mini.Show();
			mini.RefreshMini();
			Hide();
		}

		public void HideMini()
		{
			Show();
			if (WindowState == FormWindowState.Minimized)
			{
				WindowState = FormWindowState.Normal;
			}
			Activate();
			if (mini != null)
			{
				mini.Hide();
			}
		}

		// Para la ventana mini.
		public int DelaySeconds { get { return engine.TargetMs / 1000; } }
		public int DelayShown { get { return heroShown < 0 ? engine.TargetMs / 1000 : (int)Math.Round(heroShown); } }
		public bool DelayOn { get { return engine.Enabled; } }
		public bool IsLive { get { return engine.OutputActive; } }
		public void MiniToggleDelay() { SetEnabled(!engine.Enabled); tgDelay.Checked = engine.Enabled; }
		public void MiniNudge(int s) { ApplyDelay(Math.Max(0, engine.TargetMs / 1000 + s)); }
		public void MiniClip()
		{
			if (!ClipsAllowed())
			{
				HideMini();
				LicenseDialog();
				return;
			}
			if (!SaveClip("ventana mini", null))
			{
				Toast("Se acaba de guardar un clip: espera unos segundos.", "info");
			}
		}

		public string LiveFor()
		{
			lock (sumLock)
			{
				if (stats == null)
				{
					return null;
				}
				TimeSpan t = DateTime.Now - stats.Start;
				return ((int)t.TotalHours).ToString() + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00");
			}
		}

		public int ViewersTotal()
		{
			ChatHub h = chatHub;
			if (h == null)
			{
				return -1;
			}
			int total = 0;
			bool any = false;
			foreach (int v in new[] { h.TwitchViewers, h.KickViewers, h.YouTubeViewers, h.TikTokViewers })
			{
				if (v >= 0) { total += v; any = true; }
			}
			return any ? total : -1;
		}

		// ======================= Ajustes por secciones =======================

		private Card[] setCards = new Card[0];
		private VButton[] setPickBtns = new VButton[0];
		private int setPick;

		private void BuildSettingsPicker(int y0)
		{
			string[] names = new string[3] { "Destinos", "OBS", "Opciones" };
			string[] icons = new string[3] { "live", "layers", "gear" };
			setPickBtns = new VButton[3];
			for (int i = 0; i < 3; i++)
			{
				int idx = i;
				setPickBtns[i] = new VButton { Kind = ButtonKind.Pill, Text = names[i], Selected = i == setPick, FontSize = 8f };
				Place(this, setPickBtns[i], 20f + (float)i * 142f, (float)y0, 136f, 28f, PageSettings);
				setPickBtns[i].Click += delegate
				{
					setPick = idx;
					ApplySettingsPick();
				};
			}
		}

		private void ApplySettingsPick()
		{
			for (int i = 0; i < setCards.Length; i++)
			{
				if (setCards[i] != null)
				{
					setCards[i].Visible = i == setPick;
				}
			}
			for (int i = 0; i < setPickBtns.Length; i++)
			{
				setPickBtns[i].Selected = i == setPick;
				setPickBtns[i].Invalidate();
			}
		}
	}

	// ======================= Pestaña del borde que abre y cierra el panel =======================
	internal class PanelHandle : VControl
	{
		public bool Open;

		public PanelHandle()
		{
			Cursor = Cursors.Hand;
			TabStop = false;
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			Theme.Quality(g);
			float h = Ease(hoverT);
			RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
			using (GraphicsPath p = Theme.Round(r, r.Width / 2f))
			{
				using (LinearGradientBrush b = new LinearGradientBrush(r, Theme.Mix(Theme.Elevated, Theme.Violet, 0.10f + 0.12f * h), Theme.Mix(Theme.Elevated, Theme.Yellow, 0.05f + 0.08f * h), LinearGradientMode.Vertical))
				{
					g.FillPath(b, p);
				}
				using (Pen pen = new Pen(Theme.Mix(Theme.BorderHi, Theme.Violet, 0.35f + 0.5f * h), 1.2f))
				{
					g.DrawPath(pen, p);
				}
			}
			// Flecha: › abre, ‹ cierra.
			float cx = Width / 2f, cy = Theme.S(16f), a = Theme.S(3.5f);
			using (Pen pen = new Pen(Theme.Mix(Theme.Muted, Theme.VioletLight, 0.4f + 0.6f * h), Theme.S(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
			{
				float d = Open ? a * 0.6f : -a * 0.6f;
				g.DrawLines(pen, new[] { new PointF(cx + d, cy - a), new PointF(cx - d, cy), new PointF(cx + d, cy + a) });
			}
			// Texto en vertical.
			GraphicsState st = g.Save();
			g.TranslateTransform(cx, Height - Theme.S(12f));
			g.RotateTransform(-90f);
			Theme.DrawText(g, Open ? "OCULTAR PANEL" : "CHAT · VISTA PREVIA", Theme.Mono(6.5f), Theme.Mix(Theme.Muted, Theme.VioletLight, 0.5f * h), new RectangleF(0f, -Theme.S(8f), Height - Theme.S(40f), Theme.S(16f)), StringAlignment.Near, StringAlignment.Center);
			g.Restore(st);
		}
	}

	// ======================= Panel lateral =======================
	internal class SidePanel : VControl
	{
		private readonly MainForm f;
		private readonly VButton action;
		private readonly VButton open;

		public SidePanel(MainForm owner)
		{
			f = owner;
			action = new VButton { Kind = ButtonKind.Primary, Text = "Activar el chat", FontSize = 9.5f, Icon = "chat" };
			action.Click += delegate { f.TurnChatOn(); Invalidate(); };
			Controls.Add(action);
			open = new VButton { Kind = ButtonKind.Ghost, Text = "Abrir en el navegador", FontSize = 9f, Icon = "external" };
			open.Click += delegate { f.OpenPickedOverlay(); };
			Controls.Add(open);
		}

		private static Color Hex(string h, Color def)
		{
			try
			{
				if (!string.IsNullOrEmpty(h) && h.StartsWith("#") && h.Length >= 7)
				{
					return Color.FromArgb(int.Parse(h.Substring(1, 2), NumberStyles.HexNumber), int.Parse(h.Substring(3, 2), NumberStyles.HexNumber), int.Parse(h.Substring(5, 2), NumberStyles.HexNumber));
				}
			}
			catch
			{
			}
			return def;
		}

		private static Color PfColor(string p)
		{
			switch (p)
			{
			case "twitch": return Color.FromArgb(169, 112, 255);
			case "kick": return Color.FromArgb(83, 252, 24);
			case "youtube": return Color.FromArgb(255, 59, 59);
			case "tiktok": return Color.FromArgb(37, 244, 238);
			default: return Theme.Violet;
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics g = e.Graphics;
			Theme.Quality(g);
			RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
			using (GraphicsPath p = Theme.Round(r, Theme.S(16f)))
			{
				using (SolidBrush b = new SolidBrush(Color.FromArgb(235, Theme.Surface)))
				{
					g.FillPath(b, p);
				}
				using (Pen pen = new Pen(Theme.Border))
				{
					g.DrawPath(pen, p);
				}
			}
			bool preview = f.CurrentPage == 5;
			action.Visible = false;
			open.Visible = preview;
			if (preview)
			{
				PaintPreview(g);
			}
			else
			{
				PaintActivity(g);
			}
		}

		// ---------- Actividad del chat ----------
		private void PaintActivity(Graphics g)
		{
			float pad = Theme.S(18f);
			Icons.Draw(g, "chat", new RectangleF(pad, Theme.S(16f), Theme.S(16f), Theme.S(16f)), Theme.Violet);
			Theme.DrawText(g, "ACTIVIDAD DEL CHAT", Theme.Mono(7.5f), Theme.Muted, new RectangleF(pad + Theme.S(24f), Theme.S(14f), Width - pad * 2, Theme.S(20f)), StringAlignment.Near, StringAlignment.Center);
			int viewers = f.ViewersTotal();
			if (viewers >= 0)
			{
				Theme.DrawText(g, viewers.ToString("N0", CultureInfo.CurrentCulture), Theme.BodyBold(9f), Theme.Text, new RectangleF(0, Theme.S(14f), Width - pad - Theme.S(20f), Theme.S(20f)), StringAlignment.Far, StringAlignment.Center);
				Icons.Draw(g, "eye", new RectangleF(Width - pad - Theme.S(16f), Theme.S(16f), Theme.S(16f), Theme.S(16f)), Theme.Red);
			}
			RectangleF list = new RectangleF(pad, Theme.S(44f), Width - pad * 2, Height - Theme.S(60f));
			if (!f.ChatOn)
			{
				// Estado vacío con el botón para arreglarlo.
				float cy = list.Y + list.Height * 0.32f;
				using (SolidBrush b = new SolidBrush(Color.FromArgb(30, Theme.Violet)))
				{
					g.FillEllipse(b, Width / 2f - Theme.S(28f), cy - Theme.S(28f), Theme.S(56f), Theme.S(56f));
				}
				Icons.Draw(g, "chat", new RectangleF(Width / 2f - Theme.S(14f), cy - Theme.S(14f), Theme.S(28f), Theme.S(28f)), Theme.Violet);
				Theme.DrawText(g, "El chat conjunto está apagado", Theme.BodyBold(10f), Theme.Text, new RectangleF(list.X, cy + Theme.S(38f), list.Width, Theme.S(22f)), StringAlignment.Center, StringAlignment.Center);
				Theme.DrawWrapped(g, "Actívalo para ver aquí los mensajes y eventos de Twitch, Kick, YouTube y TikTok mientras trabajas en otras pestañas.", Theme.Body(8.25f), Theme.Muted, new RectangleF(list.X + Theme.S(20f), cy + Theme.S(62f), list.Width - Theme.S(40f), Theme.S(52f)));
				action.Bounds = new Rectangle((int)(Width / 2f - Theme.S(100f)), (int)(cy + Theme.S(122f)), Theme.S(200f), Theme.S(38f));
				action.Visible = true;
				return;
			}
			List<ChatItem> items = f.ActivitySnapshot();
			if (items.Count == 0)
			{
				Theme.DrawText(g, "Esperando mensajes…", Theme.Body(9f), Theme.Dim, list, StringAlignment.Center, StringAlignment.Center);
				return;
			}
			// De abajo arriba: lo último, abajo.
			float y = list.Bottom;
			Font nameFont = Theme.BodyBold(8.75f), textFont = Theme.Body(8.75f);
			GraphicsState st = g.Save();
			g.SetClip(list);
			for (int i = items.Count - 1; i >= 0 && y > list.Y; i--)
			{
				ChatItem it = items[i];
				if (it.t == "ev")
				{
					float h = Theme.S(30f);
					y -= h + Theme.S(4f);
					RectangleF row = new RectangleF(list.X, y, list.Width, h);
					using (GraphicsPath p = Theme.Round(row, Theme.S(8f)))
					using (SolidBrush b = new SolidBrush(Color.FromArgb(26, PfColor(it.p))))
					{
						g.FillPath(b, p);
					}
					using (SolidBrush b = new SolidBrush(PfColor(it.p)))
					{
						g.FillRectangle(b, row.X, row.Y + Theme.S(6f), Theme.S(3f), h - Theme.S(12f));
					}
					Theme.DrawText(g, it.text ?? "", Theme.BodySemi(8.5f), Theme.Text, new RectangleF(row.X + Theme.S(12f), row.Y, row.Width - Theme.S(16f), h), StringAlignment.Near, StringAlignment.Center);
					continue;
				}
				string text = PlainItem(it);
				string name = it.user ?? "";
				// La medida se queda algo corta con esta fuente: un poco de margen para no cortar el nombre.
				float nameW = Theme.Measure(g, name + ":", nameFont).Width * 1.12f + Theme.S(8f);
				float lh = Theme.S(20f);
				y -= lh + Theme.S(2f);
				using (SolidBrush b = new SolidBrush(PfColor(it.p)))
				{
					g.FillEllipse(b, list.X, y + lh / 2f - Theme.S(3f), Theme.S(6f), Theme.S(6f));
				}
				Color nc = Hex(it.color, PfColor(it.p));
				if (nc.GetBrightness() < 0.35f) nc = Theme.Mix(nc, Color.White, 0.45f);
				Theme.DrawText(g, name + ":", nameFont, nc, new RectangleF(list.X + Theme.S(12f), y, nameW, lh), StringAlignment.Near, StringAlignment.Center);
				Theme.DrawText(g, text, textFont, Theme.Text, new RectangleF(list.X + Theme.S(12f) + nameW, y, list.Width - Theme.S(12f) - nameW, lh), StringAlignment.Near, StringAlignment.Center);
			}
			g.Restore(st);
		}

		private static string PlainItem(ChatItem it)
		{
			if (it.parts == null)
			{
				return it.text ?? "";
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (ChatPart p in it.parts)
			{
				sb.Append(p.e != null ? p.n : p.x);
			}
			return sb.ToString();
		}

		// ---------- Vista previa del overlay elegido ----------
		private void PaintPreview(Graphics g)
		{
			float pad = Theme.S(18f);
			Icons.Draw(g, "eye", new RectangleF(pad, Theme.S(16f), Theme.S(16f), Theme.S(16f)), Theme.Violet);
			Theme.DrawText(g, "VISTA PREVIA · " + f.PickedOverlayName().ToUpperInvariant(), Theme.Mono(7.5f), Theme.Muted, new RectangleF(pad + Theme.S(24f), Theme.S(14f), Width - pad * 2, Theme.S(20f)), StringAlignment.Near, StringAlignment.Center);
			float cw = Width - pad * 2;
			float ch = cw * 9f / 16f;
			RectangleF canvas = new RectangleF(pad, Theme.S(44f), cw, ch);
			// Fondo de cuadros (como OBS cuando la fuente es transparente).
			using (GraphicsPath clip = Theme.Round(canvas, Theme.S(10f)))
			{
				GraphicsState st = g.Save();
				g.SetClip(clip);
				g.Clear(Color.FromArgb(16, 16, 20));
				float cs = Math.Max(6f, cw / 48f);
				using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 24, 30)))
				{
					for (float yy = canvas.Y; yy < canvas.Bottom; yy += cs)
					{
						for (float xx = canvas.X + (((int)((yy - canvas.Y) / cs)) % 2) * cs; xx < canvas.Right; xx += cs * 2)
						{
							g.FillRectangle(b, xx, yy, cs, cs);
						}
					}
				}
				f.PaintOverlayMock(g, canvas);
				g.Restore(st);
				using (Pen pen = new Pen(Theme.Border))
				{
					g.DrawPath(pen, clip);
				}
			}
			Theme.DrawWrapped(g, "Así se verá en OBS (1920x1080) con tus colores y ajustes. Cambia algo a la izquierda y se ve al momento.", Theme.Body(8f), Theme.Muted, new RectangleF(pad, canvas.Bottom + Theme.S(12f), cw, Theme.S(36f)));
			open.Bounds = new Rectangle((int)pad, (int)(canvas.Bottom + Theme.S(54f)), Theme.S(210f), Theme.S(36f));
		}
	}

	// ======================= Ventana mini =======================
	internal class MiniWindow : Form
	{
		private readonly MainForm f;
		private readonly PaintBox box;
		private readonly VButton toggle, minus, plus, clip, back;

		public MiniWindow(MainForm owner)
		{
			f = owner;
			Text = "Vortexs Live";
			FormBorderStyle = FormBorderStyle.FixedToolWindow;
			TopMost = true;
			ShowInTaskbar = true;
			StartPosition = FormStartPosition.Manual;
			BackColor = Theme.Bg;
			ForeColor = Theme.Text;
			AutoScaleMode = AutoScaleMode.None;
			ClientSize = new Size(Theme.S(300f), Theme.S(138f));
			try { Icon = owner.Icon; } catch { }
			HandleCreated += delegate { Theme.DarkTitleBar(Handle); };
			box = new PaintBox { Painter = PaintMini };
			box.SetBounds(0, 0, ClientSize.Width, Theme.S(88f));
			Controls.Add(box);
			toggle = new VButton { Kind = ButtonKind.Pill, FontSize = 7.5f };
			minus = new VButton { Kind = ButtonKind.Ghost, Icon = "minus", Text = "5 s", FontSize = 8.5f };
			plus = new VButton { Kind = ButtonKind.Ghost, Icon = "plus", Text = "5 s", FontSize = 8.5f };
			clip = new VButton { Kind = ButtonKind.Ghost, Icon = "film", Text = "Clip", FontSize = 8.5f };
			back = new VButton { Kind = ButtonKind.Ghost, Icon = "expand", Text = "", FontSize = 8.5f };
			float y = 94f, h = 34f;
			Place(minus, 10f, y, 66f, h);
			Place(plus, 82f, y, 66f, h);
			Place(clip, 154f, y, 90f, h);
			Place(back, 250f, y, 40f, h);
			Place(toggle, 196f, 10f, 94f, 24f);
			toggle.Click += delegate { f.MiniToggleDelay(); RefreshMini(); };
			minus.Click += delegate { f.MiniNudge(-5); RefreshMini(); };
			plus.Click += delegate { f.MiniNudge(5); RefreshMini(); };
			clip.Click += delegate { f.MiniClip(); };
			back.Click += delegate { f.HideMini(); };
			// El dibujo va detrás de los botones (el de activar está encima de él).
			box.SendToBack();
			FormClosing += delegate(object s, FormClosingEventArgs e)
			{
				// Cerrar la mini vuelve a la ventana grande (la app sigue abierta).
				if (e.CloseReason == CloseReason.UserClosing)
				{
					e.Cancel = true;
					f.HideMini();
				}
			};
		}

		private void Place(Control c, float x, float y, float w, float h)
		{
			c.SetBounds(Theme.S(x), Theme.S(y), Theme.S(w), Theme.S(h));
			Controls.Add(c);
		}

		public void RefreshMini()
		{
			bool on = f.DelayOn;
			toggle.Text = on ? "ACTIVADO" : "DESACTIVADO";
			toggle.Selected = on;
			toggle.Invalidate();
			box.Invalidate();
		}

		private void PaintMini(Graphics g, Rectangle r)
		{
			Theme.Quality(g);
			float pad = Theme.S(14f);
			// Estado del directo.
			string live = f.LiveFor();
			bool isLive = f.IsLive;
			Color sc = isLive ? Theme.Red : Theme.Dim;
			using (SolidBrush b = new SolidBrush(sc))
			{
				g.FillEllipse(b, pad, Theme.S(17f), Theme.S(8f), Theme.S(8f));
			}
			Theme.DrawText(g, isLive ? ("EN DIRECTO" + (live != null ? " · " + live : "")) : "SIN DIRECTO", Theme.Mono(7f), isLive ? Theme.Mix(Theme.Red, Color.White, 0.3f) : Theme.Muted, new RectangleF(pad + Theme.S(14f), Theme.S(11f), Theme.S(170f), Theme.S(20f)), StringAlignment.Near, StringAlignment.Center);
			// Delay grande.
			string num = f.DelayShown.ToString();
			Font big = Theme.Display(26f);
			SizeF ns = Theme.Measure(g, num, big);
			RectangleF nr = new RectangleF(pad, Theme.S(36f), ns.Width, ns.Height);
			if (f.DelayOn)
			{
				using (LinearGradientBrush br = Theme.Brand(nr, true))
				{
					g.DrawString(num, big, br, pad, Theme.S(36f), StringFormat.GenericTypographic);
				}
			}
			else
			{
				using (SolidBrush br = new SolidBrush(Color.FromArgb(82, 82, 92)))
				{
					g.DrawString(num, big, br, pad, Theme.S(36f), StringFormat.GenericTypographic);
				}
			}
			Theme.DrawText(g, "s DE DELAY", Theme.Mono(7f), Theme.Muted, new RectangleF(pad + ns.Width + Theme.S(6f), Theme.S(50f), Theme.S(120f), Theme.S(20f)), StringAlignment.Near, StringAlignment.Center);
			int v = f.ViewersTotal();
			if (v >= 0)
			{
				Theme.DrawText(g, v.ToString("N0", CultureInfo.CurrentCulture), Theme.BodyBold(11f), Theme.Text, new RectangleF(0, Theme.S(44f), r.Width - pad - Theme.S(22f), Theme.S(24f)), StringAlignment.Far, StringAlignment.Center);
				Icons.Draw(g, "eye", new RectangleF(r.Width - pad - Theme.S(17f), Theme.S(48f), Theme.S(16f), Theme.S(16f)), Theme.Red);
			}
		}
	}
}
