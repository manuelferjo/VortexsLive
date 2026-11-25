using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace StreamDelay
{
    // Chat de Kick: el websocket público de su chat (Pusher). Mensajes, subs, regalos, hosts y follows.
    class KickChat
    {
        // Clave de respaldo; la buena se pide a Kick en cada conexión (la cambian de vez en cuando).
        const string FallbackKey = "34bf7a0ff419a2a775b9", FallbackCluster = "us2";
        readonly ChatHub hub;
        readonly string slug;
        volatile bool run;
        ClientWebSocket ws;
        Thread th;
        readonly object sendLock = new object();
        string chatroomId = "", channelId = "", userId = "";
        static readonly Regex EmoteRx = new Regex(@"\[emote:(\d+):([^\]]*)\]");

        public KickChat(ChatHub h, string s) { hub = h; slug = s; }

        public void Start() { run = true; th = new Thread(Loop) { IsBackground = true, Name = "chat-kick" }; th.Start(); }
        public void Stop() { run = false; try { if (ws != null) ws.Abort(); } catch { } }

        object Channel() { return Web.GetJson("https://kick.com/api/v2/channels/" + Uri.EscapeDataString(slug)); }

        void Loop()
        {
            int wait = 2000;
            while (run && hub.Alive)
            {
                try
                {
                    if (chatroomId.Length == 0)
                    {
                        object ch;
                        try { ch = Channel(); }
                        catch (WebException ex)
                        {
                            var r = ex.Response as HttpWebResponse;
                            if (r != null && r.StatusCode == HttpStatusCode.NotFound) throw new Exception("el canal \"" + slug + "\" no existe");
                            throw;
                        }
                        chatroomId = J.S(ch, "chatroom", "id");
                        channelId = J.S(ch, "id");
                        userId = J.S(ch, "user_id");
                        if (chatroomId.Length == 0) throw new Exception("Kick no devolvió el chat del canal");
                        string uid = userId;
                        ChatHub.Background(() => hub.Emotes.LoadKick(uid, hub.Log));
                    }
                    ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    ws.ConnectAsync(new Uri(PusherUrl()), CancellationToken.None).Wait(15000);
                    if (ws.State != WebSocketState.Open) throw new Exception("no se pudo conectar con el chat de Kick");
                    var pinger = new Thread(() =>
                    {
                        var w = ws;
                        while (run && w.State == WebSocketState.Open)
                        {
                            for (int i = 0; i < 600 && run && w.State == WebSocketState.Open; i++) Thread.Sleep(100);
                            Send(w, "{\"event\":\"pusher:ping\",\"data\":{}}");
                        }
                    }) { IsBackground = true };
                    pinger.Start();
                    var buf = new byte[64 * 1024];
                    var msg = new MemoryStream();
                    while (run && ws.State == WebSocketState.Open)
                    {
                        var r = ws.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None).Result;
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        msg.Write(buf, 0, r.Count);
                        if (!r.EndOfMessage) continue;
                        string text = Encoding.UTF8.GetString(msg.ToArray());
                        msg.SetLength(0);
                        wait = 2000;
                        Handle(text);
                    }
                }
                catch (Exception ex)
                {
                    var e = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                    if (run) hub.SetState("kick", "error: " + e.Message);
                }
                try { if (ws != null) ws.Abort(); } catch { }
                if (!run) break;
                Thread.Sleep(wait);
                wait = Math.Min(wait * 2, 30000);
            }
        }

        // Pide a Kick con qué servidor de tiempo real conectarse (solo se acepta Pusher, que es el que se usa aquí).
        string PusherUrl()
        {
            string key = FallbackKey, cluster = FallbackCluster;
            try
            {
                string body = "{\"client\":{\"id\":\"" + Guid.NewGuid() + "\",\"type\":\"web\"},\"capabilities\":{\"accepted_providers\":[{\"provider\":\"pusher\"}]}}";
                var o = Web.PostJson("https://web.kick.com/api/v1/realtime/channels/" + channelId + "/chat/connection", body, null, null);
                foreach (var c in J.L(o, "data", "connections"))
                {
                    if (J.S(c, "provider") != "pusher") continue;
                    string k = J.S(c, "credentials", "app_key"), cl = J.S(c, "credentials", "cluster");
                    if (k.Length > 0 && cl.Length > 0) { key = k; cluster = cl; break; }
                }
            }
            catch { }
            return "wss://ws-" + cluster + ".pusher.com/app/" + key + "?protocol=7&client=js&version=8.4.0&flash=false";
        }

        void Send(ClientWebSocket w, string s)
        {
            try
            {
                var b = Encoding.UTF8.GetBytes(s);
                lock (sendLock) w.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).Wait(10000);
            }
            catch { }
        }

        void Subscribe(string channel) { Send(ws, "{\"event\":\"pusher:subscribe\",\"data\":{\"auth\":\"\",\"channel\":\"" + channel + "\"}}"); }

        void Handle(string text)
        {
            var o = Web.Parse(text);
            string ev = J.S(o, "event");
            object data = J.At(o, "data");
            if (data is string) data = Web.Parse((string)data);
            switch (ev)
            {
                case "pusher:connection_established":
                    Subscribe("chatrooms." + chatroomId + ".v2");
                    Subscribe("chatrooms." + chatroomId);
                    if (channelId.Length > 0) Subscribe("channel." + channelId);
                    break;
                case "pusher_internal:subscription_succeeded":
                    hub.SetState("kick", "conectado");
                    break;
                case "pusher:ping": Send(ws, "{\"event\":\"pusher:pong\",\"data\":{}}"); break;
                case "pusher:error": hub.SetState("kick", "error: " + J.S(data, "message")); break;
                case @"App\Events\ChatMessageEvent": Message(data); break;
                case @"App\Events\MessageDeletedEvent": hub.Emit(new ChatItem { t = "del", p = "kick", id = J.S(data, "message", "id") }); break;
                case @"App\Events\UserBannedEvent":
                    // Con fecha de fin es una expulsión temporal; sin ella, ban.
                    string until = J.S(data, "expires_at");
                    hub.Emit(new ChatItem { t = "del", p = "kick", uid = J.S(data, "user", "id"), kind = until.Length > 0 ? "timeout" : "ban" });
                    break;
                case @"App\Events\SubscriptionEvent":
                    {
                        string u = J.S(data, "username"); long months = J.N(data, 1, "months");
                        Event("sub", u, months > 1 ? u + " lleva " + months + " meses suscrito" : u + " se ha suscrito", null);
                    }
                    break;
                case @"App\Events\GiftedSubscriptionsEvent":
                    {
                        string u = J.S(data, "gifter_username");
                        if (u.Length == 0) u = "Anónimo";
                        int n = J.L(data, "gifted_usernames").Count;
                        Event(n > 1 ? "giftbomb" : "gift", u, u + " ha regalado " + n + (n == 1 ? " suscripción" : " suscripciones"), n.ToString());
                    }
                    break;
                case @"App\Events\StreamHostEvent":
                    {
                        string u = J.S(data, "host_username"); string v = J.S(data, "number_viewers");
                        Event("raid", u, u + " hace host con " + v + " espectadores", v);
                    }
                    break;
                case @"App\Events\FollowersUpdated":
                    {
                        string u = J.S(data, "username");
                        if (u.Length > 0 && J.S(data, "followed").ToLowerInvariant() == "true") Event("follow", u, u + " te sigue", null);
                    }
                    break;
                default:
                    // Kicks (propinas de Kick): el formato puede cambiar, se lee con tolerancia.
                    if (ev.IndexOf("Kicks", StringComparison.OrdinalIgnoreCase) >= 0 || ev.IndexOf("Gift", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string u = J.S(data, "sender", "username");
                        string amount = J.S(data, "gift", "amount");
                        if (u.Length > 0 && amount.Length > 0)
                            Event("kicks", u, u + " ha enviado " + amount + " Kicks", amount + " Kicks");
                    }
                    break;
            }
        }

        void Event(string kind, string user, string text, string amount)
        {
            hub.Emit(new ChatItem { t = "ev", p = "kick", kind = kind, user = user, text = text, amount = amount });
        }

        void Message(object d)
        {
            string content = J.S(d, "content");
            var parts = new List<ChatPart>();
            var map = hub.Emotes.For("kick");
            int pos = 0;
            foreach (Match mm in EmoteRx.Matches(content))
            {
                Emotes.AddText(parts, content.Substring(pos, mm.Index - pos), map);
                parts.Add(new ChatPart { e = "https://files.kick.com/emotes/" + mm.Groups[1].Value + "/fullsize", n = mm.Groups[2].Value });
                pos = mm.Index + mm.Length;
            }
            Emotes.AddText(parts, content.Substring(pos), map);
            var roles = new List<string>();
            foreach (var b in J.L(d, "sender", "identity", "badges"))
            {
                string t = J.S(b, "type");
                if (t == "broadcaster") roles.Add("broadcaster");
                else if (t == "moderator") roles.Add("mod");
                else if (t == "vip") roles.Add("vip");
                else if (t == "subscriber") roles.Add("sub");
                else if (t == "founder") roles.Add("founder");
                else if (t == "og") roles.Add("og");
                else if (t == "verified") roles.Add("verified");
            }
            hub.Emit(new ChatItem
            {
                t = "msg", p = "kick", id = J.S(d, "id"), uid = J.S(d, "sender", "id"),
                user = J.S(d, "sender", "username"), color = J.S(d, "sender", "identity", "color"),
                roles = roles.ToArray(), parts = parts
            });
        }

        public int FetchViewers()
        {
            var ch = Channel();
            var live = J.At(ch, "livestream");
            if (live == null) return -2;
            return (int)J.N(live, -1, "viewer_count");
        }
    }
}
