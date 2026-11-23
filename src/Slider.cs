// Barra del delay (0–300 s). Va en escala de raíz cuadrada: los primeros segundos ocupan más
// sitio y se ajustan con más precisión que los minutos. Con marcas en 30 s, 1 y 2 min.
// Se mueve con el ratón, la rueda y las flechas (RePág/AvPág: ±10 s).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace StreamDelay
{
	internal class Slider : VControl
	{
		private int value;

		private int max = 300;

		private bool dragging;

		public int Maximum
		{
			get
			{
				return max;
			}
			set
			{
				max = Math.Max(1, value);
				Invalidate();
			}
		}

		public int Value
		{
			get
			{
				return value;
			}
			set
			{
				int clamped = Math.Max(0, Math.Min(max, value));
				if (clamped != this.value)
				{
					this.value = clamped;
					Invalidate();
					if (this.ValueChanged != null)
					{
						this.ValueChanged(this, EventArgs.Empty);
					}
				}
			}
		}

		// Margen a cada lado (para que quepa el círculo en los extremos).
		private float Pad
		{
			get
			{
				return Theme.S(11f);
			}
		}

		public event EventHandler ValueChanged;

		public Slider()
		{
			Cursor = Cursors.Hand;
			base.TabStop = true;
		}

		// Valor → posición (0 a 1) y al revés.
		private float PosOf(int v)
		{
			return (float)Math.Sqrt((double)v / (double)max);
		}

		private int ValOf(float pos)
		{
			pos = Math.Max(0f, Math.Min(1f, pos));
			return (int)Math.Round(pos * pos * (float)max);
		}

		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button == MouseButtons.Left)
			{
				Focus();
				dragging = true;
				Track(e.X);
			}
		}

		protected override void OnMouseMove(MouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (dragging)
			{
				Track(e.X);
			}
		}

		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			dragging = false;
		}

		private void Track(int x)
		{
			Value = ValOf(((float)x - Pad) / ((float)base.Width - 2f * Pad));
		}

		protected override void OnMouseWheel(MouseEventArgs e)
		{
			base.OnMouseWheel(e);
			Value += ((e.Delta > 0) ? 1 : (-1));
		}

		// Las flechas son para la barra (si no, Windows las usaría para pasar a otro control).
		protected override bool IsInputKey(Keys k)
		{
			if (k != Keys.Left && k != Keys.Right && k != Keys.Up && k != Keys.Down)
			{
				return base.IsInputKey(k);
			}
			return true;
		}

		protected override void OnKeyDown(KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
			{
				Value--;
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
			{
				Value++;
				e.Handled = true;
			}
			else if (e.KeyCode == Keys.PageUp)
			{
				Value += 10;
			}
			else if (e.KeyCode == Keys.Next)
			{
				// Next = AvPág.
				Value -= 10;
			}
			base.OnKeyDown(e);
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			Graphics graphics = e.Graphics;
			Theme.Quality(graphics);
			float middle = (float)base.Height / 2f;
			float thickness = Theme.S(6f);
			// Carril.
			RectangleF rail = new RectangleF(Pad, middle - thickness / 2f, (float)base.Width - 2f * Pad, thickness);
			using (GraphicsPath path = Theme.Round(rail, thickness / 2f))
			{
				using (SolidBrush brush = new SolidBrush(Color.FromArgb(38, 38, 48)))
				{
					graphics.FillPath(brush, path);
				}
			}
			float knobX = Pad + PosOf(value) * ((float)base.Width - 2f * Pad);
			// Marcas en 30 s, 1 min, 2 min y 3 min.
			foreach (int v in new int[4] { 30, 60, 120, 180 })
			{
				float markX = Pad + PosOf(v) * ((float)base.Width - 2f * Pad);
				using (SolidBrush brush2 = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
				{
					graphics.FillEllipse(brush2, markX - 1.5f, middle - 1.5f, 3f, 3f);
				}
			}
			// Parte rellena, con el degradado de la marca a lo ancho de toda la barra.
			if (knobX > Pad + 1f)
			{
				RectangleF filled = new RectangleF(Pad, middle - thickness / 2f, knobX - Pad, thickness);
				using (GraphicsPath path2 = Theme.Round(filled, thickness / 2f))
				{
					using (LinearGradientBrush brush3 = Theme.Brand(new RectangleF(Pad, 0f, (float)base.Width - 2f * Pad, base.Height), false))
					{
						graphics.FillPath(brush3, path2);
					}
				}
			}
			// Círculo (con brillo al pasar el ratón, arrastrar o tener el foco).
			float knob = Theme.S(18f);
			if (hover || dragging || Focused)
			{
				Card.Glow(graphics, new RectangleF(knobX - knob, middle - knob, 2f * knob, 2f * knob), Color.FromArgb(90, Theme.Violet));
			}
			using (SolidBrush brush4 = new SolidBrush(Theme.Text))
			{
				graphics.FillEllipse(brush4, knobX - knob / 2f, middle - knob / 2f, knob, knob);
			}
			using (Pen pen = new Pen(Theme.Violet, 2f))
			{
				graphics.DrawEllipse(pen, knobX - knob / 2f, middle - knob / 2f, knob, knob);
			}
		}
	}
}
