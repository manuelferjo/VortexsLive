using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	// Comandos del chat (!delay, !web…) y moderación desde la página del chat conjunto.
	internal partial class MainForm
	{
		private bool cmdOn = true;

		private string cmdText = "!delay = ⏱️ {delay}\r\n!web = 🌐 www.vortexs.es";

		private const int CmdCooldownSec = 20;

		private readonly Dictionary<string, DateTime> cmdLast = new Dictionary<string, DateTime>();

		private DateTime chatStartedAt = DateTime.MinValue;

		private Dictionary<string, string> ParseCommands()
		{
			Dictionary<string, string> d = new Dictionary<string, string>();
			foreach (string line in cmdText.Replace("\r", "").Split('\n'))
			{
				int eq = line.IndexOf('=');
				if (eq <= 0)
				{
					continue;
				}
				string name = line.Substring(0, eq).Trim().ToLowerInvariant();
				string reply = line.Substring(eq + 1).Trim();
				if (name.Length < 2 || reply.Length == 0)
				{
					continue;
				}
				if (!name.StartsWith("!"))
				{
					name = "!" + name;
				}
				d[name] = reply;
			}
			return d;
		}

		private static string PlainText(ChatItem it)
		{
			StringBuilder sb = new StringBuilder();
			if (it.parts != null)
			{
				foreach (ChatPart p in it.parts)
				{
					sb.Append(p.e != null ? p.n : p.x);
				}
			}
			return sb.ToString().Trim();
		}

		private string DelayPhrase()
		{
			if (engine.Enabled && engine.TargetMs > 0)
			{
				return I18n.Tr("El directo va con ") + engine.TargetMs / 1000 + I18n.Tr(" s de delay.");
			}
			return I18n.Tr("Ahora mismo no hay delay: el directo va en tiempo real.");
		}

		// Cada mensaje del chat pasa por aquí (hilo del chat).
		private void OnChatItem(ChatItem it)
		{
			if (!cmdOn || it == null || it.t != "msg" || it.p == null)
			{
				return;
			}
			// Al conectar, Kick y YouTube mandan los últimos mensajes: no se contestan.
			if (DateTime.UtcNow - chatStartedAt < TimeSpan.FromSeconds(8.0))
			{
				return;
			}
			string text = PlainText(it);
			if (!text.StartsWith("!"))
			{
				return;
			}
			int sp = text.IndexOf(' ');
			string cmd = ((sp < 0) ? text : text.Substring(0, sp)).ToLowerInvariant();
			if (cmd == ClipCommand)
			{
				// Comando fijo: guarda los últimos 60 s en tu PC (el dueño del canal y a quien él permita).
				ChatClip(it, (it.p == "twitch") ? "Twitch" : ((it.p == "kick") ? "Kick" : ((it.p == "tiktok") ? "TikTok" : "YouTube")));
				return;
			}
			// TikTok no deja escribir en el chat desde otros programas: no se puede contestar.
			if (it.p == "tiktok")
			{
				return;
			}
			string reply;
			if (!ParseCommands().TryGetValue(cmd, out reply))
			{
				return;
			}
			string key = it.p + cmd;
			lock (cmdLast)
			{
				DateTime last;
				if (cmdLast.TryGetValue(key, out last) && DateTime.UtcNow - last < TimeSpan.FromSeconds(CmdCooldownSec))
				{
					return;
				}
				cmdLast[key] = DateTime.UtcNow;
			}
			string plat = (it.p == "twitch") ? "Twitch" : ((it.p == "kick") ? "Kick" : "YouTube");
			string msg = reply.Replace("{delay}", DelayPhrase()).Replace("{s}", (engine.TargetMs / 1000).ToString()).Replace("{user}", it.user ?? "").Replace("{plataforma}", plat);
			if (msg.StartsWith("!"))
			{
				msg = "→ " + msg;
			}
			string target = it.p;
			ThreadPool.QueueUserWorkItem(delegate
			{
				try
				{
					Dictionary<string, object> res = ChatSendAll(msg, new string[1] { target });
					object v;
					if (res.TryGetValue(target, out v) && Convert.ToString(v) != "ok")
					{
						LogLater("Comando " + cmd + " en " + plat + ": no se pudo contestar (" + v + ").");
					}
				}
				catch (Exception ex)
				{
					LogLater("Comando " + cmd + ": " + ex.Message);
				}
			});
		}

		// ---------- Moderación ----------

		// Acciones de moderación (página del chat y moderación automática).
		//   del · t60 · t600 · t3600 · t86400 · ban · unban   → en la plataforma, con tu cuenta de "Título"
		//   hide (quitar el mensaje solo de tu chat y de la pantalla) · ignore (no enseñar más a ese usuario)
		//   modes (leer los modos del chat de Twitch) · setmode (k, v) · clear (borrar todo el chat)
		private Dictionary<string, object> ModAction(Dictionary<string, object> d)
		{
			Dictionary<string, object> res = new Dictionary<string, object>();
			string p = J.S(d, "p");
			string a = J.S(d, "a");
			string id = J.S(d, "id");
			string uid = J.S(d, "uid");
			string user = J.S(d, "user");
			string text = J.S(d, "text");
			string twCh = "";
			string kcCh = "";
			try
			{
				Invoke((Action)delegate
				{
					twCh = ChatHub.Clean(fldChatTw.Box.Text);
					kcCh = ChatHub.Clean(fldChatKick.Box.Text);
				});
			}
			catch
			{
			}

			// ---- Overlays: mensaje destacado y encuesta ----
			if (a == "feature" || a == "unfeature" || a == "poll")
			{
				return WidgetMod(d, a);
			}

			// ---- Solo en tu chat y en la pantalla (vale también para TikTok) ----
			if (a == "hide")
			{
				chatServer.Send(new ChatItem { t = "del", p = p, id = id, uid = (id.Length == 0) ? uid : null });
				res["ok"] = true;
				res["msg"] = "oculto en tu chat y en pantalla";
				return res;
			}
			if (a == "ignore")
			{
				if (user.Length == 0)
				{
					res["error"] = "este mensaje no trae el usuario";
					return res;
				}
				IgnoreUser(user);
				if (uid.Length > 0)
				{
					chatServer.Send(new ChatItem { t = "del", p = p, uid = uid });
				}
				LogLater("Moderación: " + user + " ya no sale en tu chat ni en pantalla (Chat → Moderación para quitarlo de la lista).");
				res["ok"] = true;
				res["msg"] = user + " ya no sale en tu chat ni en pantalla";
				return res;
			}

			// ---- Todo el chat ----
			if (a == "clear")
			{
				// En la página se borra siempre; en Twitch, además, para todos si estás conectado.
				chatServer.Send(new ChatItem { t = "clear" });
				string extra = "";
				if (twitch.Connected && twCh.Length > 0)
				{
					try
					{
						twitch.ClearChat(twCh);
						extra = " (y en Twitch para todos)";
					}
					catch (Exception ex)
					{
						extra = " (Twitch: " + ex.Message + ")";
					}
				}
				LogLater("Moderación: chat borrado" + extra + ".");
				res["ok"] = true;
				res["msg"] = "chat borrado" + extra;
				return res;
			}
			if (a == "modes" || a == "setmode")
			{
				if (!twitch.Connected || twCh.Length == 0)
				{
					res["error"] = "los modos del chat son de Twitch: conecta tu cuenta en la pestaña Título";
					return res;
				}
				try
				{
					Dictionary<string, object> modes;
					if (a == "modes")
					{
						modes = twitch.GetChatSettings(twCh);
					}
					else
					{
						string k = J.S(d, "k");
						object v = J.At(d, "v");
						if (k != "slow" && k != "followers" && k != "subscribers" && k != "emotes")
						{
							res["error"] = "modo desconocido";
							return res;
						}
						Dictionary<string, object> ch = new Dictionary<string, object>();
						ch[k] = v;
						modes = twitch.SetChatSettings(twCh, ch);
						LogLater("Moderación Twitch: " + ModeText(k, v) + ".");
					}
					res["ok"] = true;
					res["modes"] = modes;
				}
				catch (Exception ex)
				{
					res["error"] = ex.Message;
				}
				return res;
			}

			// ---- Ficha del usuario (Twitch: antigüedad de la cuenta y desde cuándo sigue) ----
			if (a == "info")
			{
				res["ok"] = true;
				if (p == "twitch" && twitch.Connected && twCh.Length > 0 && uid.Length > 0)
				{
					try
					{
						foreach (KeyValuePair<string, object> kv in twitch.UserCard(twCh, uid))
						{
							res[kv.Key] = kv.Value;
						}
					}
					catch (Exception ex)
					{
						res["note"] = ex.Message;
					}
				}
				return res;
			}

			// ---- VIP y moderador (Twitch, solo el dueño del canal) ----
			if (a == "vip+" || a == "vip-" || a == "mod+" || a == "mod-")
			{
				if (p != "twitch")
				{
					res["error"] = "dar VIP o moderador desde aquí solo se puede en Twitch";
					return res;
				}
				if (!twitch.Connected || twCh.Length == 0)
				{
					res["error"] = "conecta tu cuenta de Twitch en la pestaña Título";
					return res;
				}
				if (uid.Length == 0)
				{
					res["error"] = "este mensaje no trae los datos necesarios";
					return res;
				}
				bool on = a.EndsWith("+");
				bool vip = a.StartsWith("vip");
				try
				{
					if (vip)
					{
						twitch.SetVip(twCh, uid, on);
					}
					else
					{
						twitch.SetModerator(twCh, uid, on);
					}
					string what = vip ? (on ? (user + " ahora es VIP") : (user + " ya no es VIP")) : (on ? (user + " ahora es moderador") : (user + " ya no es moderador"));
					LogLater("Moderación Twitch: " + what + ".");
					res["ok"] = true;
					res["msg"] = what;
				}
				catch (Exception ex)
				{
					res["error"] = ex.Message;
					LogLater("Moderación Twitch: " + ex.Message);
				}
				return res;
			}

			// ---- Borrar todos sus mensajes (los que hay en el chat), sin expulsarle ----
			if (a == "purge")
			{
				if (p == "tiktok")
				{
					res["error"] = "TikTok no deja moderar desde otros programas: usa Ocultar";
					return res;
				}
				Platform pp = (p == "twitch") ? twitch : ((p == "kick") ? ((Platform)kick) : ((p == "youtube") ? ((Platform)youtube) : null));
				if (pp == null || !pp.Connected)
				{
					res["error"] = (pp == null) ? "plataforma desconocida" : ("conecta tu cuenta de " + pp.Name + " en la pestaña Título para moderar");
					return res;
				}
				int done = 0, total = 0;
				string lastError = "";
				foreach (object o in J.L(d, "items"))
				{
					if (total >= 50)
					{
						break;
					}
					string mid = J.S(o, "id");
					string mtext = J.S(o, "text");
					if (mid.Length == 0 && p != "youtube")
					{
						continue;
					}
					total++;
					try
					{
						if (p == "twitch")
						{
							twitch.DeleteMessage(twCh, mid);
						}
						else if (p == "kick")
						{
							kick.DeleteMessage(mid);
						}
						else
						{
							youtube.DeleteMessage(uid, mtext);
						}
						done++;
						if (mid.Length > 0)
						{
							chatServer.Send(new ChatItem { t = "del", p = p, id = mid });
						}
					}
					catch (Exception ex)
					{
						lastError = ex.Message;
					}
				}
				if (done == 0)
				{
					res["error"] = (lastError.Length > 0) ? lastError : "no hay mensajes suyos que borrar";
					return res;
				}
				string pmsg = (done == 1) ? ("1 mensaje de " + user + " borrado") : (done + " mensajes de " + user + " borrados");
				LogLater("Moderación " + pp.Name + ": " + pmsg + ".");
				res["ok"] = true;
				res["msg"] = pmsg;
				return res;
			}

			// ---- Una persona o un mensaje, en la plataforma ----
			// Expulsión temporal: "t" + segundos (el control de arrastrar va de 10 s a 24 h).
			int secs = 0;
			if (a.Length > 1 && a[0] == 't')
			{
				int.TryParse(a.Substring(1), out secs);
				secs = Math.Max(0, Math.Min(86400, secs));
			}
			bool del = a == "del";
			bool unban = a == "unban";
			if (!del && !unban && a != "ban" && secs == 0)
			{
				res["error"] = "acción desconocida";
				return res;
			}
			if (p == "tiktok")
			{
				res["error"] = "TikTok no deja moderar desde otros programas: usa Ocultar";
				return res;
			}
			Platform plat = (p == "twitch") ? twitch : ((p == "kick") ? ((Platform)kick) : ((p == "youtube") ? ((Platform)youtube) : null));
			if (plat == null)
			{
				res["error"] = "plataforma desconocida";
				return res;
			}
			if (!plat.Connected)
			{
				res["error"] = "conecta tu cuenta de " + plat.Name + " en la pestaña Título para moderar";
				return res;
			}
			if ((del && id.Length == 0 && p != "youtube") || (!del && uid.Length == 0))
			{
				res["error"] = "este mensaje no trae los datos necesarios";
				return res;
			}
			try
			{
				if (p == "twitch")
				{
					if (del)
					{
						twitch.DeleteMessage(twCh, id);
					}
					else if (unban)
					{
						twitch.Unban(twCh, uid);
					}
					else
					{
						twitch.Ban(twCh, uid, secs, "Vortexs Live");
					}
				}
				else if (p == "kick")
				{
					if (del)
					{
						kick.DeleteMessage(id);
					}
					else if (unban)
					{
						kick.Unban(kcCh, uid);
					}
					else
					{
						kick.Ban(kcCh, uid, secs, "Vortexs Live");
					}
				}
				else if (del)
				{
					youtube.DeleteMessage(uid, text);
				}
				else if (unban)
				{
					youtube.Unban(uid);
				}
				else
				{
					youtube.Ban(uid, secs);
				}
				string what = del ? ("mensaje de " + user + " borrado") : (unban ? ("ban de " + user + " quitado") : ((secs > 0) ? (user + " expulsado " + Duration(secs)) : (user + " baneado")));
				LogLater("Moderación " + plat.Name + ": " + what + ".");
				res["ok"] = true;
				res["msg"] = what;
				res["undo"] = !del && !unban;
				if (del)
				{
					// Se quita ya de la página (la plataforma también lo avisará).
					chatServer.Send(new ChatItem { t = "del", p = p, id = id });
				}
			}
			catch (Exception ex)
			{
				res["error"] = ex.Message;
				LogLater("Moderación " + plat.Name + ": " + ex.Message);
			}
			return res;
		}

		private static string Duration(int secs)
		{
			if (secs >= 86400)
			{
				return secs / 86400 + " día" + ((secs >= 172800) ? "s" : "");
			}
			if (secs >= 3600)
			{
				return secs / 3600 + " h";
			}
			if (secs < 60)
			{
				return secs + " s";
			}
			return secs / 60 + " min";
		}

		private static string ModeText(string k, object v)
		{
			if (k == "slow")
			{
				int s = Convert.ToInt32(v);
				return (s > 0) ? ("modo lento de " + s + " s") : "modo lento quitado";
			}
			bool on = Convert.ToBoolean(v);
			string name = (k == "followers") ? "solo seguidores" : ((k == "subscribers") ? "solo suscriptores" : "solo emotes");
			return (on ? "activado " : "quitado ") + name;
		}

		// ---------- Configuración ----------

		private void LoadCmdConfig(Dictionary<string, string> raw)
		{
			string v;
			if (raw.TryGetValue("cmd_on", out v))
			{
				cmdOn = v != "0";
			}
			if (raw.TryGetValue("cmd_text", out v) && v.Length > 0)
			{
				try
				{
					cmdText = Encoding.UTF8.GetString(Convert.FromBase64String(v));
				}
				catch
				{
				}
			}
		}

		private void SaveCmdConfig(List<string> lines)
		{
			lines.Add("cmd_on=" + (cmdOn ? "1" : "0"));
			lines.Add("cmd_text=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(cmdText)));
		}
	}
}
