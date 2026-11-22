// Twitch: inicio de sesión con código (se escribe en twitch.tv/activate), título y categoría,
// escribir en el chat, moderar y suscribirse a los follows (EventSub por WebSocket).
// Twitch exige mandar el ID de cliente en cada llamada (cabecera Client-Id, ver H).
using System;
using System.Collections;
using System.Collections.Generic;

namespace StreamDelay
{
	internal class TwitchPlatform : Platform
	{
		private const string Scopes = "channel:manage:broadcast moderator:read:followers user:write:chat moderator:manage:chat_messages moderator:manage:banned_users moderator:manage:chat_settings user:read:emotes channel:read:redemptions channel:manage:vips channel:manage:moderators";

		public static string IdBase = "https://id.twitch.tv";

		public static string ApiBase = "https://api.twitch.tv/helix";

		private string userId = "";

		public override string Name
		{
			get
			{
				return "Twitch";
			}
		}

		public override string Key
		{
			get
			{
				return "tw";
			}
		}

		public override bool NeedsSecret
		{
			get
			{
				return false;
			}
		}

		public override string ConsoleUrl
		{
			get
			{
				return "https://dev.twitch.tv/console/apps/create";
			}
		}

		// Cabeceras que pide Twitch en cada llamada.
		private Dictionary<string, string> H()
		{
			Dictionary<string, string> headers = new Dictionary<string, string>();
			headers.Add("Client-Id", ClientId);
			return headers;
		}

		// Flujo "device code": Twitch da un código, el usuario lo escribe en su web y aquí se
		// pregunta cada pocos segundos si ya lo ha aprobado (PollDevice).
		public override void Login(ILoginUi ui)
		{
			Dictionary<string, object> d = Http.Send("POST", IdBase + "/oauth2/device", null, null, "application/x-www-form-urlencoded", Http.Form("client_id", ClientId, "scopes", Scopes));
			string url = Http.Str(d, "verification_uri");
			ui.ShowCode(Http.Str(d, "user_code"), url);
			Dictionary<string, object> tokens = PollDevice(ui, IdBase + "/oauth2/token", new string[8]
			{
				"client_id",
				ClientId,
				"scopes",
				Scopes,
				"device_code",
				Http.Str(d, "device_code"),
				"grant_type",
				"urn:ietf:params:oauth:grant-type:device_code"
			}, Http.Int(d, "interval", 5), Http.Int(d, "expires_in", 1800));
			SetTokens(tokens);
			FinishLogin();
		}

		protected override void DoRefresh()
		{
			SetTokens(Http.Send("POST", IdBase + "/oauth2/token", null, null, "application/x-www-form-urlencoded", Http.Form("client_id", ClientId, "grant_type", "refresh_token", "refresh_token", refresh)));
		}

		protected override void LoadAccount()
		{
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/users", H(), null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(0, "no se encontró tu usuario de Twitch");
			}
			userId = Http.Str(list[0], "id");
			Account = Http.Str(list[0], "display_name");
			if (Account.Length == 0)
			{
				Account = Http.Str(list[0], "login");
			}
		}

		private void EnsureUser()
		{
			if (userId.Length == 0)
			{
				LoadAccount();
			}
		}

		// ¿El token tiene el permiso de leer follows? (si se conectó con una versión antigua, no).
		public bool CanReadFollows()
		{
			return HasScope("moderator:read:followers");
		}

		// ¿El canal es el de la cuenta conectada? (los canjes de puntos solo los ve el dueño).
		public bool IsOwnChannel(string channelLogin)
		{
			EnsureUser();
			return string.IsNullOrEmpty(channelLogin) || channelLogin.Equals(Account, StringComparison.OrdinalIgnoreCase);
		}

		// ¿El token tiene este permiso? (si se conectó con una versión anterior, puede que no).
		public bool HasScope(string scope)
		{
			Dictionary<string, object> validation = Http.Send("GET", IdBase + "/oauth2/validate", null, new Dictionary<string, string> {
			{
				"Authorization",
				"OAuth " + Token()
			} }, null, null);
			object value;
			IEnumerable scopes = ((validation != null && validation.TryGetValue("scopes", out value)) ? (value as IEnumerable) : null);
			if (scopes != null)
			{
				foreach (object item in scopes)
				{
					if (scope.Equals(item))
					{
						return true;
					}
				}
			}
			return false;
		}

		// ID numérico del canal (el propio si channelLogin está vacío o es la cuenta conectada).
		private string BroadcasterId(string channelLogin)
		{
			EnsureUser();
			if (string.IsNullOrEmpty(channelLogin) || channelLogin.Equals(Account, StringComparison.OrdinalIgnoreCase))
			{
				return userId;
			}
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/users?login=" + Uri.EscapeDataString(channelLogin), H(), null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(404, "el canal \"" + channelLogin + "\" no existe");
			}
			return Http.Str(list[0], "id");
		}

		public void SendChat(string channelLogin, string text)
		{
			SendChat(channelLogin, text, null);
		}

		// replyTo: id del mensaje al que se responde (en Twitch sale como respuesta a ese mensaje).
		public void SendChat(string channelLogin, string text, string replyTo)
		{
			string broadcasterId = BroadcasterId(channelLogin);
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{ "broadcaster_id", broadcasterId },
				{ "sender_id", userId },
				{ "message", text }
			};
			if (!string.IsNullOrEmpty(replyTo))
			{
				body["reply_parent_message_id"] = replyTo;
			}
			Dictionary<string, object> d;
			try
			{
				d = Api("POST", ApiBase + "/chat/messages", H(), body);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 401 && ex.Message.ToLowerInvariant().Contains("scope"))
				{
					throw new ApiException(401, "falta permiso: vuelve a conectar Twitch");
				}
				throw;
			}
			// Twitch puede aceptar la llamada pero no publicar el mensaje (AutoMod, modo seguidores…).
			List<Dictionary<string, object>> list = Http.Arr(d, "data");
			if (list.Count > 0 && Http.Str(list[0], "is_sent").ToLowerInvariant() == "false")
			{
				string reason = Http.Str(Http.Obj(list[0], "drop_reason"), "message");
				throw new ApiException(400, (reason.Length > 0) ? reason : "Twitch no envió el mensaje");
			}
		}

		// ---------- Moderación (borrar mensajes, expulsar temporalmente o banear) ----------

		private static ApiException ModError(ApiException ex)
		{
			if (ex.Status == 401 || ex.Status == 403)
			{
				return new ApiException(ex.Status, "falta permiso de moderación: vuelve a conectar Twitch en la pestaña Título (o no eres moderador de ese canal)");
			}
			return ex;
		}

		// Ficha de un usuario: cuándo creó la cuenta y desde cuándo sigue el canal ("" si no sigue).
		// Lo que no se pueda saber (permisos, error) se deja sin poner.
		public Dictionary<string, object> UserCard(string channelLogin, string targetUserId)
		{
			Dictionary<string, object> r = new Dictionary<string, object>();
			string b = BroadcasterId(channelLogin);
			try
			{
				List<Dictionary<string, object>> u = Http.Arr(Api("GET", ApiBase + "/users?id=" + Uri.EscapeDataString(targetUserId), H(), null), "data");
				if (u.Count > 0)
				{
					r["created"] = Http.Str(u[0], "created_at");
					r["login"] = Http.Str(u[0], "login");
				}
			}
			catch
			{
			}
			try
			{
				List<Dictionary<string, object>> f = Http.Arr(Api("GET", ApiBase + "/channels/followers?broadcaster_id=" + b + "&user_id=" + Uri.EscapeDataString(targetUserId), H(), null), "data");
				r["followed"] = (f.Count > 0) ? Http.Str(f[0], "followed_at") : "";
			}
			catch
			{
			}
			return r;
		}

		// Dar o quitar VIP / moderador (solo el dueño del canal; permisos channel:manage:vips y
		// channel:manage:moderators).
		public void SetVip(string channelLogin, string targetUserId, bool on)
		{
			RoleCall(ApiBase + "/channels/vips", channelLogin, targetUserId, on);
		}

		public void SetModerator(string channelLogin, string targetUserId, bool on)
		{
			RoleCall(ApiBase + "/moderation/moderators", channelLogin, targetUserId, on);
		}

		private void RoleCall(string url, string channelLogin, string targetUserId, bool on)
		{
			string b = BroadcasterId(channelLogin);
			if (b != userId)
			{
				throw new ApiException(403, "solo el dueño del canal puede dar o quitar VIP y moderador");
			}
			try
			{
				Api(on ? "POST" : "DELETE", url + "?broadcaster_id=" + b + "&user_id=" + Uri.EscapeDataString(targetUserId), H(), null);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 401 || ex.Status == 403)
				{
					throw new ApiException(ex.Status, "falta permiso: vuelve a conectar Twitch en la pestaña Título");
				}
				throw;
			}
		}

		public void DeleteMessage(string channelLogin, string messageId)
		{
			string b = BroadcasterId(channelLogin);
			try
			{
				Api("DELETE", ApiBase + "/moderation/chat?broadcaster_id=" + b + "&moderator_id=" + userId + "&message_id=" + Uri.EscapeDataString(messageId), H(), null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// seconds = 0: ban permanente.
		public void Ban(string channelLogin, string targetUserId, int seconds, string reason)
		{
			string b = BroadcasterId(channelLogin);
			Dictionary<string, object> data = new Dictionary<string, object>();
			data.Add("user_id", targetUserId);
			if (seconds > 0)
			{
				data.Add("duration", Math.Max(1, Math.Min(1209600, seconds)));
			}
			if (!string.IsNullOrEmpty(reason))
			{
				data.Add("reason", reason);
			}
			Dictionary<string, object> body = new Dictionary<string, object>();
			body.Add("data", data);
			try
			{
				Api("POST", ApiBase + "/moderation/bans?broadcaster_id=" + b + "&moderator_id=" + userId, H(), body);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// Pide a Twitch que avise de los follows por la conexión EventSub (sessionId).
		// Quita un ban o una expulsión temporal.
		public void Unban(string channelLogin, string targetUserId)
		{
			string b = BroadcasterId(channelLogin);
			try
			{
				Api("DELETE", ApiBase + "/moderation/bans?broadcaster_id=" + b + "&moderator_id=" + userId + "&user_id=" + Uri.EscapeDataString(targetUserId), H(), null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// Borra todos los mensajes del chat.
		public void ClearChat(string channelLogin)
		{
			string b = BroadcasterId(channelLogin);
			try
			{
				Api("DELETE", ApiBase + "/moderation/chat?broadcaster_id=" + b + "&moderator_id=" + userId, H(), null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// Modos del chat: slow (segundos, 0 = no), followers, subscribers, emotes.
		public Dictionary<string, object> GetChatSettings(string channelLogin)
		{
			string b = BroadcasterId(channelLogin);
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/chat/settings?broadcaster_id=" + b + "&moderator_id=" + userId, H(), null), "data");
			return ChatModes((list.Count > 0) ? list[0] : new Dictionary<string, object>());
		}

		// Cambia solo lo que venga en "changes" (mismas claves que GetChatSettings) y devuelve cómo queda.
		public Dictionary<string, object> SetChatSettings(string channelLogin, Dictionary<string, object> changes)
		{
			string b = BroadcasterId(channelLogin);
			Dictionary<string, object> body = new Dictionary<string, object>();
			object v;
			if (changes.TryGetValue("slow", out v))
			{
				int s = Convert.ToInt32(v);
				body["slow_mode"] = s > 0;
				if (s > 0)
				{
					body["slow_mode_wait_time"] = Math.Max(3, Math.Min(120, s));
				}
			}
			if (changes.TryGetValue("followers", out v))
			{
				body["follower_mode"] = Convert.ToBoolean(v);
			}
			if (changes.TryGetValue("subscribers", out v))
			{
				body["subscriber_mode"] = Convert.ToBoolean(v);
			}
			if (changes.TryGetValue("emotes", out v))
			{
				body["emote_mode"] = Convert.ToBoolean(v);
			}
			try
			{
				List<Dictionary<string, object>> list = Http.Arr(Api("PATCH", ApiBase + "/chat/settings?broadcaster_id=" + b + "&moderator_id=" + userId, H(), body), "data");
				return ChatModes((list.Count > 0) ? list[0] : new Dictionary<string, object>());
			}
			catch (ApiException ex)
			{
				if (ex.Status == 401 || ex.Status == 403)
				{
					throw new ApiException(ex.Status, "para cambiar los modos del chat vuelve a conectar Twitch en la pestaña Título (permiso nuevo)");
				}
				throw;
			}
		}

		// Emotes de Twitch para el botón 😊 del chat, en tres grupos: los del canal, los de otros
		// canales que puedes usar (tus suscripciones) y los globales. Con el permiso user:read:emotes
		// salen todos los tuyos; con una conexión antigua, solo los del canal y los globales
		// (all = false: conviene volver a conectar Twitch).
		// Emblemas del chat (los globales y los del canal, que pisan a los globales: sub, bits…).
		// Clave "set/versión" (como en la etiqueta badges del IRC) → { imagen 2x, título }.
		public Dictionary<string, string[]> ChatBadges(string channelLogin)
		{
			Dictionary<string, string[]> map = new Dictionary<string, string[]>();
			string[] urls = new string[2]
			{
				ApiBase + "/chat/badges/global",
				ApiBase + "/chat/badges?broadcaster_id=" + BroadcasterId(channelLogin)
			};
			foreach (string url in urls)
			{
				foreach (Dictionary<string, object> set in Http.Arr(Api("GET", url, H(), null), "data"))
				{
					string setId = Http.Str(set, "set_id");
					foreach (Dictionary<string, object> v in Http.Arr(set, "versions"))
					{
						string img = Http.Str(v, "image_url_2x");
						if (img.Length == 0)
						{
							img = Http.Str(v, "image_url_1x");
						}
						if (img.Length > 0)
						{
							map[setId + "/" + Http.Str(v, "id")] = new string[2] { img, Http.Str(v, "title") };
						}
					}
				}
			}
			return map;
		}

		public void ChatEmotes(string channelLogin, List<Dictionary<string, object>> channel, List<Dictionary<string, object>> others, List<Dictionary<string, object>> globals, out bool all)
		{
			string chId = BroadcasterId(channelLogin);
			all = false;
			try
			{
				List<Dictionary<string, object>> mine = new List<Dictionary<string, object>>();
				List<Dictionary<string, object>> rest = new List<Dictionary<string, object>>();
				List<Dictionary<string, object>> glob = new List<Dictionary<string, object>>();
				string cursor = "";
				for (int page = 0; page < 30; page++)
				{
					Dictionary<string, object> d = Api("GET", ApiBase + "/chat/emotes/user?user_id=" + userId + "&broadcaster_id=" + chId + ((cursor.Length > 0) ? ("&after=" + Uri.EscapeDataString(cursor)) : ""), H(), null);
					string template = Http.Str(d, "template");
					foreach (Dictionary<string, object> e in Http.Arr(d, "data"))
					{
						string owner = Http.Str(e, "owner_id");
						Dictionary<string, object> item = EmoteItem(e, template);
						if (owner == chId)
						{
							mine.Add(item);
						}
						else if (Http.Str(e, "emote_type") == "globals" || owner == "0" || owner.Length == 0)
						{
							glob.Add(item);
						}
						else
						{
							rest.Add(item);
						}
					}
					object pg;
					cursor = (d.TryGetValue("pagination", out pg) && pg is Dictionary<string, object>) ? Http.Str((Dictionary<string, object>)pg, "cursor") : "";
					if (cursor.Length == 0)
					{
						break;
					}
				}
				channel.AddRange(mine);
				others.AddRange(rest);
				globals.AddRange(glob);
				all = true;
				return;
			}
			catch (ApiException ex)
			{
				// Sin el permiso nuevo: 401. Cualquier otro error, igual: se prueba lo básico.
				if (ex.Status != 401 && ex.Status != 403)
				{
					throw;
				}
			}
			Dictionary<string, object> ch = Api("GET", ApiBase + "/chat/emotes?broadcaster_id=" + chId, H(), null);
			foreach (Dictionary<string, object> e in Http.Arr(ch, "data"))
			{
				channel.Add(EmoteItem(e, Http.Str(ch, "template")));
			}
			Dictionary<string, object> gl = Api("GET", ApiBase + "/chat/emotes/global", H(), null);
			foreach (Dictionary<string, object> e in Http.Arr(gl, "data"))
			{
				globals.Add(EmoteItem(e, Http.Str(gl, "template")));
			}
		}

		// { "n": nombre, "u": imagen } (animada si la hay, en tema oscuro).
		private static Dictionary<string, object> EmoteItem(Dictionary<string, object> e, string template)
		{
			string id = Http.Str(e, "id");
			bool animated = false;
			object f;
			if (e.TryGetValue("format", out f) && f is IEnumerable && !(f is string))
			{
				foreach (object x in (IEnumerable)f)
				{
					if ("animated".Equals(x))
					{
						animated = true;
					}
				}
			}
			if (template.Length == 0)
			{
				template = "https://static-cdn.jtvnw.net/emoticons/v2/{{id}}/{{format}}/{{theme_mode}}/{{scale}}";
			}
			string url = template.Replace("{{id}}", id).Replace("{{format}}", animated ? "animated" : "static").Replace("{{theme_mode}}", "dark").Replace("{{scale}}", "2.0");
			return new Dictionary<string, object>
			{
				{ "n", Http.Str(e, "name") },
				{ "u", url }
			};
		}

		private static Dictionary<string, object> ChatModes(Dictionary<string, object> d)
		{
			Dictionary<string, object> r = new Dictionary<string, object>();
			r["slow"] = (Http.Str(d, "slow_mode").ToLowerInvariant() == "true") ? Http.Int(d, "slow_mode_wait_time", 0) : 0;
			r["followers"] = Http.Str(d, "follower_mode").ToLowerInvariant() == "true";
			r["subscribers"] = Http.Str(d, "subscriber_mode").ToLowerInvariant() == "true";
			r["emotes"] = Http.Str(d, "emote_mode").ToLowerInvariant() == "true";
			return r;
		}

		// Pide a Twitch que avise de los canjes de puntos del canal (solo el dueño del canal).
		public void SubscribeRedemptions(string sessionId, string channelLogin)
		{
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{ "type", "channel.channel_points_custom_reward_redemption.add" },
				{ "version", "1" },
				{ "condition", new Dictionary<string, object> { { "broadcaster_user_id", BroadcasterId(channelLogin) } } },
				{ "transport", new Dictionary<string, object> { { "method", "websocket" }, { "session_id", sessionId } } }
			};
			Api("POST", ApiBase + "/eventsub/subscriptions", H(), body);
		}

		public void SubscribeFollows(string sessionId, string channelLogin)
		{
			string broadcasterId = BroadcasterId(channelLogin);
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{ "type", "channel.follow" },
				{ "version", "2" },
				{
					"condition",
					new Dictionary<string, object>
					{
						{ "broadcaster_user_id", broadcasterId },
						{ "moderator_user_id", userId }
					}
				},
				{
					"transport",
					new Dictionary<string, object>
					{
						{ "method", "websocket" },
						{ "session_id", sessionId }
					}
				}
			};
			Api("POST", ApiBase + "/eventsub/subscriptions", H(), body);
		}

		public override List<Category> Search(string q)
		{
			List<Category> list = new List<Category>();
			foreach (Dictionary<string, object> item in Http.Arr(Api("GET", ApiBase + "/search/categories?first=12&query=" + Uri.EscapeDataString(q), H(), null), "data"))
			{
				list.Add(new Category(Http.Str(item, "id"), Http.Str(item, "name")));
			}
			return list;
		}

		public override void ReadCurrent(out string title, out Category cat)
		{
			EnsureUser();
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/channels?broadcaster_id=" + userId, H(), null), "data");
			title = ((list.Count > 0) ? Http.Str(list[0], "title") : "");
			cat = ((list.Count > 0 && Http.Str(list[0], "game_id").Length > 0) ? new Category(Http.Str(list[0], "game_id"), Http.Str(list[0], "game_name")) : null);
		}

		public override void Update(string title, Category cat)
		{
			EnsureUser();
			Dictionary<string, object> changes = new Dictionary<string, object>();
			if (title != null)
			{
				changes["title"] = title;
			}
			if (cat != null)
			{
				changes["game_id"] = cat.Id;
			}
			Api("PATCH", ApiBase + "/channels?broadcaster_id=" + userId, H(), changes);
		}
	}
}
