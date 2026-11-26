// Novedades: la primera vez que se abre una versión nueva sale una ventana corta con lo que ha
// cambiado para el usuario (nunca en pleno directo ni la primera vez que se instala).
// Al sacar una versión, añade ARRIBA de ReleaseNews su entrada: frases cortas, para el usuario.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		// Versión → cambios (de la más nueva a la más vieja).
		private static readonly string[][] ReleaseNews = new string[5][]
		{
			new string[2] { "4.30", "• Al abrir una versión nueva te contamos aquí, en pocas líneas, qué ha cambiado.\n• Anuncio: lee bien el título de tu directo aunque no tengas Twitch conectado.\n• Prueba sin publicar: la captura es solo del primer post (el del título)." },
			new string[2] { "4.29", "• Anuncio: si Twitch no está conectado, lee el título de tu página de Twitch y se queda con la parte buena (sin emojis ni !comandos).\n• Prueba sin publicar: el post del título sale resaltado en la captura." },
			new string[2] { "4.28", "• Anuncio: la captura es solo de tu post, sin cortes ni barra lateral." },
			new string[2] { "4.27", "• Anuncio: arreglado el hilo de X (el +).\n• Los errores de Telegram dicen qué hacer." },
			new string[2] { "4.26", "• Nuevo (plan Completo): Ajustes → Anuncio. Al empezar en Twitch publica en X tu título con el enlace en el hilo y te prepara las historias de TikTok e Instagram." }
		};

		private const string ReleasesUrl = "https://github.com/manuelferjo/VortexsLive/releases";

		// Última versión cuyas novedades ya se vieron ("" = de antes de que existiera esta ventana).
		private string newsSeen = "";

		private bool newsDone;

		// Unos segundos después de abrir, para no pisar el arranque ni otros avisos.
		private DateTime newsAfter = DateTime.UtcNow.AddSeconds(5.0);

		private static int CompareVersion(string a, string b)
		{
			string[] pa = (a ?? "").Split('.');
			string[] pb = (b ?? "").Split('.');
			for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
			{
				int x = 0;
				int y = 0;
				if (i < pa.Length)
				{
					int.TryParse(pa[i], out x);
				}
				if (i < pb.Length)
				{
					int.TryParse(pb[i], out y);
				}
				if (x != y)
				{
					return x.CompareTo(y);
				}
			}
			return 0;
		}

		// Texto con las novedades desde la última vista hasta esta (como mucho 4 versiones).
		private static string NewsSince(string seen)
		{
			StringBuilder sb = new StringBuilder();
			int count = 0;
			bool more = false;
			foreach (string[] n in ReleaseNews)
			{
				if (CompareVersion(n[0], Version) > 0)
				{
					continue;
				}
				// Sin versión vista (se actualiza desde una sin esta ventana): solo la actual.
				if (seen.Length == 0 ? (count >= 1) : (CompareVersion(n[0], seen) <= 0))
				{
					break;
				}
				if (count >= 4)
				{
					more = true;
					break;
				}
				if (sb.Length > 0)
				{
					sb.Append("\n\n");
				}
				if (count > 0 || seen.Length > 0 && CompareVersion(ReleaseNews[0][0], seen) > 0)
				{
					sb.Append(n[0]).Append(":\n");
				}
				sb.Append(n[1]);
				count++;
			}
			if (more)
			{
				sb.Append("\n\n… y más en la web.");
			}
			return sb.ToString();
		}

		// Desde el temporizador: enseña las novedades cuando se pueda.
		private void NewsTick()
		{
			if (newsDone || DateTime.UtcNow < newsAfter)
			{
				return;
			}
			if (firstRun)
			{
				newsDone = true;
				newsSeen = Version;
				return;
			}
			if (!wizardDone || licNoticeOpen || engine.OutputActive || !base.Visible || base.OwnedForms.Length > 0)
			{
				return;
			}
			newsDone = true;
			if (newsSeen == Version)
			{
				return;
			}
			string text = NewsSince(newsSeen);
			newsSeen = Version;
			SaveConfig();
			if (text.Length > 0)
			{
				NewsDialog(text);
			}
		}

		private void NewsDialog(string text)
		{
			licNoticeOpen = true;
			try
			{
				Form f = DarkForm("Novedades", 440, 380);
				Box(f, 24f, 20f, 392f, 280f, delegate(Graphics g, Rectangle r)
				{
					Card.Glow(g, new RectangleF(-r.Width * 0.2f, -r.Height * 0.4f, r.Width * 0.9f, r.Height * 0.8f), Color.FromArgb(40, Theme.Yellow));
					Theme.DrawText(g, "VORTEXS LIVE " + Version, Theme.Mono(7.5f), Theme.Violet, new RectangleF(0f, 0f, r.Width, S(18f)), StringAlignment.Near, StringAlignment.Center);
					Theme.DrawText(g, "Novedades de la " + Version, Theme.Display(16f), Theme.Text, new RectangleF(0f, S(20f), r.Width, S(34f)), StringAlignment.Near, StringAlignment.Center);
					Theme.DrawWrapped(g, text, Theme.Body(9f), Theme.Muted, new RectangleF(0f, S(64f), r.Width, r.Height - S(64f)));
				});
				VButton web = new VButton { Kind = ButtonKind.Ghost, Text = "Ver todas en la web", FontSize = 9f };
				VButton ok = new VButton { Kind = ButtonKind.Primary, Text = "¡Entendido!", FontSize = 10f };
				Place(f, web, 24f, 312f, 210f, 44f);
				Place(f, ok, 244f, 312f, 172f, 44f);
				web.Click += delegate
				{
					Http.OpenBrowser(ReleasesUrl);
					f.Close();
				};
				ok.Click += delegate
				{
					f.Close();
				};
				f.ShowDialog(this);
				f.Dispose();
			}
			finally
			{
				licNoticeOpen = false;
			}
		}

		private void LoadNewsConfig(Dictionary<string, string> raw)
		{
			string v;
			newsSeen = raw.TryGetValue("news_seen", out v) ? v.Trim() : "";
		}

		private void SaveNewsConfig(List<string> lines)
		{
			lines.Add("news_seen=" + newsSeen);
		}
	}
}
