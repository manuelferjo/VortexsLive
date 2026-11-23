// Atajos de teclado a la vista: cada botón dice el suyo al pasar el ratón y la tecla ? (o F1)
// abre una lista con todos encima de la página. Esc, ? o un clic la cierran.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private PaintBox keysSheet;

		// { teclas, qué hace }
		private static readonly string[][] ShortcutList = new string[][]
		{
			new string[] { "Ctrl+Alt+D", "Activar o desactivar el delay" },
			new string[] { "Ctrl+Alt+↑ / ↓", "Subir o bajar 5 s el delay" },
			new string[] { "Ctrl+Alt+C", "Clip de los últimos 60 s" },
			new string[] { "Ctrl+Alt+Mayús+1 … 6", "Delays guardados 1 a 6" },
			new string[] { "Ctrl+Alt+Mayús+A", "Activar el delay" },
			new string[] { "Ctrl+Alt+Mayús+Q", "Quitar el delay" },
			new string[] { "Ctrl+Alt+Mayús+X", "Cortar ya" },
			new string[] { "?  o  F1", "Esta lista" }
		};

		[DllImport("user32.dll")]
		private static extern IntPtr GetFocus();

		// Botones con su atajo al pasar el ratón, y la lista de atajos (oculta).
		private void BuildShortcuts()
		{
			KeyPreview = true;
			tip.SetToolTip(tgDelay, I18n.Tr("Activar o desactivar el delay") + " · Ctrl+Alt+D");
			tip.SetToolTip(slider, I18n.Tr("Arrastra y pulsa Aplicar · Ctrl+Alt+↑ / ↓ cambia 5 s al momento"));
			tip.SetToolTip(btnCut, I18n.Tr("Vacía el delay y corta en las plataformas") + " · Ctrl+Alt+Mayús+X");
			for (int i = 0; i < presets.Length; i++)
			{
				tip.SetToolTip(presets[i], "Ctrl+Alt+Mayús+" + (i + 1));
			}
			keysSheet = new PaintBox
			{
				Painter = PaintKeysSheet,
				Visible = false,
				Cursor = Cursors.Hand
			};
			keysSheet.SetBounds(0, S(SubRowTop - 8), S(460f), S(WinH - (SubRowTop - 8)));
			Controls.Add(keysSheet);
			keysSheet.Click += delegate
			{
				ToggleKeysSheet();
			};
			VButton wizBtn = new VButton { Kind = ButtonKind.Ghost, Text = "Abrir el asistente de inicio", FontSize = 9f, Icon = "info" };
			Place(keysSheet, wizBtn, 20f, (float)(WinH - (SubRowTop - 8)) - 58f, 420f, 40f);
			wizBtn.Click += delegate
			{
				OpenWizard();
			};
		}

		private void ToggleKeysSheet()
		{
			if (keysSheet == null)
			{
				return;
			}
			keysSheet.Visible = !keysSheet.Visible;
			if (keysSheet.Visible)
			{
				keysSheet.BringToFront();
				keysSheet.Invalidate();
			}
		}

		// ¿Se está escribiendo en una caja de texto? (entonces ? es un carácter, no el atajo)
		private static bool TypingInTextBox()
		{
			Control c = Control.FromHandle(GetFocus());
			return c is TextBoxBase || c is ComboBox;
		}

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if (keyData == Keys.F1)
			{
				ToggleKeysSheet();
				return true;
			}
			if (keyData == Keys.Escape && keysSheet != null && keysSheet.Visible)
			{
				ToggleKeysSheet();
				return true;
			}
			return base.ProcessCmdKey(ref msg, keyData);
		}

		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			if (e.KeyChar == '?' && !TypingInTextBox())
			{
				ToggleKeysSheet();
				e.Handled = true;
				return;
			}
			base.OnKeyPress(e);
		}

		private void PaintKeysSheet(Graphics g, Rectangle r)
		{
			Theme.Quality(g);
			using (SolidBrush bg = new SolidBrush(Theme.Bg))
			{
				g.FillRectangle(bg, r);
			}
			RectangleF card = new RectangleF(S(20f), S(8f), r.Width - S(40f), S(70f) + ShortcutList.Length * S(40f));
			using (GraphicsPath path = Theme.Round(card, S(16f)))
			{
				using (SolidBrush b = new SolidBrush(Theme.Surface))
				{
					g.FillPath(b, path);
				}
				using (Pen pen = new Pen(Theme.BorderHi))
				{
					g.DrawPath(pen, path);
				}
			}
			Theme.DrawText(g, "ATAJOS DE TECLADO", Theme.Mono(8f), Theme.Muted, new RectangleF(card.X + S(20f), card.Y + S(14f), card.Width - S(40f), S(20f)), StringAlignment.Near, StringAlignment.Center);
			Theme.DrawText(g, "Funcionan aunque estés en OBS o en el juego", Theme.Body(8f), Theme.Dim, new RectangleF(card.X + S(20f), card.Y + S(34f), card.Width - S(40f), S(20f)), StringAlignment.Near, StringAlignment.Center);
			float y = card.Y + S(64f);
			Font keyFont = Theme.Mono(8.5f);
			foreach (string[] k in ShortcutList)
			{
				string keys = I18n.English ? k[0].Replace("Mayús", "Shift").Replace(" o ", " or ") : k[0];
				float kw = Theme.Measure(g, keys, keyFont).Width + S(18f);
				RectangleF key = new RectangleF(card.X + S(20f), y + S(4f), kw, S(28f));
				using (GraphicsPath kp = Theme.Round(key, S(7f)))
				using (SolidBrush kb = new SolidBrush(Theme.Input))
				using (Pen kpen = new Pen(Theme.Border))
				{
					g.FillPath(kb, kp);
					g.DrawPath(kpen, kp);
				}
				Theme.DrawText(g, keys, keyFont, Theme.VioletLight, key, StringAlignment.Center, StringAlignment.Center);
				float tx = card.X + S(206f);
				Theme.DrawText(g, k[1], Theme.Body(9f), Theme.Text, new RectangleF(tx, y, card.Right - tx - S(16f), S(36f)), StringAlignment.Near, StringAlignment.Center);
				y += S(40f);
			}
			Theme.DrawText(g, "Pulsa ?, Esc o en cualquier sitio para cerrar", Theme.Body(8f), Theme.Dim, new RectangleF(0f, card.Bottom + S(10f), r.Width, S(20f)), StringAlignment.Center, StringAlignment.Center);
		}
	}
}
