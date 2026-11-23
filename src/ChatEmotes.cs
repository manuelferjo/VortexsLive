// Botón 😊 de la página del chat: la lista de emotes que se pueden poner al escribir.
// - Twitch: los de tu canal, los de otros canales que puedes usar (tus suscripciones) y los globales
//   (hace falta Twitch conectado en Directo → Título y categoría). Al enviarlos, Twitch los enseña como emotes.
// - 7TV, BetterTTV y FrankerFaceZ: los del canal y los globales. Se ven en el chat de Vortexs Live y
//   para quien tenga esas extensiones.
// Se guarda la lista 3 minutos para no preguntar a Twitch cada vez que se abre el panel.
using System;
using System.Collections.Generic;

namespace StreamDelay
{
	internal partial class MainForm
	{
		private readonly object emotesLock = new object();

		private Dictionary<string, object> emotesCache;

		private DateTime emotesAt = DateTime.MinValue;

		private Dictionary<string, object> ChatEmotes()
		{
			lock (emotesLock)
			{
				if (emotesCache != null && DateTime.UtcNow - emotesAt < TimeSpan.FromMinutes(3.0))
				{
					return emotesCache;
				}
				List<object> groups = new List<object>();
				string hint = "";
				string twCh = "";
				Invoke((Action)delegate
				{
					twCh = ChatHub.Clean(fldChatTw.Box.Text);
				});
				if (twitch.Connected)
				{
					try
					{
						List<Dictionary<string, object>> channel = new List<Dictionary<string, object>>();
						List<Dictionary<string, object>> others = new List<Dictionary<string, object>>();
						List<Dictionary<string, object>> globals = new List<Dictionary<string, object>>();
						bool all;
						twitch.ChatEmotes(twCh, channel, others, globals, out all);
						AddEmoteGroup(groups, "Tu canal de Twitch", channel);
						AddEmoteGroup(groups, "Tus suscripciones", others);
						AddEmoteGroup(groups, "Twitch", globals);
						if (!all)
						{
							hint = "Para ver también los emotes de tus suscripciones, vuelve a conectar Twitch en Directo → Título y categoría.";
						}
					}
					catch (Exception ex)
					{
						hint = "No se pudieron cargar los emotes de Twitch: " + ex.Message;
					}
				}
				else
				{
					hint = "Conecta Twitch en Directo → Título y categoría para ver los emotes de tu canal.";
				}
				ChatHub hub = chatHub;
				if (hub != null)
				{
					AddEmoteGroup(groups, "7TV · BTTV · FFZ del canal", ToItems(hub.Emotes.List(true)));
					AddEmoteGroup(groups, "7TV · BTTV · FFZ", ToItems(hub.Emotes.List(false)));
				}
				Dictionary<string, object> result = new Dictionary<string, object>
				{
					{ "groups", groups },
					{ "hint", hint }
				};
				// Si algo falló, se vuelve a intentar enseguida la próxima vez.
				emotesCache = (hint.StartsWith("No se pudieron") ? null : result);
				emotesAt = DateTime.UtcNow;
				return result;
			}
		}

		private static List<Dictionary<string, object>> ToItems(List<KeyValuePair<string, string>> list)
		{
			List<Dictionary<string, object>> items = new List<Dictionary<string, object>>();
			foreach (KeyValuePair<string, string> kv in list)
			{
				items.Add(new Dictionary<string, object>
				{
					{ "n", kv.Key },
					{ "u", kv.Value }
				});
			}
			return items;
		}

		private static void AddEmoteGroup(List<object> groups, string name, List<Dictionary<string, object>> items)
		{
			if (items.Count == 0)
			{
				return;
			}
			groups.Add(new Dictionary<string, object>
			{
				{ "name", I18n.Tr(name) },
				{ "items", items }
			});
		}
	}
}
