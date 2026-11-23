// Base de las conexiones con Twitch, YouTube y Kick (Directo → Título y categoría, chat y moderación).
// Guarda la app (ID y secreto de cliente) y los tokens de acceso, los renueva cuando caducan y
// hace las llamadas a la API. Cada plataforma pone su forma de iniciar sesión y sus llamadas.
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal abstract class Platform
	{
		public string ClientId = "";

		public string ClientSecret = "";

		public bool Enabled = true;

		// Token de acceso, token para renovarlo y cuándo caduca el de acceso.
		protected string access = "";

		protected string refresh = "";

		protected DateTime expires = DateTime.MinValue;

		public string Account = "";

		// Últimos resultados de buscar categoría y la elegida.
		public List<Category> Results = new List<Category>();

		public Category Chosen;

		public string LastError;

		private readonly object tokenLock = new object();

		public abstract string Name { get; }

		// Prefijo de sus ajustes en VortexsLive.ini ("twitch", "kick"…).
		public abstract string Key { get; }

		public abstract bool NeedsSecret { get; }

		// Panel de desarrolladores donde se crea la app.
		public abstract string ConsoleUrl { get; }

		public virtual int TitleMax
		{
			get
			{
				return 140;
			}
		}

		// Tiene los datos de la app.
		public bool Configured
		{
			get
			{
				if (ClientId.Length > 0)
				{
					if (NeedsSecret)
					{
						return ClientSecret.Length > 0;
					}
					return true;
				}
				return false;
			}
		}

		// Ha iniciado sesión.
		public bool Connected
		{
			get
			{
				if (refresh.Length <= 0)
				{
					return access.Length > 0;
				}
				return true;
			}
		}

		public abstract void Login(ILoginUi ui);

		public abstract List<Category> Search(string q);

		public abstract void ReadCurrent(out string title, out Category cat);

		public abstract void Update(string title, Category cat);

		// Pide un token de acceso nuevo con el de renovación.
		protected abstract void DoRefresh();

		// Lee el nombre de la cuenta conectada.
		protected abstract void LoadAccount();

		public void Logout()
		{
			access = (refresh = (Account = ""));
			expires = DateTime.MinValue;
			Chosen = null;
			Results.Clear();
		}

		// Guarda los tokens de una respuesta OAuth (caducan un minuto antes de lo que dice, por si acaso).
		protected void SetTokens(Dictionary<string, object> r)
		{
			string newAccess = Http.Str(r, "access_token");
			if (newAccess.Length == 0)
			{
				throw new ApiException(0, "la plataforma no devolvió el acceso");
			}
			access = newAccess;
			string newRefresh = Http.Str(r, "refresh_token");
			if (newRefresh.Length > 0)
			{
				refresh = newRefresh;
			}
			int seconds = Http.Int(r, "expires_in", 3600);
			expires = DateTime.UtcNow.AddSeconds(Math.Max(60, seconds - 60));
		}

		// Token de acceso válido (lo renueva si ha caducado). Si la plataforma ya no acepta la
		// renovación, cierra la sesión y pide volver a conectar.
		protected string Token()
		{
			lock (tokenLock)
			{
				if (access.Length == 0 || DateTime.UtcNow >= expires)
				{
					if (refresh.Length == 0)
					{
						throw new ApiException(401, "sesión caducada: vuelve a conectar " + Name);
					}
					try
					{
						DoRefresh();
					}
					catch (ApiException ex)
					{
						if (ex.Status == 400 || ex.Status == 401)
						{
							Logout();
							throw new ApiException(401, "sesión caducada: vuelve a conectar " + Name);
						}
						throw;
					}
				}
				return access;
			}
		}

		// Llamada a la API con el token. Reintenta una vez: si falla la red en una lectura (GET), o si
		// la plataforma dice 401 (se fuerza a renovar el token).
		protected Dictionary<string, object> Api(string method, string url, Dictionary<string, string> headers, object body)
		{
			string json = ((body == null) ? null : Http.ToJson(body));
			try
			{
				return Http.Send(method, url, Token(), headers, "application/json", json);
			}
			catch (ApiException ex)
			{
				if (ex.Status == 0 && method == "GET")
				{
					Thread.Sleep(700);
					return Http.Send(method, url, Token(), headers, "application/json", json);
				}
				if (ex.Status != 401 || refresh.Length == 0)
				{
					throw;
				}
				lock (tokenLock)
				{
					expires = DateTime.MinValue;
				}
				return Http.Send(method, url, Token(), headers, "application/json", json);
			}
		}

		public void FinishLogin()
		{
			LoadAccount();
		}

		// ---------- VortexsLive.ini ----------
		// Los tokens van juntos (acceso, renovación y caducidad) y cifrados, igual que el secreto.

		public void Load(Func<string, string> get)
		{
			ClientId = get(Key + "_client");
			ClientSecret = Secret.Unprotect(get(Key + "_secret"));
			Enabled = get(Key + "_on") != "0";
			Account = get(Key + "_account");
			string tokens = Secret.Unprotect(get(Key + "_token"));
			string[] parts = tokens.Split('\n');
			if (parts.Length >= 3)
			{
				access = parts[0];
				refresh = parts[1];
				long ticks;
				expires = (long.TryParse(parts[2], out ticks) ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue);
			}
			string catId = get(Key + "_cat_id");
			string catName = get(Key + "_cat_name");
			if (catId.Length > 0 && catName.Length > 0)
			{
				Chosen = new Category(catId, catName);
			}
		}

		public void Save(List<string> lines)
		{
			lines.Add(Key + "_client=" + ClientId);
			lines.Add(Key + "_secret=" + Secret.Protect(ClientSecret));
			lines.Add(Key + "_on=" + (Enabled ? "1" : "0"));
			lines.Add(Key + "_account=" + Account.Replace("\n", " "));
			lines.Add(Key + "_token=" + (Connected ? Secret.Protect(access + "\n" + refresh + "\n" + expires.Ticks) : ""));
			lines.Add(Key + "_cat_id=" + ((Chosen != null) ? Chosen.Id : ""));
			lines.Add(Key + "_cat_name=" + ((Chosen != null) ? Chosen.Name.Replace("\n", " ") : ""));
		}

		// ---------- Utilidades ----------

		// La categoría que mejor coincide con lo escrito: igual, luego que empiece igual, y si no la primera.
		public static Category Best(List<Category> list, string q)
		{
			if (list.Count == 0)
			{
				return null;
			}
			string wanted = Norm(q);
			foreach (Category item in list)
			{
				if (Norm(item.Name) == wanted)
				{
					return item;
				}
			}
			foreach (Category item in list)
			{
				if (Norm(item.Name).StartsWith(wanted))
				{
					return item;
				}
			}
			return list[0];
		}

		// Solo letras y números, en minúsculas ("Grand Theft Auto V" → "grandtheftautov").
		public static string Norm(string s)
		{
			StringBuilder sb = new StringBuilder();
			string lower = (s ?? "").ToLowerInvariant();
			foreach (char c in lower)
			{
				if (char.IsLetterOrDigit(c))
				{
					sb.Append(c);
				}
			}
			return sb.ToString();
		}

		// Inicio de sesión con código en otro dispositivo (Twitch, YouTube): pregunta cada "interval"
		// segundos si el usuario ya lo ha aprobado, hasta que caduque el código.
		protected Dictionary<string, object> PollDevice(ILoginUi ui, string tokenUrl, string[] form, int interval, int expiresIn)
		{
			DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn));
			int every = Math.Max(2, interval);
			while (DateTime.UtcNow < deadline)
			{
				for (int i = 0; i < every * 10; i++)
				{
					if (ui.Cancelled)
					{
						throw new OperationCanceledException();
					}
					Thread.Sleep(100);
				}
				try
				{
					return Http.Send("POST", tokenUrl, null, null, "application/x-www-form-urlencoded", Http.Form(form));
				}
				catch (ApiException ex)
				{
					string message = ex.Message.ToLowerInvariant();
					if (message.Contains("authorization_pending") || message.Contains("pending"))
					{
						continue;
					}
					if (message.Contains("slow_down"))
					{
						// La plataforma pide preguntar menos a menudo.
						every += 5;
						continue;
					}
					if (message.Contains("denied"))
					{
						throw new ApiException(ex.Status, "has rechazado el acceso");
					}
					if (message.Contains("expired"))
					{
						throw new ApiException(ex.Status, "el código ha caducado, vuelve a intentarlo");
					}
					throw;
				}
			}
			throw new ApiException(0, "el código ha caducado, vuelve a intentarlo");
		}
	}
}
