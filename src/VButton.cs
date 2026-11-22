// Botón de la ventana. Kind decide el aspecto: Primary (degradado de la marca), Ghost (gris),
// Danger (rojo, "Cortar ya"), Pill (pastilla pequeña, Selected = elegida) y Link (solo texto).
// Muted: Primary apagado (por ejemplo "Delay aplicado").
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class VButton : VControl
	{
		public ButtonKind Kind = ButtonKind.Ghost;

		public bool Selected;

		public bool Muted;

		public float FontSize = 9.5f;

		// Icono de línea (Icons.cs) a la izquierda del texto; null = sin icono.
		public string Icon;

		public VButton()
		{
			Cursor = Cursors.Hand;
			base.TabStop = true;
		}

		protected override void OnTextChanged(EventArgs e)
		{
			Invalidate();
			base.OnTextChanged(e);
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
			{
				OnClick(EventArgs.Empty);
				e.Handled = true;
			}
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			RectangleF rect = new RectangleF(0.5f, 0.5f, (float)base.Width - 1.5f, (float)base.Height - 1.5f);
			// Al pulsarlo se encoge un poco (con transición).
			if (pressT > 0f)
			{
				float shrink = 1.5f * Ease(pressT);
				rect.Inflate(0f - shrink, 0f - shrink);
			}
			float h = Ease(hoverT);
			float radius = ((Kind == ButtonKind.Pill) ? (rect.Height / 2f) : ((float)Theme.S(12f)));
			Color c = Theme.Text;
			Font f = ((Kind == ButtonKind.Pill) ? Theme.Mono(FontSize) : Theme.BodyBold(FontSize));
			// Un botón rojo desactivado se ve como uno gris.
			ButtonKind look = ((!base.Enabled && (Kind == ButtonKind.Danger || Kind == ButtonKind.Ghost)) ? ButtonKind.Ghost : Kind);
			using (GraphicsPath path = Theme.Round(rect, radius))
			{
				switch (look)
				{
				case ButtonKind.Primary:
				{
					if (!base.Enabled || Muted)
					{
						using (SolidBrush brush5 = new SolidBrush(Theme.Elevated))
						{
							graphics.FillPath(brush5, path);
						}
						using (Pen pen5 = new Pen(Theme.Border))
						{
							graphics.DrawPath(pen5, path);
						}
						c = (base.Enabled ? Theme.Muted : Theme.Dim);
						break;
					}
					if (h > 0.01f)
					{
						// Halo violeta de tres capas alrededor (aparece poco a poco).
						for (int ring = 3; ring >= 1; ring--)
						{
							RectangleF r = RectangleF.Inflate(rect, ring * 2, ring * 2);
							using (GraphicsPath path2 = Theme.Round(r, radius + (float)(ring * 2)))
							{
								using (SolidBrush brush6 = new SolidBrush(Color.FromArgb((int)(18f * h), Theme.Violet)))
								{
									graphics.FillPath(brush6, path2);
								}
							}
						}
					}
					using (LinearGradientBrush brush7 = Theme.Brand(rect, false))
					{
						graphics.FillPath(brush7, path);
					}
					c = Color.Black;
					break;
				}
				case ButtonKind.Danger:
				{
					using (SolidBrush brush2 = new SolidBrush(OnHover(Color.FromArgb(18, Theme.Red), Color.FromArgb(40, Theme.Red))))
					{
						graphics.FillPath(brush2, path);
					}
					using (Pen pen2 = new Pen(Color.FromArgb((int)(150f + 80f * h), Theme.Red), 1.2f))
					{
						graphics.DrawPath(pen2, path);
					}
					c = Color.FromArgb(255, 120, 130);
					break;
				}
				case ButtonKind.Pill:
					if (Selected)
					{
						using (SolidBrush brush3 = new SolidBrush(Color.FromArgb(base.Enabled ? 38 : 20, Theme.Violet)))
						{
							graphics.FillPath(brush3, path);
						}
						using (Pen pen3 = new Pen(Color.FromArgb(base.Enabled ? 200 : 90, Theme.Violet), 1.2f))
						{
							graphics.DrawPath(pen3, path);
						}
						c = (base.Enabled ? Theme.VioletLight : Theme.Mix(Theme.VioletLight, Theme.Bg, 0.4f));
					}
					else
					{
						using (SolidBrush brush4 = new SolidBrush(OnHover(Theme.Input, Theme.Elevated)))
						{
							graphics.FillPath(brush4, path);
						}
						using (Pen pen4 = new Pen(OnHover(Theme.Border, Theme.BorderHi)))
						{
							graphics.DrawPath(pen4, path);
						}
						c = ((!base.Enabled) ? Theme.Dim : OnHover(Theme.Muted, Theme.Text));
					}
					break;
				case ButtonKind.Link:
					c = OnHover(Theme.Violet, Theme.VioletLight);
					f = Theme.BodySemi(FontSize);
					break;
				default:
				{
					using (SolidBrush brush = new SolidBrush(OnHover(Theme.Elevated, Color.FromArgb(32, 32, 42))))
					{
						graphics.FillPath(brush, path);
					}
					using (Pen pen = new Pen(OnHover(Theme.Border, Theme.BorderHi)))
					{
						graphics.DrawPath(pen, path);
					}
					if (!base.Enabled)
					{
						c = Theme.Dim;
					}
					break;
				}
				}
				// Borde de puntos al llegar con el tabulador.
				if (Focused && ShowFocusCues && Kind != ButtonKind.Link)
				{
					using (Pen focusPen = new Pen(Color.FromArgb(160, Theme.Violet), 1.5f)
					{
						DashStyle = DashStyle.Dot
					})
					{
						RectangleF r2 = RectangleF.Inflate(rect, -3f, -3f);
						using (GraphicsPath path3 = Theme.Round(r2, Math.Max(2f, radius - 3f)))
						{
							graphics.DrawPath(focusPen, path3);
						}
					}
				}
			}
			// Con icono: icono + texto, centrados juntos.
			if (!string.IsNullOrEmpty(Icon) && Kind != ButtonKind.Link)
			{
				string label = I18n.Tr(Text ?? "");
				float isz = f.GetHeight(graphics) * 1.05f;
				float gap = label.Length > 0 ? isz * 0.42f : 0f;
				float tw = label.Length > 0 ? Theme.Measure(graphics, label, f).Width : 0f;
				float x0 = rect.X + (rect.Width - (isz + gap + tw)) / 2f;
				Icons.Draw(graphics, Icon, new RectangleF(x0, rect.Y + (rect.Height - isz) / 2f, isz, isz), c);
				if (label.Length > 0)
				{
					// Hasta el borde del botón: la medida puede quedarse algo corta y no se debe cortar.
					float tx = x0 + isz + gap;
					Theme.DrawText(graphics, label, f, c, new RectangleF(tx, rect.Y, Math.Max(tw + 4f, rect.Right - tx - 2f), rect.Height), StringAlignment.Near, StringAlignment.Center);
				}
				return;
			}
			// Los enlaces van alineados a la derecha; el resto, centrados.
			Theme.DrawText(graphics, Text, f, c, rect, (Kind != ButtonKind.Link) ? StringAlignment.Center : StringAlignment.Far, StringAlignment.Center);
		}
	}
}
