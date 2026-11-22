using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace StreamDelay
{
	internal class ChatServer
	{
		private class Request
		{
			public string Method = "";

			public string Path = "";

			public string Body = "";

			public Dictionary<string, string> Headers = new Dictionary<string, string>();
		}

		private const int RecentMax = 80;

		public Action<string> Log = delegate
		{
		};

		private TcpListener listener;

		private volatile bool running;

		private readonly List<Stream> clients = new List<Stream>();

		private readonly object lk = new object();

		private readonly LinkedList<string> recent = new LinkedList<string>();

		private string lastViewers;

		private string lastStatus;

		private string pageHtml;

		private string delayHtml;

		private string viewersHtml;

		private string mandoHtml;

		// Código común de los overlays para aplicar el estilo (res\overlay-style.js).
		private string styleJs;

		// Mando a distancia (móvil, Stream Deck, panel de OBS): acción + parámetros -> JSON de estado.
		// Exige la clave del mando (?k=...) en cada petición.
		public Func<string, Dictionary<string, string>, string> OnRemote = (string a, Dictionary<string, string> q) => "{}";

		public Func<string> RemoteKey = () => "";

		// Moderación desde la página del chat.
		public Func<Dictionary<string, object>, Dictionary<string, object>> OnMod = (Dictionary<string, object> d) => new Dictionary<string, object>();

		// true: escucha también en la red local (para el móvil). Solo el mando responde desde fuera del PC.
		public bool Lan { get; private set; }

		// Espectadores y qué mostrar, para el overlay de espectadores.
		public Func<string> OnViewersState = () => "{}";

		// Estado del delay para el overlay de OBS (lo rellena la ventana principal).
		public Func<string> OnDelayState = () => "{}";

		// Estilo de los overlays (colores, forma, fuente…), lo leen las tres páginas.
		public Func<string> OnStyleState = () => "{}";

		// Estilo propio del chat (fuente, tamaño, colores, fondo…), lo lee la página del chat.
		public Func<string> OnChatStyle = () => "{}";

		// Activar una licencia desde la página de compra (vortexs.es/vortexs-live/activar): devuelve la
		// página que ve el navegador.
		public Func<string, string> OnLicense = (string key) => "";

		private readonly string secret = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

		public Func<Dictionary<string, object>> OnInfo = () => new Dictionary<string, object>();

		public Func<string, string[], Dictionary<string, object>> OnSend = (string t, string[] to) => new Dictionary<string, object>();

		// Igual, respondiendo a un mensaje de Twitch (texto, plataformas, id del mensaje).
		public Func<string, string[], string, Dictionary<string, object>> OnSendReply;

		// Emotes para el botón 😊 de la página del chat: { "groups": [{ "name", "items": [{ "n", "u" }] }], "hint" }.
		public Func<Dictionary<string, object>> OnEmotes = () => new Dictionary<string, object>();

		public int Port { get; private set; }

		public bool Running
		{
			get
			{
				return running;
			}
		}

		public string Url
		{
			get
			{
				return "http://127.0.0.1:" + Port + "/";
			}
		}

		public void Start(int port, bool lan)
		{
			Lan = lan;
			Start(port);
		}

		public void Start(int port)
		{
			if (running)
			{
				return;
			}
			using (Stream stream = typeof(ChatServer).Assembly.GetManifestResourceStream("StreamDelay.chat.html"))
			{
				using (MemoryStream memoryStream = new MemoryStream())
				{
					if (stream != null)
					{
						stream.CopyTo(memoryStream);
					}
					pageHtml = Encoding.UTF8.GetString(memoryStream.ToArray());
				}
			}
			using (Stream stream2 = typeof(ChatServer).Assembly.GetManifestResourceStream("StreamDelay.delay.html"))
			{
				using (MemoryStream memoryStream2 = new MemoryStream())
				{
					if (stream2 != null)
					{
						stream2.CopyTo(memoryStream2);
					}
					delayHtml = Encoding.UTF8.GetString(memoryStream2.ToArray());
				}
			}
			using (Stream stream3 = typeof(ChatServer).Assembly.GetManifestResourceStream("StreamDelay.viewers.html"))
			{
				using (MemoryStream memoryStream3 = new MemoryStream())
				{
					if (stream3 != null)
					{
						stream3.CopyTo(memoryStream3);
					}
					viewersHtml = Encoding.UTF8.GetString(memoryStream3.ToArray());
				}
			}
			using (Stream stream4 = typeof(ChatServer).Assembly.GetManifestResourceStream("StreamDelay.mando.html"))
			{
				using (MemoryStream memoryStream4 = new MemoryStream())
				{
					if (stream4 != null)
					{
						stream4.CopyTo(memoryStream4);
					}
					mandoHtml = Encoding.UTF8.GetString(memoryStream4.ToArray());
				}
			}
			using (Stream stream5 = typeof(ChatServer).Assembly.GetManifestResourceStream("StreamDelay.overlay-style.js"))
			{
				using (MemoryStream memoryStream5 = new MemoryStream())
				{
					if (stream5 != null)
					{
						stream5.CopyTo(memoryStream5);
					}
					styleJs = Encoding.UTF8.GetString(memoryStream5.ToArray());
				}
			}
			listener = new TcpListener(Lan ? IPAddress.Any : IPAddress.Loopback, port);
			Sockets.NoInherit(listener);   // si ffmpeg lo heredara, el 8935 seguiría ocupado al reabrir
			listener.Start();
			Port = port;
			running = true;
			Thread thread = new Thread(AcceptLoop);
			thread.IsBackground = true;
			thread.Name = "chat-web";
			thread.Start();
			Thread thread2 = new Thread(HeartbeatLoop);
			thread2.IsBackground = true;
			thread2.Name = "chat-web-ping";
			thread2.Start();
		}

		public void Stop()
		{
			running = false;
			try
			{
				if (listener != null)
				{
					listener.Stop();
				}
			}
			catch
			{
			}
			lock (lk)
			{
				foreach (Stream client in clients)
				{
					try
					{
						client.Close();
					}
					catch
					{
					}
				}
				clients.Clear();
			}
		}

		public void Send(ChatItem it)
		{
			// Los avisos (subs, raids, donaciones…) en el idioma elegido; los mensajes del chat, tal cual.
			if (it.t == "ev" && it.text != null)
			{
				it.text = I18n.Tr(it.text);
			}
			string text = Web.ToJson(it);
			lock (lk)
			{
				if (it.t == "viewers")
				{
					lastViewers = text;
				}
				else if (it.t == "status")
				{
					lastStatus = text;
				}
				else if (it.t == "clear")
				{
					// Chat borrado: las páginas que se abran después tampoco ven lo anterior.
					recent.Clear();
				}
				else
				{
					recent.AddLast(text);
					while (recent.Count > 80)
					{
						recent.RemoveFirst();
					}
				}
			}
			Broadcast("data: " + text + "\n\n");
		}

		private void Broadcast(string s)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(s);
			lock (lk)
			{
				// Hacia atrás, para poder quitar las páginas que ya se cerraron.
				for (int i = clients.Count - 1; i >= 0; i--)
				{
					try
					{
						clients[i].Write(bytes, 0, bytes.Length);
						clients[i].Flush();
					}
					catch
					{
						try
						{
							clients[i].Close();
						}
						catch
						{
						}
						clients.RemoveAt(i);
					}
				}
			}
		}

		private void HeartbeatLoop()
		{
			while (running)
			{
				for (int i = 0; i < 150; i++)
				{
					if (!running)
					{
						break;
					}
					Thread.Sleep(100);
				}
				if (running)
				{
					Broadcast(": ping\n\n");
				}
			}
		}

		private void AcceptLoop()
		{
			while (running)
			{
				TcpClient c;
				try
				{
					c = listener.AcceptTcpClient();
					Sockets.NoInherit(c);
				}
				catch
				{
					if (running)
					{
						Thread.Sleep(200);
					}
					continue;
				}
				Thread thread = new Thread((ThreadStart)delegate
				{
					Serve(c);
				});
				thread.IsBackground = true;
				thread.Start();
			}
		}

		private void Serve(TcpClient c)
		{
			try
			{
				c.NoDelay = true;
				NetworkStream stream = c.GetStream();
				stream.ReadTimeout = 10000;
				Request request = ReadRequest(stream);
				if (request == null)
				{
					c.Close();
					return;
				}
				string text = Header(request, "host").ToLowerInvariant();
				string pathOnly = request.Path.Split('?')[0];
				bool local = text == "127.0.0.1:" + Port || text == "localhost:" + Port;
				bool fromPc = IPAddress.IsLoopback(((IPEndPoint)c.Client.RemoteEndPoint).Address);
				if (pathOnly == "/mando" || pathOnly == "/mando/api")
				{
					// El mando se puede abrir desde el móvil, pero solo con la clave correcta.
					if ((!local && !Lan) || (!fromPc && !Lan))
					{
						Respond(stream, "403 Forbidden", "text/plain", "");
						c.Close();
						return;
					}
					Mando(stream, request, pathOnly);
					c.Close();
					return;
				}
				if (!local || !fromPc)
				{
					Respond(stream, "403 Forbidden", "text/plain", "");
					c.Close();
					return;
				}
				string path = request.Path;
				int query = path.IndexOf('?');
				string route = ((query >= 0) ? path.Substring(0, query) : path);
				if (route.StartsWith("/api/"))
				{
					Api(stream, request, route);
					c.Close();
					return;
				}
				if (request.Method != "GET")
				{
					Respond(stream, "405 Method Not Allowed", "text/plain", "");
					c.Close();
					return;
				}
				switch (route)
				{
				case "/events":
				{
					// Server-Sent Events: la página se queda conectada y recibe cada mensaje. Al entrar
					// se le manda el último estado, los espectadores y los mensajes recientes.
					Write(stream, "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream; charset=utf-8\r\nCache-Control: no-cache\r\nConnection: keep-alive\r\n\r\nretry: 2000\n\n");
					StringBuilder backlog = new StringBuilder();
					lock (lk)
					{
						if (lastStatus != null)
						{
							backlog.Append("data: ").Append(lastStatus).Append("\n\n");
						}
						if (lastViewers != null)
						{
							backlog.Append("data: ").Append(lastViewers).Append("\n\n");
						}
						foreach (string item in recent)
						{
							backlog.Append("data: ").Append(item).Append("\n\n");
						}
						byte[] bytes = Encoding.UTF8.GetBytes(backlog.ToString());
						stream.Write(bytes, 0, bytes.Length);
						stream.Flush();
						clients.Add(stream);
					}
					stream.ReadTimeout = -1;
					return;
				}
				case "/delay":
				case "/overlay-delay":
					Respond(stream, "200 OK", "text/html; charset=utf-8", delayHtml.Replace("__SD_LANG__", I18n.Lang));
					break;
				case "/viewers":
				case "/espectadores":
					Respond(stream, "200 OK", "text/html; charset=utf-8", viewersHtml.Replace("__SD_LANG__", I18n.Lang));
					break;
				case "/viewers.json":
				{
					string vs;
					try
					{
						vs = OnViewersState();
					}
					catch
					{
						vs = "{}";
					}
					Respond(stream, "200 OK", "application/json; charset=utf-8", vs);
					break;
				}
				case "/overlay-style.js":
					Respond(stream, "200 OK", "application/javascript; charset=utf-8", styleJs ?? "");
					break;
				case "/style.json":
				{
					string style;
					try
					{
						style = OnStyleState();
					}
					catch
					{
						style = "{}";
					}
					Respond(stream, "200 OK", "application/json; charset=utf-8", style);
					break;
				}
				case "/chatstyle.json":
				{
					string cs;
					try
					{
						cs = OnChatStyle();
					}
					catch
					{
						cs = "{}";
					}
					Respond(stream, "200 OK", "application/json; charset=utf-8", cs);
					break;
				}
				case "/licencia":
				case "/activar":
				{
					Dictionary<string, string> lq = Query(request.Path);
					string lk2;
					lq.TryGetValue("key", out lk2);
					Respond(stream, "200 OK", "text/html; charset=utf-8", OnLicense((lk2 ?? "").Trim()));
					break;
				}
				case "/delay.json":
				{
					string st;
					try
					{
						st = OnDelayState();
					}
					catch
					{
						st = "{}";
					}
					Respond(stream, "200 OK", "application/json; charset=utf-8", st);
					break;
				}
				case "/":
				case "/index.html":
				case "/chat":
					Respond(stream, "200 OK", "text/html; charset=utf-8", pageHtml.Replace("__SD_SECRET__", secret).Replace("__SD_LANG__", I18n.Lang));
					break;
				default:
					Respond(stream, "404 Not Found", "text/plain", "");
					break;
				}
				c.Close();
			}
			catch
			{
				try
				{
					c.Close();
				}
				catch
				{
				}
			}
		}

		private void Api(Stream s, Request req, string p)
		{
			string text = Header(req, "origin").ToLowerInvariant();
			if ((text.Length != 0 && !(text == "http://127.0.0.1:" + Port) && !(text == "http://localhost:" + Port)) || Header(req, "x-sd-secret") != secret)
			{
				Respond(s, "403 Forbidden", "text/plain", "");
				return;
			}
			object o;
			try
			{
				if (p == "/api/info" && req.Method == "GET")
				{
					o = OnInfo();
				}
				else if (p == "/api/emotes" && req.Method == "GET")
				{
					// Sin TrValues: los nombres de los emotes no se traducen.
					Dictionary<string, object> em = OnEmotes();
					object hint;
					if (em.TryGetValue("hint", out hint) && hint is string)
					{
						em["hint"] = I18n.Tr((string)hint);
					}
					Respond(s, "200 OK", "application/json; charset=utf-8", Web.ToJson(em));
					return;
				}
				else
				{
					if (p == "/api/mod" && req.Method == "POST")
					{
						Dictionary<string, object> md = Web.Parse(req.Body) as Dictionary<string, object>;
						if (md == null)
						{
							Respond(s, "400 Bad Request", "text/plain", "");
							return;
						}
						Respond(s, "200 OK", "application/json; charset=utf-8", Web.ToJson(TrValues(OnMod(md))));
						return;
					}
					if (!(p == "/api/send") || !(req.Method == "POST"))
					{
						Respond(s, "404 Not Found", "text/plain", "");
						return;
					}
					// Escribir en los chats: { "text": "…", "to": ["twitch", "kick", …] }.
					object body = Web.Parse(req.Body);
					string message = J.S(body, "text").Trim();
					List<string> targets = new List<string>();
					foreach (object item in J.L(body, "to"))
					{
						targets.Add(Convert.ToString(item));
					}
					if (message.Length == 0 || targets.Count == 0)
					{
						Respond(s, "400 Bad Request", "text/plain", "");
						return;
					}
					// "reply": id del mensaje de Twitch al que se responde (opcional).
					string reply = J.S(body, "reply");
					o = (reply.Length > 0 && OnSendReply != null) ? OnSendReply(message, targets.ToArray(), reply) : OnSend(message, targets.ToArray());
				}
			}
			catch (Exception ex)
			{
				Dictionary<string, object> failure = new Dictionary<string, object>();
				failure.Add("error", ex.Message);
				o = failure;
			}
			Respond(s, "200 OK", "application/json; charset=utf-8", Web.ToJson(TrValues(o)));
		}

		// Respuestas para la página: los textos (errores, "mensaje borrado"…) en el idioma elegido.
		private static object TrValues(object o)
		{
			Dictionary<string, object> d = o as Dictionary<string, object>;
			if (d == null)
			{
				return o;
			}
			Dictionary<string, object> res = new Dictionary<string, object>();
			foreach (KeyValuePair<string, object> kv in d)
			{
				string text = kv.Value as string;
				res[kv.Key] = (text != null && text != "ok") ? I18n.Tr(text) : kv.Value;
			}
			return res;
		}

		private static Dictionary<string, string> Query(string path)
		{
			Dictionary<string, string> d = new Dictionary<string, string>();
			int i = path.IndexOf('?');
			if (i < 0)
			{
				return d;
			}
			foreach (string kv in path.Substring(i + 1).Split('&'))
			{
				if (kv.Length == 0)
				{
					continue;
				}
				int e = kv.IndexOf('=');
				string k = Uri.UnescapeDataString((e < 0 ? kv : kv.Substring(0, e)).Replace('+', ' '));
				string v = (e < 0) ? "" : Uri.UnescapeDataString(kv.Substring(e + 1).Replace('+', ' '));
				d[k.ToLowerInvariant()] = v;
			}
			return d;
		}

		private static bool SameKey(string a, string b)
		{
			if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a.Length != b.Length)
			{
				return false;
			}
			int diff = 0;
			for (int i = 0; i < a.Length; i++)
			{
				diff |= a[i] ^ b[i];
			}
			return diff == 0;
		}

		private void Mando(Stream s, Request req, string p)
		{
			if (req.Method != "GET" && req.Method != "POST")
			{
				Respond(s, "405 Method Not Allowed", "text/plain", "");
				return;
			}
			Dictionary<string, string> q = Query(req.Path);
			string k;
			q.TryGetValue("k", out k);
			if (!SameKey((k ?? "").Trim().ToLowerInvariant(), RemoteKey()))
			{
				if (p == "/mando")
				{
					Respond(s, "403 Forbidden", "text/html; charset=utf-8", "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width'><body style='background:#09090c;color:#f4f4f5;font:16px system-ui;padding:24px'><h2>Clave del mando incorrecta</h2><p>Copia de nuevo la URL en Vortexs Live → pestaña Mando.</p>");
				}
				else
				{
					Respond(s, "403 Forbidden", "application/json; charset=utf-8", "{\"error\":\"clave incorrecta\"}");
				}
				return;
			}
			if (p == "/mando")
			{
				Respond(s, "200 OK", "text/html; charset=utf-8", mandoHtml.Replace("__SD_LANG__", I18n.Lang));
				return;
			}
			string a;
			if (!q.TryGetValue("a", out a) || a.Length == 0)
			{
				a = "estado";
			}
			string json;
			try
			{
				json = OnRemote(a.ToLowerInvariant(), q);
			}
			catch (Exception ex)
			{
				json = "{\"error\":" + Web.ToJson(ex.Message) + "}";
			}
			// Abierto en una pestaña del navegador (p. ej. el Stream Deck sin «Acceder en segundo plano»):
			// la acción ya está hecha; se explica cómo evitar la pestaña y se intenta cerrar sola.
			if (Header(req, "sec-fetch-mode") == "navigate")
			{
				string done = "";
				try
				{
					done = J.S(Web.Parse(json), "msg");
				}
				catch
				{
				}
				string title = I18n.Tr("Hecho") + (done.Length > 0 ? (": " + I18n.Tr(done)) : "");
				string tip = I18n.Tr("Puedes cerrar esta pestaña. Para que el Stream Deck no abra el navegador, usa la acción «Tecla de acceso rápido» con el atajo de Vortexs Live (pestaña Mando → Stream Deck) o, en «Sitio web», marca «Acceder en segundo plano».");
				Respond(s, "200 OK", "text/html; charset=utf-8", "<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width'><title>Vortexs Live</title><body style='background:#09090c;color:#f4f4f5;font:16px system-ui;padding:24px;max-width:560px'><h2 style='margin:0 0 8px'>✓ " + WebUtility.HtmlEncode(title) + "</h2><p style='color:#a1a1aa;line-height:1.5'>" + WebUtility.HtmlEncode(tip) + "</p><script>setTimeout(function(){window.close()},1500)</script>");
				return;
			}
			Respond(s, "200 OK", "application/json; charset=utf-8", json);
		}

		private static string Header(Request r, string k)
		{
			string value;
			if (!r.Headers.TryGetValue(k, out value))
			{
				return "";
			}
			return value.Trim();
		}

		// Lee una petición HTTP: cabecera (hasta la línea vacía, como mucho 16 KB) y cuerpo (como mucho 64 KB).
		// null si la petición no es válida o la conexión se cierra.
		private static Request ReadRequest(Stream s)
		{
			List<byte> head = new List<byte>();
			byte[] one = new byte[1];
			while (head.Count < 16384)
			{
				if (s.Read(one, 0, 1) <= 0)
				{
					return null;
				}
				head.Add(one[0]);
				int count = head.Count;
				// Fin de la cabecera: \r\n\r\n.
				if (count >= 4 && head[count - 1] == 10 && head[count - 2] == 13 && head[count - 3] == 10 && head[count - 4] == 13)
				{
					break;
				}
			}
			string[] lines = Encoding.UTF8.GetString(head.ToArray()).Split(new string[1] { "\r\n" }, StringSplitOptions.None);
			// Primera línea: "GET /ruta HTTP/1.1".
			string[] requestLine = lines[0].Split(' ');
			if (requestLine.Length < 2)
			{
				return null;
			}
			Request request = new Request
			{
				Method = requestLine[0].ToUpperInvariant(),
				Path = requestLine[1]
			};
			for (int i = 1; i < lines.Length; i++)
			{
				int colon = lines[i].IndexOf(':');
				if (colon > 0)
				{
					request.Headers[lines[i].Substring(0, colon).Trim().ToLowerInvariant()] = lines[i].Substring(colon + 1).Trim();
				}
			}
			int length;
			if (int.TryParse(Header(request, "content-length"), out length) && length > 0)
			{
				if (length > 65536)
				{
					return null;
				}
				byte[] body = new byte[length];
				int got;
				for (int j = 0; j < length; j += got)
				{
					got = s.Read(body, j, length - j);
					if (got <= 0)
					{
						return null;
					}
				}
				request.Body = Encoding.UTF8.GetString(body);
			}
			return request;
		}

		private static void Respond(Stream s, string status, string type, string body)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(body);
			Write(s, "HTTP/1.1 " + status + "\r\nContent-Type: " + type + "\r\nCache-Control: no-cache\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
			s.Write(bytes, 0, bytes.Length);
			s.Flush();
		}

		private static void Write(Stream s, string text)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(text);
			s.Write(bytes, 0, bytes.Length);
		}
	}
}
