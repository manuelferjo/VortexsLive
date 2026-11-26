// Anunciar el directo (Ajustes → Anuncio). Gratis, sin la API de pago de X:
//   1. Al empezar a emitir en Twitch, lee el título y se queda con su principio (XpText.Summarize).
//   2. Publica en X con un Chrome (o Edge) propio y oculto (XpBrowser, por el protocolo DevTools) en el que el
//      usuario inició sesión una vez: el post con el título y, con el botón «+», una respuesta en el
//      hilo con el enlace de Twitch.
//   3. Hace una captura del post y monta dos historias de 1080×1920 (XpStory): la de TikTok con el
//      texto «Twitch: canal» encima y la de Instagram libre abajo para el widget de enlace.
//   4. Guarda todo en Imágenes\Vortexs Live\Anuncios y, si hay bot de Telegram, lo manda al móvil
//      con el enlace listo para copiar (las historias con widget no se pueden subir por API).
// Si el directo se corta y vuelve en menos de 20 minutos, no se vuelve a anunciar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace StreamDelay
{
	// ======================= Textos del anuncio =======================
	internal static class XpText
	{
		// El principio del título: sin !comandos ni enlaces, hasta el primer separador ( | - // • …)
		// y como mucho max caracteres (cortando por una palabra).
		public static string Summarize(string title, int max)
		{
			string t = (title ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
			StringBuilder sb = new StringBuilder();
			foreach (string w in t.Split(' '))
			{
				if (w.Length == 0)
				{
					continue;
				}
				if (w.Length > 1 && w[0] == '!')
				{
					continue;
				}
				string lw = w.ToLowerInvariant();
				if (lw.StartsWith("http://") || lw.StartsWith("https://") || lw.StartsWith("www."))
				{
					continue;
				}
				if (sb.Length > 0)
				{
					sb.Append(' ');
				}
				sb.Append(w);
			}
			t = sb.ToString();
			string[] seps = new string[11] { " | ", "|", " // ", " - ", " — ", " – ", " • ", " · ", "║", " ~ ", " ❘ " };
			int cut = -1;
			foreach (string sep in seps)
			{
				int i = t.IndexOf(sep, StringComparison.Ordinal);
				if (i >= 8 && (cut < 0 || i < cut))
				{
					cut = i;
				}
			}
			if (cut > 0)
			{
				t = t.Substring(0, cut);
			}
			t = t.Trim(' ', '-', '|', '/', '•', '·', ',', ';', ':');
			if (t.Length > max)
			{
				int len = max - 1;
				int sp = t.LastIndexOf(' ', len);
				if (sp > max / 2)
				{
					len = sp;
				}
				if (len > 0 && char.IsHighSurrogate(t[len - 1]))
				{
					len--;
				}
				t = t.Substring(0, len).TrimEnd(' ', ',', ';', ':', '.', '-', '|') + "…";
			}
			return t;
		}

		// Plantilla con {titulo}, {titulo_completo}, {enlace}, {categoria} y \n para saltar de línea.
		public static string Fill(string tpl, string summary, string full, string link, string cat)
		{
			return (tpl ?? "").Replace("\\n", "\n").Replace("{titulo_completo}", full ?? "").Replace("{titulo}", summary ?? "").Replace("{enlace}", link ?? "").Replace("{categoria}", cat ?? "").Trim();
		}
	}

	// ======================= Chrome (o Edge) controlado por DevTools =======================
	internal class XpBrowser : IDisposable
	{
		private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

		private Process proc;

		private ClientWebSocket ws;

		private int nextId = 1;

		private readonly byte[] buf = new byte[1 << 20];

		// Perfil propio (no toca el Chrome normal): aquí se queda la sesión de X.
		public static string ProfileDir
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VortexsLive", "navegador-x");
			}
		}

		public static string FindBrowser()
		{
			List<string> roots = new List<string>();
			foreach (string v in new string[3] { "ProgramFiles(x86)", "ProgramFiles", "LocalAppData" })
			{
				string r = Environment.GetEnvironmentVariable(v);
				if (!string.IsNullOrEmpty(r))
				{
					roots.Add(r);
				}
			}
			// Chrome primero; si no está, Edge (viene con Windows).
			foreach (string r in roots)
			{
				string p = Path.Combine(r, "Google", "Chrome", "Application", "chrome.exe");
				if (File.Exists(p))
				{
					return p;
				}
			}
			foreach (string r in roots)
			{
				string p = Path.Combine(r, "Microsoft", "Edge", "Application", "msedge.exe");
				if (File.Exists(p))
				{
					return p;
				}
			}
			return null;
		}

		// Ventana normal para iniciar sesión en X (una vez). Devuelve el proceso para poder cerrarlo.
		public static Process OpenLogin()
		{
			string exe = FindBrowser();
			if (exe == null)
			{
				throw new Exception("no se encuentra Google Chrome ni Microsoft Edge en este PC");
			}
			Directory.CreateDirectory(ProfileDir);
			ProcessStartInfo si = new ProcessStartInfo(exe, "--user-data-dir=\"" + ProfileDir + "\" --no-first-run --no-default-browser-check --new-window https://x.com/i/flow/login");
			si.UseShellExecute = false;
			return Process.Start(si);
		}

		private static int FreePort()
		{
			TcpListener l = new TcpListener(IPAddress.Loopback, 0);
			l.Start();
			int p = ((IPEndPoint)l.LocalEndpoint).Port;
			l.Stop();
			return p;
		}

		private static string Get(string url)
		{
			using (WebClient wc = new WebClient())
			{
				wc.Proxy = null;
				wc.Encoding = Encoding.UTF8;
				return wc.DownloadString(url);
			}
		}

		// Abre el navegador sin ventana y se conecta a su pestaña.
		public void Start()
		{
			string exe = FindBrowser();
			if (exe == null)
			{
				throw new Exception("no se encuentra Google Chrome ni Microsoft Edge en este PC");
			}
			Directory.CreateDirectory(ProfileDir);
			int port = FreePort();
			ProcessStartInfo si = new ProcessStartInfo(exe, "--headless=new --remote-debugging-port=" + port + " --user-data-dir=\"" + ProfileDir + "\" --no-first-run --no-default-browser-check --disable-extensions --mute-audio --window-size=1000,1600 about:blank");
			si.UseShellExecute = false;
			si.CreateNoWindow = true;
			proc = Process.Start(si);
			string wsUrl = null;
			DateTime end = DateTime.UtcNow.AddSeconds(25);
			while (wsUrl == null && DateTime.UtcNow < end)
			{
				Thread.Sleep(400);
				if (proc.HasExited)
				{
					throw new Exception("el navegador se cerró al abrirlo: cierra la ventana de X de Vortexs Live si la tienes abierta y vuelve a probar");
				}
				try
				{
					foreach (object t in J.L(Json.DeserializeObject(Get("http://127.0.0.1:" + port + "/json/list"))))
					{
						if (J.S(t, "type") == "page" && J.S(t, "webSocketDebuggerUrl").Length > 0)
						{
							wsUrl = J.S(t, "webSocketDebuggerUrl");
							break;
						}
					}
				}
				catch
				{
				}
			}
			if (wsUrl == null)
			{
				throw new Exception("el navegador no responde: cierra la ventana de X de Vortexs Live si la tienes abierta y vuelve a probar");
			}
			ws = new ClientWebSocket();
			if (!ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None).Wait(15000) || ws.State != WebSocketState.Open)
			{
				throw new Exception("no se pudo conectar con el navegador");
			}
			// Sin «Headless» en el agente de usuario: X lo ve como un navegador normal.
			string ua = J.S(Call("Browser.getVersion", null, 10000), "userAgent").Replace("HeadlessEdg", "Edg").Replace("HeadlessChrome", "Chrome");
			if (ua.Length > 0)
			{
				Call("Emulation.setUserAgentOverride", P("userAgent", ua, "acceptLanguage", "es-ES,es"), 10000);
			}
			Call("Emulation.setDeviceMetricsOverride", P("width", 1000, "height", 1600, "deviceScaleFactor", 2, "mobile", false), 10000);
		}

		public static Dictionary<string, object> P(params object[] kv)
		{
			Dictionary<string, object> d = new Dictionary<string, object>();
			for (int i = 0; i + 1 < kv.Length; i += 2)
			{
				d[(string)kv[i]] = kv[i + 1];
			}
			return d;
		}

		// Manda una orden y espera su respuesta (los eventos que lleguen mientras tanto se ignoran).
		public Dictionary<string, object> Call(string method, Dictionary<string, object> prms, int timeoutMs)
		{
			int id = nextId++;
			Dictionary<string, object> msg = new Dictionary<string, object>();
			msg["id"] = id;
			msg["method"] = method;
			msg["params"] = prms ?? new Dictionary<string, object>();
			byte[] bytes = Encoding.UTF8.GetBytes(Json.Serialize(msg));
			if (!ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(15000))
			{
				throw new Exception("el navegador no responde");
			}
			DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (true)
			{
				string text = Receive(end);
				Dictionary<string, object> d = Json.DeserializeObject(text) as Dictionary<string, object>;
				if (d == null || !d.ContainsKey("id") || Convert.ToInt32(d["id"]) != id)
				{
					continue;
				}
				if (d.ContainsKey("error"))
				{
					throw new Exception("navegador: " + J.S(d, "error", "message"));
				}
				return (d.ContainsKey("result") ? (d["result"] as Dictionary<string, object>) : null) ?? new Dictionary<string, object>();
			}
		}

		private string Receive(DateTime end)
		{
			MemoryStream ms = new MemoryStream();
			while (true)
			{
				int left = (int)Math.Max(1.0, (end - DateTime.UtcNow).TotalMilliseconds);
				if (DateTime.UtcNow >= end)
				{
					throw new Exception("el navegador tarda demasiado");
				}
				WebSocketReceiveResult r;
				using (CancellationTokenSource cts = new CancellationTokenSource(left))
				{
					try
					{
						r = ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token).Result;
					}
					catch (AggregateException)
					{
						throw new Exception(cts.IsCancellationRequested ? "el navegador tarda demasiado" : "se perdió la conexión con el navegador");
					}
				}
				if (r.MessageType == WebSocketMessageType.Close)
				{
					throw new Exception("el navegador se ha cerrado");
				}
				ms.Write(buf, 0, r.Count);
				if (r.EndOfMessage)
				{
					return Encoding.UTF8.GetString(ms.ToArray());
				}
			}
		}

		// Ejecuta JavaScript en la página y devuelve el resultado.
		public object Eval(string js)
		{
			Dictionary<string, object> r = Call("Runtime.evaluate", P("expression", js, "returnByValue", true, "awaitPromise", true), 30000);
			if (r.ContainsKey("exceptionDetails"))
			{
				string m = J.S(r, "exceptionDetails", "exception", "description");
				throw new Exception("error en la página de X" + ((m.Length > 0) ? (": " + m.Split('\n')[0]) : ""));
			}
			return J.At(r, "result", "value");
		}

		public bool EvalBool(string js)
		{
			object v = Eval(js);
			return v is bool && (bool)v;
		}

		public static string Q(string s)
		{
			return Json.Serialize(s ?? "");
		}

		// Espera a que la condición (JavaScript) sea verdadera.
		public bool WaitFor(string cond, int ms)
		{
			DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
			while (DateTime.UtcNow < end)
			{
				try
				{
					if (EvalBool("!!(" + cond + ")"))
					{
						return true;
					}
				}
				catch
				{
				}
				Thread.Sleep(400);
			}
			return false;
		}

		public void Go(string url)
		{
			Call("Page.navigate", P("url", url), 30000);
			Thread.Sleep(800);
			WaitFor("document.readyState === 'complete' || document.readyState === 'interactive'", 20000);
		}

		// Escribe como si fuera el teclado (Enter para los saltos de línea).
		public void Type(string text)
		{
			string[] parts = (text ?? "").Replace("\r", "").Split('\n');
			for (int i = 0; i < parts.Length; i++)
			{
				if (i > 0)
				{
					Call("Input.dispatchKeyEvent", P("type", "keyDown", "key", "Enter", "code", "Enter", "windowsVirtualKeyCode", 13, "nativeVirtualKeyCode", 13, "text", "\r"), 10000);
					Call("Input.dispatchKeyEvent", P("type", "keyUp", "key", "Enter", "code", "Enter", "windowsVirtualKeyCode", 13, "nativeVirtualKeyCode", 13), 10000);
				}
				if (parts[i].Length > 0)
				{
					Call("Input.insertText", P("text", parts[i]), 10000);
				}
				Thread.Sleep(250);
			}
		}

		public bool HasCookie(string url, string name)
		{
			foreach (object c in J.L(Call("Network.getCookies", P("urls", new string[1] { url }), 10000), "cookies"))
			{
				if (J.S(c, "name") == name)
				{
					return true;
				}
			}
			return false;
		}

		// Captura de la zona (en píxeles de la página) como PNG.
		public byte[] Shot(double x, double y, double w, double h)
		{
			Dictionary<string, object> clip = P("x", x, "y", y, "width", w, "height", h, "scale", 1);
			Dictionary<string, object> r = Call("Page.captureScreenshot", P("format", "png", "clip", clip, "captureBeyondViewport", true), 60000);
			return Convert.FromBase64String(J.S(r, "data"));
		}

		public void Dispose()
		{
			try
			{
				if (ws != null && ws.State == WebSocketState.Open)
				{
					// Cerrar bien guarda las cookies (la sesión de X).
					try
					{
						Call("Browser.close", null, 5000);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
			try
			{
				if (ws != null)
				{
					ws.Abort();
				}
			}
			catch
			{
			}
			try
			{
				if (proc != null && !proc.WaitForExit(6000))
				{
					proc.Kill();
				}
			}
			catch
			{
			}
		}
	}

	// ======================= Publicar en X =======================
	internal static class XpPoster
	{
		public class Result
		{
			public string Url = "";

			public byte[] Png;
		}

		private const string Box0 = "document.querySelector('[data-testid=\"tweetTextarea_0\"]')";

		private const string Box1 = "document.querySelector('[data-testid=\"tweetTextarea_1\"]')";

		// Publica el post y, si hay reply, lo añade al hilo con el «+». Devuelve el enlace y la captura.
		// dryRun: lo escribe todo pero no pulsa «Postear»; la captura es la de la ventana de escribir.
		public static Result Post(string main, string reply, Action<string> status, bool dryRun)
		{
			Result res = new Result();
			using (XpBrowser b = new XpBrowser())
			{
				status("Abriendo X…");
				b.Start();
				b.Go("https://x.com/compose/post");
				if (!b.HasCookie("https://x.com", "auth_token"))
				{
					throw new Exception("no has iniciado sesión en X: pulsa «Iniciar sesión en X» en Ajustes → Anuncio");
				}
				if (!b.WaitFor(Box0, 30000))
				{
					throw new Exception("no se abrió la ventana para escribir en X (¿ha cambiado la web o pide verificar la cuenta?)");
				}
				string handle = (b.Eval("(function(){var a=document.querySelector('[data-testid=\"AppTabBar_Profile_Link\"]');return a?a.getAttribute('href'):'';})()") as string) ?? "";
				status("Escribiendo el post…");
				b.Eval("(function(){var e=" + Box0 + ";e.focus();e.click();return true;})()");
				Thread.Sleep(400);
				b.Type(main);
				if (!string.IsNullOrEmpty(reply))
				{
					// El «+» añade otro post al hilo.
					if (!b.WaitFor("document.querySelector('[data-testid=\"addButton\"]') && !document.querySelector('[data-testid=\"addButton\"]').disabled", 10000))
					{
						throw new Exception("no aparece el botón + para añadir al hilo");
					}
					b.Eval("document.querySelector('[data-testid=\"addButton\"]').click();true");
					if (!b.WaitFor(Box1, 10000))
					{
						throw new Exception("no se abrió el segundo post del hilo");
					}
					Thread.Sleep(500);
					b.Eval("(function(){var e=" + Box1 + ";e.focus();e.click();return true;})()");
					Thread.Sleep(400);
					b.Type(reply);
				}
				Thread.Sleep(800);
				if (dryRun)
				{
					// Prueba: captura de la ventana de escribir y se cierra sin publicar (no queda borrador).
					status("Prueba: haciendo la captura sin publicar…");
					Thread.Sleep(1000);
					object box = b.Eval("(function(){var a=document.querySelector('[role=\"dialog\"] [data-testid=\"tweetTextarea_0\"]');var d=a?a.closest('[role=\"dialog\"]'):null;if(!d)return null;var r=null;var all=d.querySelectorAll('div');for(var i=0;i<all.length;i++){var x=all[i].getBoundingClientRect();if(x.width>=400&&x.width<900&&x.height>100&&all[i].contains(a)){r=x;break;}}if(!r)r=d.getBoundingClientRect();return [r.left+window.scrollX,r.top+window.scrollY,r.width,r.height];})()");
					List<object> bv = J.L(box);
					if (bv.Count < 4)
					{
						throw new Exception("no se encontró la ventana de escribir para la captura");
					}
					res.Png = b.Shot(Convert.ToDouble(bv[0]), Convert.ToDouble(bv[1]), Convert.ToDouble(bv[2]), Convert.ToDouble(bv[3]));
					res.Url = "(prueba: no se ha publicado)";
					return res;
				}
				string btn = "document.querySelector('[data-testid=\"tweetButton\"]')";
				if (!b.WaitFor(btn + " && " + btn + ".getAttribute('aria-disabled') !== 'true' && !" + btn + ".disabled", 10000))
				{
					throw new Exception("X no deja publicar (¿texto demasiado largo?)");
				}
				status("Publicando…");
				b.Eval(btn + ".click();true");
				// Tras publicar, X enseña un aviso con «Ver» que lleva al post.
				string toast = "document.querySelector('[data-testid=\"toast\"] a[href*=\"/status/\"]')";
				if (b.WaitFor(toast, 25000))
				{
					res.Url = (b.Eval("(" + toast + ").href") as string) ?? "";
				}
				if (res.Url.Length == 0)
				{
					if (b.EvalBool("!!" + Box0))
					{
						throw new Exception("X no ha publicado el post (revisa la cuenta en la ventana de X)");
					}
					// Sin aviso: se busca en el perfil el post con ese texto.
					if (handle.Length > 1)
					{
						Thread.Sleep(3000);
						b.Go("https://x.com" + handle);
						string probe = FirstLine(main);
						string find = "(function(){var p=" + XpBrowser.Q(probe) + ";var arts=document.querySelectorAll('article[data-testid=\"tweet\"]');for(var i=0;i<arts.length;i++){var t=arts[i].querySelector('[data-testid=\"tweetText\"]');if(t&&t.innerText.indexOf(p)>=0){var a=arts[i].querySelector('a[href*=\"/status/\"] time');if(a&&a.parentElement)return a.parentElement.href;}}return '';})()";
						DateTime end = DateTime.UtcNow.AddSeconds(20);
						while (res.Url.Length == 0 && DateTime.UtcNow < end)
						{
							res.Url = (b.Eval(find) as string) ?? "";
							if (res.Url.Length == 0)
							{
								Thread.Sleep(1000);
							}
						}
					}
				}
				if (res.Url.Length == 0)
				{
					throw new Exception("se publicó, pero no se encontró el post para la captura");
				}
				// Enlace limpio (sin /photo/… ni parámetros).
				int q = res.Url.IndexOf('?');
				if (q > 0)
				{
					res.Url = res.Url.Substring(0, q);
				}
				status("Haciendo la captura…");
				Thread.Sleep(1500);
				b.Go(res.Url);
				if (!b.WaitFor("document.querySelector('article[data-testid=\"tweet\"] [data-testid=\"tweetText\"]')", 25000))
				{
					throw new Exception("no se pudo abrir el post para la captura");
				}
				Thread.Sleep(2000);
				object rect = b.Eval("(function(){var L=document.getElementById('layers');if(L)L.style.display='none';var a=document.querySelector('article[data-testid=\"tweet\"]');if(!a)return null;a.scrollIntoView({block:'center'});var r=a.getBoundingClientRect();return [r.left+window.scrollX,r.top+window.scrollY,r.width,r.height];})()");
				List<object> v = J.L(rect);
				if (v.Count < 4)
				{
					throw new Exception("no se encontró el post en la página");
				}
				Thread.Sleep(600);
				res.Png = b.Shot(Convert.ToDouble(v[0]), Convert.ToDouble(v[1]), Convert.ToDouble(v[2]), Convert.ToDouble(v[3]));
			}
			return res;
		}

		private static string FirstLine(string s)
		{
			string t = (s ?? "").Replace("\r", "").Split('\n')[0].Trim();
			// Sin emojis al principio (X los cambia por imágenes y no salen en innerText igual).
			int i = 0;
			while (i < t.Length && !char.IsLetterOrDigit(t[i]))
			{
				i++;
			}
			t = t.Substring(i);
			return (t.Length > 24) ? t.Substring(0, 24) : t;
		}
	}

	// ======================= Historias (1080×1920) =======================
	internal static class XpStory
	{
		public const int W = 1080;

		public const int H = 1920;

		// topText: texto encima de la captura (TikTok); null para Instagram (deja sitio abajo al widget).
		public static Bitmap Make(Image shot, string topText)
		{
			Bitmap bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				g.InterpolationMode = InterpolationMode.HighQualityBicubic;
				g.PixelOffsetMode = PixelOffsetMode.HighQuality;
				g.TextRenderingHint = TextRenderingHint.AntiAlias;
				g.Clear(Color.FromArgb(9, 9, 12));
				Glow(g, new RectangleF(-420f, -380f, 1250f, 1250f), Color.FromArgb(70, Theme.Yellow));
				Glow(g, new RectangleF(300f, 1050f, 1300f, 1300f), Color.FromArgb(85, Theme.Violet));
				bool top = !string.IsNullOrEmpty(topText);
				float maxW = 940f;
				float maxH = top ? 1240f : 1300f;
				float scale = Math.Min(maxW / shot.Width, maxH / shot.Height);
				float sw = shot.Width * scale;
				float sh = shot.Height * scale;
				float textH = 0f;
				Font font = null;
				if (top)
				{
					font = FitFont(g, topText, 960f, 92f);
					textH = font.GetHeight(g) + 56f;
				}
				float blockH = textH + sh;
				// Instagram: un poco por encima del centro, para poner el widget debajo.
				float y = top ? ((H - blockH) / 2f) : Math.Max(140f, (H - sh) / 2f - 170f);
				if (top)
				{
					DrawTop(g, topText, font, y);
					y += textH;
				}
				RectangleF r = new RectangleF((W - sw) / 2f, y, sw, sh);
				using (GraphicsPath shadow = Theme.Round(new RectangleF(r.X, r.Y + 14f, r.Width, r.Height), 34f))
				using (SolidBrush sb = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
				{
					g.FillPath(sb, shadow);
				}
				using (GraphicsPath p = Theme.Round(r, 34f))
				{
					g.SetClip(p);
					g.DrawImage(shot, r);
					g.ResetClip();
					using (Pen pen = new Pen(Color.FromArgb(90, Theme.Violet), 3f))
					{
						g.DrawPath(pen, p);
					}
				}
				if (font != null)
				{
					font.Dispose();
				}
			}
			return bmp;
		}

		private static void Glow(Graphics g, RectangleF r, Color c)
		{
			using (GraphicsPath p = new GraphicsPath())
			{
				p.AddEllipse(r);
				using (PathGradientBrush b = new PathGradientBrush(p))
				{
					b.CenterColor = c;
					b.SurroundColors = new Color[1] { Color.FromArgb(0, c) };
					g.FillPath(b, p);
				}
			}
		}

		private static FontFamily Family()
		{
			try
			{
				return Theme.Display(12f).FontFamily;
			}
			catch
			{
				return new FontFamily("Segoe UI");
			}
		}

		private static Font FitFont(Graphics g, string s, float maxW, float size)
		{
			FontFamily fam = Family();
			FontStyle st = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
			while (true)
			{
				Font f = new Font(fam, size, st, GraphicsUnit.Pixel);
				if (size <= 40f || g.MeasureString(s, f, PointF.Empty, StringFormat.GenericTypographic).Width <= maxW)
				{
					return f;
				}
				f.Dispose();
				size -= 4f;
			}
		}

		// «Twitch: canal»: lo de antes de los dos puntos en blanco y el resto en amarillo.
		private static void DrawTop(Graphics g, string s, Font f, float y)
		{
			string a = s;
			string b = "";
			int i = s.IndexOf(':');
			if (i >= 0 && i < s.Length - 1)
			{
				a = s.Substring(0, i + 1) + " ";
				b = s.Substring(i + 1).Trim();
			}
			StringFormat fmt = StringFormat.GenericTypographic;
			float wa = g.MeasureString(a, f, PointF.Empty, fmt).Width;
			if (a.EndsWith(" "))
			{
				wa += f.Size * 0.28f;
			}
			float wb = (b.Length > 0) ? g.MeasureString(b, f, PointF.Empty, fmt).Width : 0f;
			float x = (W - wa - wb) / 2f;
			using (SolidBrush white = new SolidBrush(Color.White))
			using (SolidBrush yellow = new SolidBrush(Theme.Yellow))
			{
				g.DrawString(a, f, white, x, y, fmt);
				if (b.Length > 0)
				{
					g.DrawString(b, f, yellow, x + wa, y, fmt);
				}
			}
		}
	}

	// ======================= Telegram (para recibirlo en el móvil) =======================
	internal static class XpTelegram
	{
		private static string Api(string token, string method)
		{
			return "https://api.telegram.org/bot" + token.Trim() + "/" + method;
		}

		// Último chat que ha escrito al bot (para no tener que buscar el número a mano).
		public static string DetectChat(string token, out string who)
		{
			who = "";
			Dictionary<string, object> d = Http.Send("GET", Api(token, "getUpdates"), null, null, null, null);
			List<Dictionary<string, object>> list = Http.Arr(d, "result");
			for (int i = list.Count - 1; i >= 0; i--)
			{
				string id = J.S(list[i], "message", "chat", "id");
				if (id.Length > 0)
				{
					who = J.S(list[i], "message", "chat", "first_name");
					if (who.Length == 0)
					{
						who = J.S(list[i], "message", "chat", "username");
					}
					return id;
				}
			}
			return "";
		}

		public static void Message(string token, string chat, string html)
		{
			Http.Send("POST", Api(token, "sendMessage"), null, null, "application/x-www-form-urlencoded", Http.Form("chat_id", chat, "text", html, "parse_mode", "HTML", "disable_web_page_preview", "true"));
		}

		public static string Esc(string s)
		{
			return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
		}

		// Foto con texto (multipart/form-data).
		public static void Photo(string token, string chat, string path, string caption)
		{
			string boundary = "----VortexsLive" + DateTime.Now.Ticks.ToString("x");
			HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Api(token, "sendPhoto"));
			req.Method = "POST";
			req.UserAgent = "VortexsLive";
			req.Timeout = 90000;
			req.ReadWriteTimeout = 90000;
			req.ContentType = "multipart/form-data; boundary=" + boundary;
			using (MemoryStream body = new MemoryStream())
			{
				Action<string> w = delegate(string s)
				{
					byte[] x = Encoding.UTF8.GetBytes(s);
					body.Write(x, 0, x.Length);
				};
				w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"chat_id\"\r\n\r\n" + chat + "\r\n");
				w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"caption\"\r\n\r\n" + (caption ?? "") + "\r\n");
				w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"parse_mode\"\r\n\r\nHTML\r\n");
				w("--" + boundary + "\r\nContent-Disposition: form-data; name=\"photo\"; filename=\"" + Path.GetFileName(path) + "\"\r\nContent-Type: image/png\r\n\r\n");
				byte[] file = File.ReadAllBytes(path);
				body.Write(file, 0, file.Length);
				w("\r\n--" + boundary + "--\r\n");
				req.ContentLength = body.Length;
				using (Stream s = req.GetRequestStream())
				{
					body.Position = 0;
					body.CopyTo(s);
				}
			}
			try
			{
				using (WebResponse r = req.GetResponse())
				{
				}
			}
			catch (WebException ex)
			{
				string text = "";
				try
				{
					if (ex.Response != null)
					{
						using (StreamReader sr = new StreamReader(ex.Response.GetResponseStream()))
						{
							text = sr.ReadToEnd();
						}
					}
				}
				catch
				{
				}
				throw new Exception("Telegram: " + ((text.Length > 0 && text.Length < 300) ? text : ex.Message));
			}
		}
	}

	// ======================= Ajustes → Anuncio =======================
	internal partial class MainForm
	{
		// Si Twitch vuelve antes de estos minutos, es el mismo directo (corte): no se anuncia otra vez.
		private const int XpGapMinutes = 20;

		private Toggle tgXp;

		private Field fldXpMain;

		private Field fldXpReply;

		private Field fldXpChannel;

		private Field fldXpTikTok;

		private Field fldXpTg;

		private PaintBox xpStatusBox;

		private bool xpOn;

		private string xpMain = "🔴 ¡EN DIRECTO! {titulo}";

		private string xpReply = "👉 {enlace}";

		private string xpChannel = "";

		private string xpTikTok = "";

		private string xpTgToken = "";

		private string xpTgChat = "";

		private volatile string xpStatus = "";

		private volatile bool xpBusy;

		private bool xpWasLive;

		private DateTime xpLastLive = DateTime.MinValue;

		private DateTime xpDueAt = DateTime.MinValue;

		private string xpLastShown = "";

		private Process xpLoginProc;

		private static string XpFolder
		{
			get
			{
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Vortexs Live", "Anuncios");
			}
		}

		// Canal de Twitch para el enlace: el escrito aquí, el del chat o la cuenta conectada.
		private string XpChannelName()
		{
			string c = (fldXpChannel != null) ? fldXpChannel.Box.Text.Trim() : xpChannel;
			if (c.Length == 0 && fldChatTw != null)
			{
				c = fldChatTw.Box.Text.Trim();
			}
			if (c.Length == 0)
			{
				c = twitch.Account;
			}
			c = c.Replace("https://", "").Replace("http://", "").Replace("www.", "").Replace("twitch.tv/", "").Trim('/', '@', ' ');
			return c;
		}

		private string XpTikTokText()
		{
			string t = (fldXpTikTok != null) ? fldXpTikTok.Box.Text.Trim() : xpTikTok;
			if (t.Length > 0)
			{
				return t;
			}
			string name = (twitch.Account.Length > 0) ? twitch.Account : XpChannelName();
			return "Twitch: " + name;
		}

		private Card BuildAnnounce(int y0)
		{
			Card card = new Card
			{
				Title = "Anunciar el directo"
			};
			Place(this, card, 20f, y0, 420f, 486f, PageSettings);
			tgXp = new Toggle
			{
				Text = "Al empezar en Twitch: publicar en X y preparar historias"
			};
			Place(card, tgXp, 18f, 40f, 384f, 28f);
			tgXp.CheckedChanged += delegate
			{
				xpOn = tgXp.Checked;
				if (!loading)
				{
					SaveConfig();
				}
			};
			Label(card, "POST · {titulo} = PRINCIPIO DEL TÍTULO", 20f, 76f, 380f);
			fldXpMain = new Field
			{
				ColorEmoji = true
			};
			Place(card, fldXpMain, 20f, 94f, 380f, 34f);
			Label(card, "RESPUESTA EN EL HILO (EL +) · {enlace}", 20f, 134f, 380f);
			fldXpReply = new Field
			{
				ColorEmoji = true
			};
			Place(card, fldXpReply, 20f, 152f, 380f, 34f);
			Label(card, "CANAL DE TWITCH", 20f, 192f, 148f);
			Label(card, "TEXTO HISTORIA TIKTOK", 176f, 192f, 224f);
			fldXpChannel = new Field();
			Place(card, fldXpChannel, 20f, 210f, 148f, 34f);
			fldXpTikTok = new Field();
			Place(card, fldXpTikTok, 176f, 210f, 224f, 34f);
			foreach (Field f in new Field[4] { fldXpMain, fldXpReply, fldXpChannel, fldXpTikTok })
			{
				f.Box.Leave += delegate
				{
					xpMain = fldXpMain.Box.Text;
					xpReply = fldXpReply.Box.Text;
					xpChannel = fldXpChannel.Box.Text.Trim();
					xpTikTok = fldXpTikTok.Box.Text.Trim();
					if (!loading)
					{
						SaveConfig();
					}
				};
			}
			VButton login = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Iniciar sesión en X",
				FontSize = 8.5f
			};
			Place(card, login, 20f, 254f, 120f, 34f);
			login.Click += delegate
			{
				XpOpenLogin();
			};
			VButton dry = new VButton
			{
				Kind = ButtonKind.Primary,
				Text = "Prueba sin publicar",
				FontSize = 8.5f
			};
			Place(card, dry, 148f, 254f, 126f, 34f);
			dry.Click += delegate
			{
				// Lo escribe en X pero no lo publica: sirve sin estar en directo.
				if (XpCheckPlan())
				{
					XpStart(true);
				}
			};
			VButton test = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Publicar ya",
				FontSize = 8.5f
			};
			Place(card, test, 282f, 254f, 118f, 34f);
			test.Click += delegate
			{
				if (xpBusy || !XpCheckPlan())
				{
					return;
				}
				if (I18n.Show(this, "Esto publica de verdad en X ahora mismo con el título actual, hace la captura y prepara las historias.\n\n¿Seguir?", "Publicar anuncio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
				{
					XpStart(false);
				}
			};
			Label(card, "TELEGRAM · PARA RECIBIR LAS HISTORIAS EN EL MÓVIL", 20f, 300f, 380f);
			fldXpTg = new Field();
			Place(card, fldXpTg, 20f, 318f, 262f, 34f);
			fldXpTg.Box.UseSystemPasswordChar = true;
			fldXpTg.Box.Leave += delegate
			{
				string t = fldXpTg.Box.Text.Trim();
				if (t != xpTgToken)
				{
					xpTgToken = t;
					xpTgChat = "";
				}
				if (!loading)
				{
					SaveConfig();
				}
			};
			VButton detect = new VButton
			{
				Kind = ButtonKind.Ghost,
				Text = "Detectar chat",
				FontSize = 9f
			};
			Place(card, detect, 290f, 318f, 110f, 34f);
			detect.Click += delegate
			{
				XpDetectChat();
			};
			Box(card, 20f, 358f, 380f, 40f, delegate(Graphics g, Rectangle r)
			{
				XpWrap(g, "Gratis: crea un bot con @BotFather en Telegram, pega aquí su token, escríbele «hola» al bot y pulsa Detectar chat.", Theme.Muted, r);
			});
			xpStatusBox = Box(card, 20f, 404f, 380f, 44f, delegate(Graphics g, Rectangle r)
			{
				string s = xpStatus;
				if (s.Length == 0 && !AnnounceAllowed())
				{
					s = "Anunciar el directo es del plan Completo (" + License.PriceFull + "). Puedes dejarlo preparado y se activa al suscribirte.";
				}
				if (s.Length == 0)
				{
					s = (xpTgChat.Length > 0) ? "Telegram conectado. Listo para el próximo directo." : "Sin Telegram: las historias se guardan solo en la carpeta.";
				}
				XpWrap(g, s, s.StartsWith("Error") ? Theme.Red : Theme.VioletLight, r);
			});
			VButton folder = new VButton
			{
				Kind = ButtonKind.Link,
				Text = "Abrir carpeta de anuncios →",
				FontSize = 8.5f
			};
			Place(card, folder, 14f, 452f, 220f, 24f);
			folder.Click += delegate
			{
				try
				{
					Directory.CreateDirectory(XpFolder);
					Process.Start("explorer.exe", "\"" + XpFolder + "\"");
				}
				catch
				{
				}
			};
			return card;
		}

		// Texto de varias líneas (DrawText es de una sola).
		private void XpWrap(Graphics g, string s, Color c, Rectangle r)
		{
			using (StringFormat fmt = new StringFormat())
			using (SolidBrush b = new SolidBrush(c))
			{
				fmt.Alignment = StringAlignment.Near;
				fmt.LineAlignment = StringAlignment.Center;
				fmt.Trimming = StringTrimming.EllipsisWord;
				g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
				g.DrawString(I18n.Tr(s), Theme.Body(8f), b, r, fmt);
			}
		}

		// Avisa si no tiene el plan Completo y abre la ventana de planes.
		private bool XpCheckPlan()
		{
			if (AnnounceAllowed())
			{
				return true;
			}
			I18n.Show(this, "Anunciar el directo en X y preparar las historias es del plan Completo (" + License.PriceFull + ").\n\nPuedes dejar la sesión de X y Telegram preparados: en cuanto tengas el plan Completo, funciona solo.", "Plan Completo", MessageBoxButtons.OK, MessageBoxIcon.Information);
			LicenseDialog();
			return AnnounceAllowed();
		}

		private void XpSetStatus(string s)
		{
			xpStatus = s;
		}

		private void XpOpenLogin()
		{
			try
			{
				if (xpLoginProc != null && !xpLoginProc.HasExited)
				{
					xpStatus = "La ventana de X ya está abierta: inicia sesión y ciérrala.";
					return;
				}
				xpLoginProc = XpBrowser.OpenLogin();
				xpStatus = "Inicia sesión en X en la ventana de Chrome que se ha abierto y después ciérrala. Solo hace falta una vez.";
			}
			catch (Exception ex)
			{
				xpStatus = "Error: " + ex.Message;
			}
		}

		private void XpDetectChat()
		{
			string token = fldXpTg.Box.Text.Trim();
			if (token.Length == 0)
			{
				xpStatus = "Error: primero pega el token del bot de Telegram.";
				return;
			}
			xpTgToken = token;
			xpStatus = "Buscando tu chat…";
			new Thread((ThreadStart)delegate
			{
				try
				{
					string who;
					string id = XpTelegram.DetectChat(token, out who);
					if (id.Length == 0)
					{
						xpStatus = "Error: escríbele «hola» a tu bot en Telegram y vuelve a pulsar Detectar chat.";
						return;
					}
					XpTelegram.Message(token, id, "✅ Vortexs Live conectado. Aquí te llegarán las historias cuando empieces directo.");
					BeginInvoke((Action)delegate
					{
						xpTgChat = id;
						xpStatus = "Telegram conectado" + ((who.Length > 0) ? (" (" + who + ")") : "") + ". Te ha llegado un mensaje de prueba.";
						SaveConfig();
					});
				}
				catch (Exception ex)
				{
					xpStatus = "Error: " + ex.Message;
				}
			})
			{
				IsBackground = true,
				Name = "anuncio-telegram"
			}.Start();
		}

		// Cada medio segundo (desde el temporizador): mira si Twitch acaba de empezar a emitir.
		private void XpTick()
		{
			bool live = false;
			try
			{
				foreach (Sink s in engine.Sinks)
				{
					if (s.D.IsTwitch && s.Live)
					{
						live = true;
					}
				}
			}
			catch
			{
			}
			DateTime now = DateTime.UtcNow;
			if (live)
			{
				if (!xpWasLive)
				{
					xpWasLive = true;
					bool fresh = xpLastLive == DateTime.MinValue || (now - xpLastLive).TotalMinutes >= XpGapMinutes;
					if (fresh && xpOn && !xpBusy && AnnounceAllowed())
					{
						// Un poco después, para que Twitch ya enseñe el directo al abrir el enlace.
						xpDueAt = now.AddSeconds(20);
						xpStatus = "Directo empezado: se anuncia en unos segundos…";
					}
				}
				xpLastLive = now;
				if (xpDueAt != DateTime.MinValue && now >= xpDueAt)
				{
					xpDueAt = DateTime.MinValue;
					XpStart(false);
				}
			}
			else if (xpWasLive)
			{
				xpWasLive = false;
				xpDueAt = DateTime.MinValue;
				SaveConfig();
			}
			if (xpStatusBox != null && xpStatus != xpLastShown)
			{
				xpLastShown = xpStatus;
				xpStatusBox.Invalidate();
			}
		}

		private void XpStart(bool dryRun)
		{
			if (xpBusy)
			{
				return;
			}
			xpBusy = true;
			// Lo que se lee de la ventana, aquí (en el hilo de la ventana).
			string uiTitle = (fldTitle != null) ? fldTitle.Box.Text.Trim() : "";
			string uiCat = (fldCat != null) ? fldCat.Box.Text.Trim() : "";
			string tplMain = (fldXpMain != null) ? fldXpMain.Box.Text : xpMain;
			string tplReply = (fldXpReply != null) ? fldXpReply.Box.Text : xpReply;
			string channel = XpChannelName();
			string tiktok = XpTikTokText();
			string token = xpTgToken;
			string chat = xpTgChat;
			new Thread((ThreadStart)delegate
			{
				XpRun(uiTitle, uiCat, tplMain, tplReply, channel, tiktok, token, chat, dryRun);
			})
			{
				IsBackground = true,
				Name = "anuncio-x"
			}.Start();
		}

		private void XpRun(string title, string cat, string tplMain, string tplReply, string channel, string tiktok, string token, string chat, bool dryRun)
		{
			bool tg = token.Length > 0 && chat.Length > 0;
			try
			{
				if (xpLoginProc != null && !xpLoginProc.HasExited)
				{
					// La ventana de iniciar sesión usa el mismo perfil: hay que cerrarla antes.
					try
					{
						xpLoginProc.CloseMainWindow();
						if (!xpLoginProc.WaitForExit(5000))
						{
							xpLoginProc.Kill();
						}
					}
					catch
					{
					}
				}
				if (channel.Length == 0)
				{
					throw new Exception("escribe tu canal de Twitch en Ajustes → Anuncio");
				}
				xpStatus = "Leyendo el título…";
				if (twitch.Connected)
				{
					try
					{
						string t;
						Category c;
						twitch.ReadCurrent(out t, out c);
						if (!string.IsNullOrEmpty(t))
						{
							title = t;
						}
						if (c != null)
						{
							cat = c.Name;
						}
					}
					catch
					{
					}
				}
				if (title.Length == 0)
				{
					title = "¡Directo!";
				}
				string link = "https://twitch.tv/" + channel.ToLowerInvariant();
				string summary = XpText.Summarize(title, 90);
				string main = XpText.Fill(tplMain, summary, title, link, cat);
				string reply = XpText.Fill(tplReply, summary, title, link, cat);
				if (main.Length == 0)
				{
					main = summary;
				}
				XpPoster.Result r = XpPoster.Post(main, reply, XpSetStatus, dryRun);
				LogLater(dryRun ? "Anuncio: prueba hecha (no se ha publicado nada en X)." : ("Anuncio: publicado en X → " + r.Url));
				xpStatus = "Preparando las historias…";
				string dir = Path.Combine(XpFolder, DateTime.Now.ToString("yyyy-MM-dd HH.mm") + (dryRun ? " (prueba)" : ""));
				Directory.CreateDirectory(dir);
				string pShot = Path.Combine(dir, "post-x.png");
				string pTik = Path.Combine(dir, "historia-tiktok.png");
				string pIg = Path.Combine(dir, "historia-instagram.png");
				File.WriteAllBytes(pShot, r.Png);
				using (MemoryStream ms = new MemoryStream(r.Png))
				using (Image shot = Image.FromStream(ms))
				{
					using (Bitmap a = XpStory.Make(shot, tiktok))
					{
						a.Save(pTik, ImageFormat.Png);
					}
					using (Bitmap b = XpStory.Make(shot, null))
					{
						b.Save(pIg, ImageFormat.Png);
					}
				}
				if (tg)
				{
					xpStatus = "Mandándolo a Telegram…";
					XpTelegram.Message(token, chat, dryRun ? ("🧪 <b>PRUEBA</b> (no se ha publicado nada en X). Así quedaría:\n\n" + XpTelegram.Esc(main) + "\n\n↳ " + XpTelegram.Esc(reply)) : ("🔴 <b>Directo anunciado en X</b>\n" + XpTelegram.Esc(r.Url)));
					XpTelegram.Photo(token, chat, pTik, "📱 <b>Historia de TikTok</b>: guárdala y súbela.");
					XpTelegram.Photo(token, chat, pIg, "📸 <b>Historia de Instagram</b>: súbela y añade el widget de enlace.");
					XpTelegram.Message(token, chat, "🔗 Enlace para el widget de Instagram (tócalo para copiarlo):\n<code>" + XpTelegram.Esc(link) + "</code>");
				}
				xpStatus = (dryRun ? "Prueba hecha a las " : "Hecho a las ") + DateTime.Now.ToString("HH:mm") + (dryRun ? (tg ? ": no se ha publicado nada; mira las historias en Telegram." : ": no se ha publicado nada; mira las historias en la carpeta.") : (tg ? ": post en X e historias enviadas a Telegram." : ": post en X. Las historias están en la carpeta de anuncios."));
				LogLater("Anuncio: historias guardadas en " + dir);
			}
			catch (Exception ex)
			{
				xpStatus = "Error: " + ex.Message;
				LogLater("Anuncio: no se pudo hacer (" + ex.Message + ").");
				if (tg)
				{
					try
					{
						XpTelegram.Message(token, chat, "⚠️ Vortexs Live no pudo anunciar el directo: " + XpTelegram.Esc(ex.Message));
					}
					catch
					{
					}
				}
			}
			finally
			{
				xpBusy = false;
			}
		}

		// ---------- VortexsLive.ini ----------

		private void LoadXpConfig(Dictionary<string, string> raw)
		{
			string v;
			xpOn = raw.TryGetValue("xp_on", out v) && v == "1";
			if (raw.TryGetValue("xp_main", out v))
			{
				xpMain = v;
			}
			if (raw.TryGetValue("xp_reply", out v))
			{
				xpReply = v;
			}
			if (raw.TryGetValue("xp_channel", out v))
			{
				xpChannel = v;
			}
			if (raw.TryGetValue("xp_tiktok", out v))
			{
				xpTikTok = v;
			}
			if (raw.TryGetValue("xp_tg", out v))
			{
				xpTgToken = Secret.Unprotect(v);
			}
			if (raw.TryGetValue("xp_tg_chat", out v))
			{
				xpTgChat = v.Trim();
			}
			long ticks;
			if (raw.TryGetValue("xp_last", out v) && long.TryParse(v, out ticks) && ticks > 0)
			{
				xpLastLive = new DateTime(ticks, DateTimeKind.Utc);
			}
			if (tgXp != null)
			{
				tgXp.Checked = xpOn;
				fldXpMain.Box.Text = xpMain;
				fldXpReply.Box.Text = xpReply;
				fldXpChannel.Box.Text = xpChannel;
				fldXpTikTok.Box.Text = xpTikTok;
				fldXpTg.Box.Text = xpTgToken;
			}
		}

		private void SaveXpConfig(List<string> lines)
		{
			if (fldXpMain != null)
			{
				xpMain = fldXpMain.Box.Text;
				xpReply = fldXpReply.Box.Text;
				xpChannel = fldXpChannel.Box.Text.Trim();
				xpTikTok = fldXpTikTok.Box.Text.Trim();
			}
			lines.Add("xp_on=" + (xpOn ? "1" : "0"));
			lines.Add("xp_main=" + xpMain.Replace("\r", "").Replace("\n", "\\n"));
			lines.Add("xp_reply=" + xpReply.Replace("\r", "").Replace("\n", "\\n"));
			lines.Add("xp_channel=" + xpChannel);
			lines.Add("xp_tiktok=" + xpTikTok);
			lines.Add("xp_tg=" + Secret.Protect(xpTgToken));
			lines.Add("xp_tg_chat=" + xpTgChat);
			lines.Add("xp_last=" + ((xpLastLive == DateTime.MinValue) ? "0" : xpLastLive.Ticks.ToString()));
		}
	}
}
