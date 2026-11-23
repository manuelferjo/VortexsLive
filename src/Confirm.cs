// Confirmar sin ventanas: el primer clic deja el botón en rojo pidiendo "Pulsa otra vez…" durante
// 3 segundos; el segundo hace la acción. Si no se pulsa, el botón vuelve a como estaba.
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private readonly Dictionary<VButton, DateTime> armedUntil = new Dictionary<VButton, DateTime>();

		private readonly Dictionary<VButton, string> armedText = new Dictionary<VButton, string>();

		private readonly Dictionary<VButton, ButtonKind> armedKind = new Dictionary<VButton, ButtonKind>();

		private bool IsArmed(VButton b)
		{
			DateTime until;
			return b != null && armedUntil.TryGetValue(b, out until) && DateTime.UtcNow < until;
		}

		private void Disarm(VButton b)
		{
			if (!armedUntil.ContainsKey(b))
			{
				return;
			}
			armedUntil.Remove(b);
			b.Text = armedText[b];
			b.Kind = armedKind[b];
			armedText.Remove(b);
			armedKind.Remove(b);
			b.Invalidate();
		}

		private void ConfirmClick(VButton b, string ask, Action act)
		{
			if (IsArmed(b))
			{
				Disarm(b);
				act();
				return;
			}
			Disarm(b);
			armedText[b] = b.Text;
			armedKind[b] = b.Kind;
			armedUntil[b] = DateTime.UtcNow.AddSeconds(3.0);
			b.Text = ask;
			b.Kind = ButtonKind.Danger;
			b.Invalidate();
			Timer t = new Timer
			{
				Interval = 3100
			};
			t.Tick += delegate
			{
				t.Stop();
				t.Dispose();
				if (armedUntil.ContainsKey(b) && !IsArmed(b))
				{
					Disarm(b);
					RefreshStatus();
				}
			};
			t.Start();
		}
	}
}
