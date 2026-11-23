// Moderación automática del chat conjunto (pestaña Chat → Moderación):
// - Palabras prohibidas (una por línea; * = lo que sea).
// - Spam: enlaces, mayúsculas, el mismo mensaje repetido y demasiados emotes.
// - Usuarios que no salen en el chat (bots conocidos y una lista propia).
// Lo que se filtra NUNCA llega a tu chat ni a la pantalla. Además, si se elige, se borra el
// mensaje o se expulsa/banea en la plataforma con tu cuenta de "Título" (TikTok no lo permite).
// Tú y tus moderadores nunca se moderan; los suscriptores y VIP, si así se elige, tampoco.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private static readonly string[] AmActions = new string[5] { "hide", "del", "t60", "t600", "ban" };

		private static readonly string[] AmActionNames = new string[5] { "Ocultar", "Borrar", "1 min", "10 min", "Ban" };

		private static readonly string[] AmActionDone = new string[5] { "oculto", "borrado", "expulsado 1 min", "expulsado 10 min", "baneado" };

		private readonly AutoModRules am = new AutoModRules();

		private readonly Dictionary<string, DateTime> amActed = new Dictionary<string, DateTime>();

		private void IgnoreUser(string user)
		{
			string u = (user ?? "").Trim().TrimStart('@').ToLowerInvariant();
			if (u.Length == 0)
			{
				return;
			}
			lock (am.Ignored)
			{
				am.Ignored.Add(u);
			}
			try
			{
				BeginInvoke((Action)delegate
				{
					SaveConfig();
				});
			}
			catch
			{
			}
		}

		// ¿Sale este mensaje en el chat? Si no, de paso se modera en la plataforma (si se eligió).
		// Se llama con cada cosa que llega del chat, antes de mandarla a la página.
		private bool AutoModAllows(ChatItem it)
		{
			int act;
			string reason = am.Check(it, out act);
			if (reason == null)
			{
				return true;
			}
			if (act >= 0)
			{
				Punish(it, AutoModRules.Plain(it), reason, act);
			}
			return false;
		}

		private void Punish(ChatItem it, string text, string reason, int act)
		{
			string plat = (it.p == "twitch") ? "Twitch" : ((it.p == "kick") ? "Kick" : ((it.p == "tiktok") ? "TikTok" : "YouTube"));
			act = Math.Max(0, Math.Min(4, act));
			// TikTok no deja moderar desde otros programas: allí solo se oculta.
			if (act == 0 || it.p == "tiktok")
			{
				LogLater("Moderación automática · " + plat + " · " + it.user + ": " + reason + " → oculto.");
				return;
			}
			// Como mucho una acción cada 10 s por persona (si escribe 5 mensajes seguidos, no 5 expulsiones).
			string key = it.p + "|" + (it.user ?? "").ToLowerInvariant();
			lock (amActed)
			{
				DateTime last;
				if (amActed.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(10.0) && act > 1)
				{
					return;
				}
				amActed[key] = DateTime.UtcNow;
			}
			Dictionary<string, object> d = new Dictionary<string, object>();
			d["p"] = it.p;
			d["a"] = AmActions[act];
			d["id"] = it.id ?? "";
			d["uid"] = it.uid ?? "";
			d["user"] = it.user ?? "";
			d["text"] = text;
			ThreadPool.QueueUserWorkItem(delegate
			{
				Dictionary<string, object> r = ModAction(d);
				object err;
				LogLater("Moderación automática · " + plat + " · " + it.user + ": " + reason + " → " + (r.TryGetValue("error", out err) ? ("oculto (no se pudo " + AmActionNames[act].ToLowerInvariant() + ": " + err + ")") : AmActionDone[act]) + ".");
			});
		}

		// ---------- Ventana "Moderación" ----------

		private void AutoModDialog()
		{
			Form f = DarkForm("Moderación", 460, 632);
			Card c1 = new Card
			{
				Title = "Moderación automática",
				Subtitle = "Twitch, Kick y YouTube. En TikTok solo se oculta."
			};
			Place(f, c1, 20f, 18f, 420f, 344f);
			Toggle tOn = new Toggle { Text = "Activada", Checked = am.On };
			Place(c1, tOn, 268f, 9f, 136f, 28f);
			Label(c1, "PALABRAS PROHIBIDAS · UNA POR LÍNEA · * = LO QUE SEA", 20f, 54f, 380f);
			Field fWords = new Field();
			fWords.Box.Multiline = true;
			fWords.Box.AcceptsReturn = true;
			fWords.Box.ScrollBars = ScrollBars.Vertical;
			fWords.Box.Text = am.Words;
			Place(c1, fWords, 20f, 70f, 380f, 56f);
			int wordAct = am.WordAct;
			int spamAct = am.SpamAct;
			Label(c1, "QUÉ HACER CON ESOS MENSAJES", 20f, 132f, 380f);
			VButton[] wp = new VButton[5];
			for (int i = 0; i < 5; i++)
			{
				int idx = i;
				wp[i] = new VButton { Kind = ButtonKind.Pill, Text = AmActionNames[i], Selected = i == wordAct, FontSize = 7.5f };
				Place(c1, wp[i], 20f + (float)i * 77f, 148f, 72f, 26f);
				wp[i].Click += delegate
				{
					wordAct = idx;
					for (int j = 0; j < 5; j++)
					{
						wp[j].Selected = j == idx;
						wp[j].Invalidate();
					}
				};
			}
			Label(c1, "SPAM", 20f, 182f, 380f);
			Toggle tLinks = new Toggle { Text = "Enlaces", Checked = am.Links };
			Toggle tCaps = new Toggle { Text = "Todo en mayúsculas", Checked = am.Caps };
			Toggle tRep = new Toggle { Text = "Repetir el mensaje", Checked = am.Repeat };
			Toggle tEm = new Toggle { Text = "Demasiados emotes", Checked = am.Emotes };
			Place(c1, tLinks, 18f, 198f, 186f, 28f);
			Place(c1, tCaps, 210f, 198f, 194f, 28f);
			Place(c1, tRep, 18f, 226f, 186f, 28f);
			Place(c1, tEm, 210f, 226f, 194f, 28f);
			Label(c1, "QUÉ HACER CON EL SPAM", 20f, 258f, 380f);
			VButton[] sp = new VButton[5];
			for (int i = 0; i < 5; i++)
			{
				int idx = i;
				sp[i] = new VButton { Kind = ButtonKind.Pill, Text = AmActionNames[i], Selected = i == spamAct, FontSize = 7.5f };
				Place(c1, sp[i], 20f + (float)i * 77f, 274f, 72f, 26f);
				sp[i].Click += delegate
				{
					spamAct = idx;
					for (int j = 0; j < 5; j++)
					{
						sp[j].Selected = j == idx;
						sp[j].Invalidate();
					}
				};
			}
			Toggle tSubs = new Toggle { Text = "No aplicar a suscriptores ni VIP", Checked = am.ExemptSubs };
			Place(c1, tSubs, 18f, 308f, 384f, 28f);

			Card c2 = new Card
			{
				Title = "No salen en el chat",
				Subtitle = "Ni en tu chat ni en pantalla."
			};
			Place(f, c2, 20f, 372f, 420f, 158f);
			Toggle tBots = new Toggle { Text = "Bots conocidos (Nightbot, StreamElements…)", Checked = am.HideBots };
			Place(c2, tBots, 18f, 50f, 384f, 28f);
			Label(c2, "USUARIOS · UNO POR LÍNEA", 20f, 84f, 380f);
			Field fUsers = new Field();
			fUsers.Box.Multiline = true;
			fUsers.Box.AcceptsReturn = true;
			fUsers.Box.ScrollBars = ScrollBars.Vertical;
			List<string> ig;
			lock (am.Ignored)
			{
				ig = new List<string>(am.Ignored);
			}
			ig.Sort();
			fUsers.Box.Text = string.Join("\r\n", ig.ToArray());
			Place(c2, fUsers, 20f, 100f, 380f, 48f);
			Box(f, 22f, 536f, 416f, 32f, delegate(Graphics g, Rectangle r)
			{
				Theme.DrawWrapped(g, "Tú y tus moderadores nunca se moderan. Borrar, expulsar y banear usan tu cuenta conectada en Directo → Título y categoría.", Theme.Body(7.75f), Theme.Muted, r);
			});
			VButton cancel = new VButton { Kind = ButtonKind.Ghost, Text = "Cancelar", FontSize = 10f };
			VButton save = new VButton { Kind = ButtonKind.Primary, Text = "Guardar", FontSize = 10f };
			Place(f, cancel, 20f, 574f, 130f, 42f);
			Place(f, save, 160f, 574f, 280f, 42f);
			cancel.Click += delegate
			{
				f.Close();
			};
			save.Click += delegate
			{
				am.On = tOn.Checked;
				am.Words = fWords.Box.Text.Trim();
				am.WordAct = wordAct;
				am.SpamAct = spamAct;
				am.Links = tLinks.Checked;
				am.Caps = tCaps.Checked;
				am.Repeat = tRep.Checked;
				am.Emotes = tEm.Checked;
				am.ExemptSubs = tSubs.Checked;
				am.HideBots = tBots.Checked;
				lock (am.Ignored)
				{
					am.Ignored.Clear();
					foreach (string line in fUsers.Box.Text.Replace("\r", "").Split('\n'))
					{
						string u = line.Trim().TrimStart('@').ToLowerInvariant();
						if (u.Length > 0)
						{
							am.Ignored.Add(u);
						}
					}
				}
				SaveConfig();
				AppendLog("Moderación automática " + (am.On ? "activada" : "desactivada") + ".");
				f.Close();
			};
			f.ShowDialog(this);
		}

		// ---------- Configuración ----------

		private static string B64(string s)
		{
			return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
		}

		private static string UnB64(string s)
		{
			try
			{
				return Encoding.UTF8.GetString(Convert.FromBase64String(s ?? ""));
			}
			catch
			{
				return "";
			}
		}

		private void LoadAutoModConfig(Dictionary<string, string> raw)
		{
			string v;
			int n;
			if (raw.TryGetValue("am_on", out v))
			{
				am.On = v == "1";
			}
			if (raw.TryGetValue("am_words", out v))
			{
				am.Words = UnB64(v);
			}
			if (raw.TryGetValue("am_word_act", out v) && int.TryParse(v, out n))
			{
				am.WordAct = Math.Max(0, Math.Min(4, n));
			}
			if (raw.TryGetValue("am_spam_act", out v) && int.TryParse(v, out n))
			{
				am.SpamAct = Math.Max(0, Math.Min(4, n));
			}
			if (raw.TryGetValue("am_spam", out v) && v.Length == 4)
			{
				am.Links = v[0] == '1';
				am.Caps = v[1] == '1';
				am.Repeat = v[2] == '1';
				am.Emotes = v[3] == '1';
			}
			if (raw.TryGetValue("am_subs", out v))
			{
				am.ExemptSubs = v != "0";
			}
			if (raw.TryGetValue("am_bots", out v))
			{
				am.HideBots = v != "0";
			}
			if (raw.TryGetValue("am_users", out v))
			{
				lock (am.Ignored)
				{
					am.Ignored.Clear();
					foreach (string u in UnB64(v).Split('\n'))
					{
						if (u.Trim().Length > 0)
						{
							am.Ignored.Add(u.Trim().ToLowerInvariant());
						}
					}
				}
			}
		}

		private void SaveAutoModConfig(List<string> lines)
		{
			lines.Add("am_on=" + (am.On ? "1" : "0"));
			lines.Add("am_words=" + B64(am.Words));
			lines.Add("am_word_act=" + am.WordAct);
			lines.Add("am_spam_act=" + am.SpamAct);
			lines.Add("am_spam=" + (am.Links ? "1" : "0") + (am.Caps ? "1" : "0") + (am.Repeat ? "1" : "0") + (am.Emotes ? "1" : "0"));
			lines.Add("am_subs=" + (am.ExemptSubs ? "1" : "0"));
			lines.Add("am_bots=" + (am.HideBots ? "1" : "0"));
			string users;
			lock (am.Ignored)
			{
				users = string.Join("\n", new List<string>(am.Ignored).ToArray());
			}
			lines.Add("am_users=" + B64(users));
		}
	}
}
