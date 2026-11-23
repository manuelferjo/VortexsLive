// YouTube: inicio de sesión con código (Google "TVs y dispositivos"), título y categoría del
// directo en curso o programado, escribir en el chat y moderar. YouTube no busca categorías:
// tiene una lista fija (All) y se propone una según el juego (Suggest).
using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal class YouTubePlatform : Platform
	{
		private const string Scope = "https://www.googleapis.com/auth/youtube";

		public static string AuthBase = "https://oauth2.googleapis.com";

		public static string ApiBase = "https://www.googleapis.com/youtube/v3";

		public static readonly Category[] All = new Category[14]
		{
			new Category("20", "Videojuegos"),
			new Category("22", "Gente y blogs"),
			new Category("24", "Entretenimiento"),
			new Category("23", "Humor"),
			new Category("10", "Música"),
			new Category("17", "Deportes"),
			new Category("28", "Ciencia y tecnología"),
			new Category("27", "Educación"),
			new Category("26", "Consejos y estilo"),
			new Category("1", "Cine y animación"),
			new Category("2", "Motor"),
			new Category("19", "Viajes y eventos"),
			new Category("15", "Animales"),
			new Category("25", "Noticias y política")
		};

		private string liveChatId = "";

		private DateTime liveChatAt = DateTime.MinValue;

		public override string Name
		{
			get
			{
				return "YouTube";
			}
		}

		public override string Key
		{
			get
			{
				return "yt";
			}
		}

		public override bool NeedsSecret
		{
			get
			{
				return true;
			}
		}

		public override string ConsoleUrl
		{
			get
			{
				return "https://console.cloud.google.com/apis/credentials";
			}
		}

		public override int TitleMax
		{
			get
			{
				return 100;
			}
		}

		public override void Login(ILoginUi ui)
		{
			Dictionary<string, object> d = Http.Send("POST", AuthBase + "/device/code", null, null, "application/x-www-form-urlencoded", Http.Form("client_id", ClientId, "scope", Scope));
			// Google lo ha llamado de las dos formas.
			string url = Http.Str(d, "verification_url");
			if (url.Length == 0)
			{
				url = Http.Str(d, "verification_uri");
			}
			ui.ShowCode(Http.Str(d, "user_code"), url);
			Dictionary<string, object> tokens = PollDevice(ui, AuthBase + "/token", new string[8]
			{
				"client_id",
				ClientId,
				"client_secret",
				ClientSecret,
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
			SetTokens(Http.Send("POST", AuthBase + "/token", null, null, "application/x-www-form-urlencoded", Http.Form("client_id", ClientId, "client_secret", ClientSecret, "refresh_token", refresh, "grant_type", "refresh_token")));
		}

		protected override void LoadAccount()
		{
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/channels?part=snippet&mine=true", null, null), "items");
			Account = ((list.Count > 0) ? Http.Str(Http.Obj(list[0], "snippet"), "title") : "");
		}

		public override List<Category> Search(string q)
		{
			List<Category> list = new List<Category>();
			string wanted = Platform.Norm(q);
			foreach (Category category in All)
			{
				if (wanted.Length == 0 || Platform.Norm(category.Name).Contains(wanted))
				{
					list.Add(category);
				}
			}
			return list;
		}

		// Categoría de YouTube para un juego o categoría de Twitch/Kick: charla → Gente y blogs,
		// música → Música, deportes → Deportes, y el resto Videojuegos.
		public static Category Suggest(string gameName)
		{
			string game = Platform.Norm(gameName);
			if (game.Length == 0)
			{
				return null;
			}
			if (game.Contains("justchatting") || game.Contains("charlando") || game == "irl" || game.Contains("chatting"))
			{
				return All[1];
			}
			if (game.Contains("music") || game.Contains("musica"))
			{
				return All[4];
			}
			if (game.Contains("sport") || game.Contains("deporte"))
			{
				return All[5];
			}
			return All[0];
		}

		// El directo en curso o, si no hay, el próximo programado.
		private string BroadcastId()
		{
			foreach (string status in new string[2] { "active", "upcoming" })
			{
				List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/liveBroadcasts?part=id&broadcastType=all&maxResults=5&broadcastStatus=" + status, null, null), "items");
				if (list.Count > 0)
				{
					return Http.Str(list[0], "id");
				}
			}
			throw new ApiException(404, "no hay ningún directo en curso ni programado en tu canal");
		}

		public override void ReadCurrent(out string title, out Category cat)
		{
			Dictionary<string, object> d = Snippet(BroadcastId());
			title = Http.Str(d, "title");
			string categoryId = Http.Str(d, "categoryId");
			cat = null;
			foreach (Category category in All)
			{
				if (category.Id == categoryId)
				{
					cat = category;
				}
			}
		}

		private Dictionary<string, object> Snippet(string id)
		{
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/videos?part=snippet&id=" + id, null, null), "items");
			if (list.Count == 0)
			{
				throw new ApiException(404, "no se encontró el vídeo del directo");
			}
			return Http.Obj(list[0], "snippet");
		}

		public override void Update(string title, Category cat)
		{
			// YouTube sustituye el "snippet" entero: hay que volver a mandar la descripción, las etiquetas
			// y los idiomas que ya tenía, o se borrarían.
			string videoId = BroadcastId();
			Dictionary<string, object> current = Snippet(videoId);
			Dictionary<string, object> snippet = new Dictionary<string, object>();
			snippet["title"] = title ?? Http.Str(current, "title");
			snippet["categoryId"] = ((cat != null) ? cat.Id : Http.Str(current, "categoryId"));
			snippet["description"] = Http.Str(current, "description");
			object value;
			if (current.TryGetValue("tags", out value) && value != null)
			{
				snippet["tags"] = value;
			}
			foreach (string field in new string[2] { "defaultLanguage", "defaultAudioLanguage" })
			{
				if (Http.Str(current, field).Length > 0)
				{
					snippet[field] = Http.Str(current, field);
				}
			}
			Dictionary<string, object> video = new Dictionary<string, object>();
			video["id"] = videoId;
			video["snippet"] = snippet;
			Api("PUT", ApiBase + "/videos?part=snippet", null, video);
		}

		// Escribe en el chat del directo en curso (el ID del chat se guarda 10 minutos).
		public void SendChat(string text)
		{
			if (liveChatId.Length == 0 || DateTime.UtcNow - liveChatAt > TimeSpan.FromMinutes(10.0))
			{
				List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/liveBroadcasts?part=snippet&broadcastType=all&maxResults=5&broadcastStatus=active", null, null), "items");
				liveChatId = ((list.Count > 0) ? Http.Str(Http.Obj(list[0], "snippet"), "liveChatId") : "");
				liveChatAt = DateTime.UtcNow;
				if (liveChatId.Length == 0)
				{
					throw new ApiException(404, "no estás en directo en YouTube");
				}
			}
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{
					"snippet",
					new Dictionary<string, object>
					{
						{ "liveChatId", liveChatId },
						{ "type", "textMessageEvent" },
						{
							"textMessageDetails",
							new Dictionary<string, object> { { "messageText", text } }
						}
					}
				}
			};
			try
			{
				Api("POST", ApiBase + "/liveChat/messages?part=snippet", null, body);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 403 || ex.Status == 404)
				{
					liveChatId = "";
				}
				if (ex.Status == 403 && ex.Message.ToLowerInvariant().Contains("quota"))
				{
					throw new ApiException(403, "se ha gastado el cupo diario de la API de YouTube");
				}
				throw;
			}
		}
		// ---------- Moderación ----------

		private string LiveChatId()
		{
			if (liveChatId.Length == 0 || DateTime.UtcNow - liveChatAt > TimeSpan.FromMinutes(10.0))
			{
				List<Dictionary<string, object>> list = Http.Arr(Api("GET", ApiBase + "/liveBroadcasts?part=snippet&broadcastType=all&maxResults=5&broadcastStatus=active", null, null), "items");
				liveChatId = ((list.Count > 0) ? Http.Str(Http.Obj(list[0], "snippet"), "liveChatId") : "");
				liveChatAt = DateTime.UtcNow;
				if (liveChatId.Length == 0)
				{
					throw new ApiException(404, "no estás en directo en YouTube");
				}
			}
			return liveChatId;
		}

		private static ApiException ModError(ApiException ex)
		{
			if (ex.Status == 403 && ex.Message.ToLowerInvariant().Contains("quota"))
			{
				return new ApiException(403, "se ha gastado el cupo diario de la API de YouTube");
			}
			if (ex.Status == 401 || ex.Status == 403)
			{
				return new ApiException(ex.Status, "YouTube no deja moderar con esta cuenta (vuelve a conectar YouTube en Directo → Título y categoría)");
			}
			return ex;
		}

		// El chat de YouTube se lee sin la API, así que el mensaje se busca entre los últimos del chat
		// por autor y texto (si el texto no coincide, se borra el último de esa persona).
		public void DeleteMessage(string authorChannelId, string text)
		{
			try
			{
				string chat = LiveChatId();
				List<Dictionary<string, object>> items = Http.Arr(Api("GET", ApiBase + "/liveChat/messages?part=snippet,authorDetails&maxResults=2000&liveChatId=" + Uri.EscapeDataString(chat), null, null), "items");
				string byText = null;
				string last = null;
				string want = (text ?? "").Trim();
				foreach (Dictionary<string, object> it in items)
				{
					if (Http.Str(Http.Obj(it, "authorDetails"), "channelId") != authorChannelId)
					{
						continue;
					}
					string id = Http.Str(it, "id");
					last = id;
					if (want.Length > 0 && Http.Str(Http.Obj(it, "snippet"), "displayMessage").Trim() == want)
					{
						byText = id;
					}
				}
				string target = byText ?? last;
				if (target == null)
				{
					throw new ApiException(404, "no se encontró el mensaje en YouTube");
				}
				Api("DELETE", ApiBase + "/liveChat/messages?id=" + Uri.EscapeDataString(target), null, null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// Para poder deshacer: id de cada ban que se ha puesto en esta sesión, por canal del usuario.
		private readonly Dictionary<string, string> banIds = new Dictionary<string, string>();

		public void Unban(string authorChannelId)
		{
			string id;
			lock (banIds)
			{
				if (!banIds.TryGetValue(authorChannelId, out id))
				{
					throw new ApiException(404, "YouTube solo deja deshacer los bans puestos desde Vortexs Live en esta sesión");
				}
			}
			try
			{
				Api("DELETE", ApiBase + "/liveChat/bans?id=" + Uri.EscapeDataString(id), null, null);
				lock (banIds)
				{
					banIds.Remove(authorChannelId);
				}
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// seconds = 0: ban permanente.
		public void Ban(string authorChannelId, int seconds)
		{
			Dictionary<string, object> snippet = new Dictionary<string, object>();
			snippet.Add("liveChatId", LiveChatId());
			snippet.Add("type", (seconds > 0) ? "temporary" : "permanent");
			if (seconds > 0)
			{
				snippet.Add("banDurationSeconds", seconds);
			}
			snippet.Add("bannedUserDetails", new Dictionary<string, object> { { "channelId", authorChannelId } });
			Dictionary<string, object> body = new Dictionary<string, object>();
			body.Add("snippet", snippet);
			try
			{
				string id = Http.Str(Api("POST", ApiBase + "/liveChat/bans?part=snippet", null, body), "id");
				if (id.Length > 0)
				{
					lock (banIds)
					{
						banIds[authorChannelId] = id;
					}
				}
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}
	}
}
