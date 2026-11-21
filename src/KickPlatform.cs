// Kick: inicio de sesión OAuth con PKCE (se abre el navegador y Kick vuelve a
// http://localhost:17563/callback, donde Vortexs Live escucha un momento), título y categoría,
// escribir en el chat y moderar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal class KickPlatform : Platform
	{
		public const int CallbackPort = 17563;

		private const string Scopes = "user:read channel:read channel:write chat:write moderation:ban moderation:chat_message:manage";

		public static string IdBase = "https://id.kick.com";

		public static string ApiBase = "https://api.kick.com/public";

		// La que hay que poner en la app de Kick.
		public static string RedirectUri
		{
			get
			{
				return "http://localhost:" + CallbackPort + "/callback";
			}
		}

		public override string Name
		{
			get
			{
				return "Kick";
			}
		}

		public override string Key
		{
			get
			{
				return "kick";
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
				return "https://kick.com/settings/developer";
			}
		}

		// Base64 para URLs (sin "=" y con - y _).
		private static string B64Url(byte[] b)
		{
			return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-')
				.Replace('/', '_');
		}

		// OAuth con PKCE: se abre la página de Kick, se espera (máximo 5 min) a que el navegador vuelva
		// a localhost con el código y se cambia el código por los tokens.
		public override void Login(ILoginUi ui)
		{
			// PKCE: un secreto aleatorio (verifier) del que a Kick solo se le manda el SHA-256 (challenge).
			byte[] random = new byte[32];
			using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
			{
				rng.GetBytes(random);
			}
			string verifier = B64Url(random);
			string challenge;
			using (SHA256 sha = SHA256.Create())
			{
				challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
			}
			// state: para comprobar que la respuesta es de esta petición.
			byte[] stateBytes = new byte[16];
			using (RandomNumberGenerator rng2 = RandomNumberGenerator.Create())
			{
				rng2.GetBytes(stateBytes);
			}
			string state = B64Url(stateBytes);
			string code = null;
			string error = null;
			// Se escucha en localhost (IPv4 e IPv6, el navegador puede usar cualquiera).
			List<TcpListener> listeners = new List<TcpListener>();
			foreach (IPAddress localaddr in new IPAddress[2]
			{
				IPAddress.Loopback,
				IPAddress.IPv6Loopback
			})
			{
				try
				{
					TcpListener listener = new TcpListener(localaddr, CallbackPort);
					Sockets.NoInherit(listener);
					listener.Start();
					listeners.Add(listener);
				}
				catch
				{
				}
			}
			if (listeners.Count == 0)
			{
				throw new ApiException(0, "el puerto " + CallbackPort + " está ocupado; cierra el programa que lo use y vuelve a intentarlo");
			}
			try
			{
				string url = IdBase + "/oauth/authorize?" + Http.Form("response_type", "code", "client_id", ClientId, "redirect_uri", RedirectUri, "scope", Scopes, "code_challenge", challenge, "code_challenge_method", "S256", "state", state);
				ui.ShowBrowserWait(url);
				Http.OpenBrowser(url);
				DateTime deadline = DateTime.UtcNow.AddMinutes(5.0);
				while (code == null && error == null)
				{
					if (ui.Cancelled)
					{
						throw new OperationCanceledException();
					}
					if (DateTime.UtcNow > deadline)
					{
						throw new ApiException(0, "tiempo agotado esperando la autorización");
					}
					TcpListener ready = null;
					foreach (TcpListener item in listeners)
					{
						if (item.Pending())
						{
							ready = item;
							break;
						}
					}
					if (ready == null)
					{
						Thread.Sleep(100);
						continue;
					}
					using (TcpClient browser = ready.AcceptTcpClient())
					{
						browser.ReceiveTimeout = 5000;
						NetworkStream stream = browser.GetStream();
						// "GET /callback?code=…&state=… HTTP/1.1"
						string requestLine = ReadLine(stream);
						string q = "";
						string[] parts = requestLine.Split(' ');
						if (parts.Length >= 2 && parts[1].StartsWith("/callback"))
						{
							int question = parts[1].IndexOf('?');
							if (question >= 0)
							{
								q = parts[1].Substring(question + 1);
							}
							Dictionary<string, string> query = ParseQuery(q);
							string gotState;
							query.TryGetValue("state", out gotState);
							if (query.ContainsKey("error"))
							{
								error = query["error"];
								goto answer;
							}
							if (gotState != state)
							{
								Reply(stream, 400, "Respuesta no válida. Vuelve a intentarlo desde Vortexs Live.");
								continue;
							}
							if (query.TryGetValue("code", out code))
							{
								goto answer;
							}
							Reply(stream, 400, "Falta el código.");
						}
						else
						{
							// Otra petición del navegador (por ejemplo el favicon).
							Reply(stream, 404, "No encontrado");
						}
						goto done;
						answer:
						// Página que ve el usuario en el navegador al terminar.
						Reply(stream, 200, (error == null) ? "¡Listo! Vortexs Live ya está conectado a Kick. Puedes cerrar esta pestaña." : ("Kick no ha dado permiso (" + error + "). Puedes cerrar esta pestaña."));
						done:;
					}
				}
			}
			finally
			{
				foreach (TcpListener item in listeners)
				{
					try
					{
						item.Stop();
					}
					catch
					{
					}
				}
			}
			if (error != null)
			{
				throw new ApiException(0, (error == "access_denied") ? "has rechazado el acceso" : ("Kick devolvió: " + error));
			}
			SetTokens(Http.Send("POST", IdBase + "/oauth/token", null, null, "application/x-www-form-urlencoded", Http.Form("grant_type", "authorization_code", "code", code, "client_id", ClientId, "client_secret", ClientSecret, "redirect_uri", RedirectUri, "code_verifier", verifier)));
			FinishLogin();
		}

		// Una línea (hasta \n, sin \r), como mucho 8 KB.
		private static string ReadLine(Stream s)
		{
			StringBuilder sb = new StringBuilder();
			int b;
			while ((b = s.ReadByte()) >= 0 && b != 10 && sb.Length < 8192)
			{
				if (b != 13)
				{
					sb.Append((char)b);
				}
			}
			return sb.ToString();
		}

		// "a=1&b=2" → diccionario (con los caracteres %xx y + descodificados).
		private static Dictionary<string, string> ParseQuery(string q)
		{
			Dictionary<string, string> result = new Dictionary<string, string>();
			foreach (string pair in q.Split('&'))
			{
				int eq = pair.IndexOf('=');
				if (eq > 0)
				{
					result[Uri.UnescapeDataString(pair.Substring(0, eq).Replace('+', ' '))] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
				}
			}
			return result;
		}

		// Respuesta HTML sencilla para el navegador.
		private static void Reply(Stream s, int code, string msg)
		{
			string html = "<!doctype html><meta charset=utf-8><title>Vortexs Live</title><body style=\"background:#09090C;color:#F4F4F5;font:18px 'Segoe UI',sans-serif;display:grid;place-items:center;height:90vh\"><div style=\"max-width:520px;text-align:center\"><div style=\"font-size:28px;font-weight:700;color:#C09AFD;margin-bottom:12px\">Vortexs Live</div>" + WebUtility.HtmlEncode(I18n.Tr(msg)) + "</div>";
			byte[] body = Encoding.UTF8.GetBytes(html);
			string head = "HTTP/1.1 " + code + " OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n";
			byte[] headBytes = Encoding.ASCII.GetBytes(head);
			try
			{
				s.Write(headBytes, 0, headBytes.Length);
				s.Write(body, 0, body.Length);
				s.Flush();
			}
			catch
			{
			}
		}

		protected override void DoRefresh()
		{
			SetTokens(Http.Send("POST", IdBase + "/oauth/token", null, null, "application/x-www-form-urlencoded", Http.Form("grant_type", "refresh_token", "refresh_token", refresh, "client_id", ClientId, "client_secret", ClientSecret)));
		}

		// Nombre del canal (slug); si no lo hay, el nombre del usuario.
		protected override void LoadAccount()
		{
			List<Dictionary<string, object>> channels = Http.Arr(Api("GET", ApiBase + "/v1/channels", null, null), "data");
			if (channels.Count > 0)
			{
				Account = Http.Str(channels[0], "slug");
				if (Account.Length > 0)
				{
					return;
				}
			}
			List<Dictionary<string, object>> users = Http.Arr(Api("GET", ApiBase + "/v1/users", null, null), "data");
			Account = ((users.Count > 0) ? Http.Str(users[0], "name") : "");
		}

		public override List<Category> Search(string q)
		{
			List<Category> found = new List<Category>();
			foreach (Dictionary<string, object> item in Http.Arr(Api("GET", ApiBase + "/v2/categories?limit=12&name=" + Uri.EscapeDataString(q), null, null), "data"))
			{
				found.Add(new Category(Http.Str(item, "id"), Http.Str(item, "name")));
			}
			return found;
		}

		public override void ReadCurrent(out string title, out Category cat)
		{
			List<Dictionary<string, object>> channels = Http.Arr(Api("GET", ApiBase + "/v1/channels", null, null), "data");
			title = ((channels.Count > 0) ? Http.Str(channels[0], "stream_title") : "");
			Dictionary<string, object> category = ((channels.Count > 0) ? Http.Obj(channels[0], "category") : null);
			// Categoría "0" = ninguna.
			cat = ((category != null && Http.Str(category, "id").Length > 0 && Http.Str(category, "id") != "0") ? new Category(Http.Str(category, "id"), Http.Str(category, "name")) : null);
		}

		public override void Update(string title, Category cat)
		{
			Dictionary<string, object> changes = new Dictionary<string, object>();
			if (title != null)
			{
				changes["stream_title"] = title;
			}
			long categoryId;
			if (cat != null && long.TryParse(cat.Id, out categoryId))
			{
				changes["category_id"] = categoryId;
			}
			Api("PATCH", ApiBase + "/v1/channels", null, changes);
		}

		// Escribe en el chat del canal slug (el propio si está vacío).
		public void SendChat(string slug, string text)
		{
			string url = ApiBase + "/v1/channels" + ((string.IsNullOrEmpty(slug) || slug.Equals(Account, StringComparison.OrdinalIgnoreCase)) ? "" : ("?slug=" + Uri.EscapeDataString(slug)));
			List<Dictionary<string, object>> channels = Http.Arr(Api("GET", url, null, null), "data");
			if (channels.Count == 0)
			{
				throw new ApiException(404, "el canal \"" + slug + "\" no existe");
			}
			long broadcasterId;
			long.TryParse(Http.Str(channels[0], "broadcaster_user_id"), out broadcasterId);
			Dictionary<string, object> body = new Dictionary<string, object>
			{
				{ "type", "user" },
				{ "content", text },
				{ "broadcaster_user_id", broadcasterId }
			};
			Dictionary<string, object> d;
			try
			{
				d = Api("POST", ApiBase + "/v1/chat", null, body);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 401 || ex.Status == 403)
				{
					throw new ApiException(ex.Status, "falta permiso de chat: marca chat:write en tu app de Kick y vuelve a conectar Kick");
				}
				throw;
			}
			Dictionary<string, object> data = Http.Obj(d, "data");
			if (data != null && Http.Str(data, "is_sent").ToLowerInvariant() == "false")
			{
				throw new ApiException(400, "Kick no envió el mensaje");
			}
		}

		// ---------- Moderación ----------

		private static ApiException ModError(ApiException ex)
		{
			if (ex.Status == 401 || ex.Status == 403)
			{
				return new ApiException(ex.Status, "falta permiso de moderación: marca moderation:ban y moderation:chat_message:manage en tu app de Kick y vuelve a conectar Kick");
			}
			return ex;
		}

		private long ChannelUserId(string slug)
		{
			string url = ApiBase + "/v1/channels" + ((string.IsNullOrEmpty(slug) || slug.Equals(Account, StringComparison.OrdinalIgnoreCase)) ? "" : ("?slug=" + Uri.EscapeDataString(slug)));
			List<Dictionary<string, object>> list = Http.Arr(Api("GET", url, null, null), "data");
			if (list.Count == 0)
			{
				throw new ApiException(404, "el canal \"" + slug + "\" no existe");
			}
			long result;
			long.TryParse(Http.Str(list[0], "broadcaster_user_id"), out result);
			return result;
		}

		public void DeleteMessage(string messageId)
		{
			try
			{
				Api("DELETE", ApiBase + "/v1/chat/" + Uri.EscapeDataString(messageId), null, null);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// Quita un ban o una expulsión temporal.
		public void Unban(string slug, string targetUserId)
		{
			long b = ChannelUserId(slug);
			long uid;
			long.TryParse(targetUserId, out uid);
			Dictionary<string, object> body = new Dictionary<string, object>();
			body.Add("broadcaster_user_id", b);
			body.Add("user_id", uid);
			try
			{
				Api("DELETE", ApiBase + "/v1/moderation/bans", null, body);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}

		// seconds = 0: ban permanente. Kick cuenta las expulsiones en minutos (de 1 a 10080).
		public void Ban(string slug, string targetUserId, int seconds, string reason)
		{
			long b = ChannelUserId(slug);
			long uid;
			long.TryParse(targetUserId, out uid);
			Dictionary<string, object> body = new Dictionary<string, object>();
			body.Add("broadcaster_user_id", b);
			body.Add("user_id", uid);
			if (seconds > 0)
			{
				body.Add("duration", Math.Max(1, Math.Min(10080, (seconds + 59) / 60)));
			}
			if (!string.IsNullOrEmpty(reason))
			{
				body.Add("reason", reason.Length > 100 ? reason.Substring(0, 100) : reason);
			}
			try
			{
				Api("POST", ApiBase + "/v1/moderation/bans", null, body);
			}
			catch (ApiException ex)
			{
				throw ModError(ex);
			}
		}
	}
}
