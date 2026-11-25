using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace StreamDelay
{
    // Chat de Twitch: IRC anónimo (sin iniciar sesión). Subs, regalos, raids y bits llegan por el propio chat.
    class TwitchChat
    {
        readonly ChatHub hub;
        readonly string channel;
        volatile bool run;
        TcpClient tcp;
        Thread th;
        string roomId = "";

        public TwitchChat(ChatHub h, string ch) { hub = h; channel = ch; }

        // Imágenes de los emblemas (con Twitch conectado en Directo → Título y categoría; si no, se dibujan los
        // de la página). Se piden una vez por canal y se renuevan cada hora.
        static readonly object badgesLock = new object();
        static Dictionary<string, string[]> badges;
        static string badgesChannel = "";
        static DateTime badgesAt = DateTime.MinValue;
        static bool badgesLoading;

        void LoadBadges()
        {
            var api = hub.TwitchApi;
            if (api == null || !api.Connected) return;
            lock (badgesLock)
            {
                if (badgesLoading) return;
                if (badgesChannel == channel && DateTime.UtcNow - badgesAt < TimeSpan.FromHours(1)) return;
                badgesLoading = true;
            }
            ChatHub.Background(() =>
            {
                Dictionary<string, string[]> map = null;
                try { map = api.ChatBadges(channel); } catch (Exception ex) { hub.Log("Emblemas de Twitch: " + ex.Message); }
                lock (badgesLock)
                {
                    if (map != null) { badges = map; badgesChannel = channel; }
                    badgesAt = DateTime.UtcNow;   // si falla, no se reintenta en cada mensaje
                    badgesLoading = false;
                }
            });
        }

        List<string[]> BadgeImages(string tag)
        {
            LoadBadges();
            Dictionary<string, string[]> map;
            lock (badgesLock) { map = badgesChannel == channel ? badges : null; }
            if (map == null || string.IsNullOrEmpty(tag)) return null;
            var list = new List<string[]>();
            foreach (var b in tag.Split(','))
            {
                string[] x;
                if (map.TryGetValue(b, out x)) list.Add(x);
            }
            return list.Count > 0 ? list : null;
        }

        public void Start() { run = true; th = new Thread(Loop) { IsBackground = true, Name = "chat-twitch" }; th.Start(); }
        public void Stop() { run = false; try { if (tcp != null) tcp.Close(); } catch { } }

        void Loop()
        {
            int wait = 2000;
            while (run && hub.Alive)
            {
                try
                {
                    tcp = new TcpClient();
                    tcp.Connect("irc.chat.twitch.tv", 6697);
                    var ssl = new SslStream(tcp.GetStream());
                    ssl.AuthenticateAsClient("irc.chat.twitch.tv", null, SslProtocols.Tls12, false);
                    var rd = new StreamReader(ssl, new UTF8Encoding(false));
                    var wr = new StreamWriter(ssl, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
                    wr.WriteLine("CAP REQ :twitch.tv/tags twitch.tv/commands");
                    wr.WriteLine("PASS SCHMOOPIIE");
                    wr.WriteLine("NICK justinfan" + new Random().Next(10000, 99999));
                    wr.WriteLine("JOIN #" + channel);
                    tcp.ReceiveTimeout = 6 * 60 * 1000; // Twitch manda PING cada ~5 min
                    string line;
                    while (run && (line = rd.ReadLine()) != null)
                    {
                        wait = 2000;
                        Handle(line, wr);
                    }
                }
                catch (Exception ex)
                {
                    if (run) hub.SetState("twitch", "error: " + ex.Message);
                }
                try { if (tcp != null) tcp.Close(); } catch { }
                if (!run) break;
                Thread.Sleep(wait);
                wait = Math.Min(wait * 2, 30000);
            }
        }

        class Irc
        {
            public Dictionary<string, string> Tags = new Dictionary<string, string>();
            public string Prefix = "", Command = "", Channel = "", Trailing;
        }

        static Irc Parse(string line)
        {
            var m = new Irc();
            int i = 0;
            if (line.StartsWith("@"))
            {
                int sp = line.IndexOf(' ');
                foreach (var kv in line.Substring(1, sp - 1).Split(';'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq < 0) m.Tags[kv] = "";
                    else m.Tags[kv.Substring(0, eq)] = Unescape(kv.Substring(eq + 1));
                }
                i = sp + 1;
            }
            if (i < line.Length && line[i] == ':')
            {
                int sp = line.IndexOf(' ', i);
                if (sp < 0) return m;
                m.Prefix = line.Substring(i + 1, sp - i - 1);
                i = sp + 1;
            }
            int t = line.IndexOf(" :", i);
            string head = t >= 0 ? line.Substring(i, t - i) : line.Substring(i);
            if (t >= 0) m.Trailing = line.Substring(t + 2);
            var parts = head.Split(' ');
            m.Command = parts[0];
            if (parts.Length > 1) m.Channel = parts[1];
            return m;
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                sb.Append(c == 's' ? ' ' : c == ':' ? ';' : c == 'r' ? '\r' : c == 'n' ? '\n' : c);
            }
            return sb.ToString();
        }

        string Tag(Irc m, string k) { string v; return m.Tags.TryGetValue(k, out v) ? v : ""; }

        void Handle(string line, StreamWriter wr)
        {
            var m = Parse(line);
            switch (m.Command)
            {
                case "PING": wr.WriteLine("PONG :" + (m.Trailing ?? "tmi.twitch.tv")); break;
                case "RECONNECT": throw new IOException("Twitch pidió reconectar");
                case "ROOMSTATE":
                    string rid = Tag(m, "room-id");
                    if (rid.Length == 0) break;
                    hub.SetState("twitch", "conectado");
                    if (rid != roomId)
                    {
                        roomId = rid;
                        ChatHub.Background(() => hub.Emotes.LoadTwitch(rid, hub.Log));
                    }
                    break;
                case "NOTICE":
                    if ((m.Trailing ?? "").Length > 0 && Tag(m, "msg-id") == "msg_channel_suspended") hub.SetState("twitch", "error: canal suspendido o inexistente");
                    break;
                case "PRIVMSG": Message(m); break;
                case "USERNOTICE": Notice(m); break;
                case "CLEARMSG": hub.Emit(new ChatItem { t = "del", p = "twitch", id = Tag(m, "target-msg-id") }); break;
                case "CLEARCHAT":
                    string target = Tag(m, "target-user-id");
                    // ban-duration = expulsión temporal (segundos); sin él, ban. El chat los deja borrosos.
                    string dur = Tag(m, "ban-duration");
                    if (target.Length > 0) hub.Emit(new ChatItem { t = "del", p = "twitch", uid = target, kind = dur.Length > 0 ? "timeout" : "ban", amount = dur.Length > 0 ? dur : null });
                    break;
            }
        }

        string UserName(Irc m)
        {
            string n = Tag(m, "display-name");
            if (n.Length == 0) { int ex = m.Prefix.IndexOf('!'); n = ex > 0 ? m.Prefix.Substring(0, ex) : m.Prefix; }
            return n;
        }

        static string[] Roles(string badges)
        {
            var r = new List<string>();
            foreach (var b in badges.Split(','))
            {
                string n = b.Split('/')[0];
                if (n == "broadcaster") r.Add("broadcaster");
                else if (n == "moderator") r.Add("mod");
                else if (n == "vip") r.Add("vip");
                else if (n == "subscriber") r.Add("sub");
                else if (n == "founder") r.Add("founder");
                else if (n == "partner" || n == "verified") r.Add("verified");
            }
            return r.ToArray();
        }

        List<ChatPart> Parts(string text, string emotesTag)
        {
            var parts = new List<ChatPart>();
            var map = hub.Emotes.For("twitch");
            if (string.IsNullOrEmpty(emotesTag)) { Emotes.AddText(parts, text, map); return parts; }
            // Posiciones de Twitch en puntos de código Unicode: se convierten a índices de la cadena.
            var cp = new List<int>();
            for (int i = 0; i < text.Length; i++) { cp.Add(i); if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) i++; }
            cp.Add(text.Length);
            var list = new List<KeyValuePair<int, KeyValuePair<int, string>>>(); // inicio -> (fin, id)
            foreach (var em in emotesTag.Split('/'))
            {
                int c = em.IndexOf(':');
                if (c < 0) continue;
                string id = em.Substring(0, c);
                foreach (var rng in em.Substring(c + 1).Split(','))
                {
                    var se = rng.Split('-');
                    int s, e;
                    if (se.Length == 2 && int.TryParse(se[0], out s) && int.TryParse(se[1], out e) && s >= 0 && s <= e && e + 1 < cp.Count)
                        list.Add(new KeyValuePair<int, KeyValuePair<int, string>>(cp[s], new KeyValuePair<int, string>(cp[e + 1], id)));
                }
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            int pos = 0;
            foreach (var r in list)
            {
                int start = r.Key, end = r.Value.Key;
                if (start < pos) continue;
                Emotes.AddText(parts, text.Substring(pos, start - pos), map);
                parts.Add(new ChatPart { e = "https://static-cdn.jtvnw.net/emoticons/v2/" + r.Value.Value + "/default/dark/2.0", n = text.Substring(start, end - start) });
                pos = end;
            }
            Emotes.AddText(parts, text.Substring(pos), map);
            return parts;
        }

        // Chat compartido de Twitch (varios canales con el chat unido): los mensajes de los otros
        // canales traen source-room-id distinto de room-id. Nombre e imagen de cada canal, una vez.
        readonly Dictionary<string, string[]> sources = new Dictionary<string, string[]>();

        string[] SourceChannel(string id)
        {
            string[] s;
            lock (sources) { if (sources.TryGetValue(id, out s)) return s; }
            s = new string[] { "", "" };
            try
            {
                string body = "{\"query\":\"query{user(id:\\\"" + id + "\\\"){login displayName profileImageURL(width:50)}}\"}";
                var h = new Dictionary<string, string> { { "Client-Id", "kimne78kx3ncx6brgo4mv6wki5h1ko" } };
                var o = Web.PostJson("https://gql.twitch.tv/gql", body, h, null);
                string name = J.S(o, "data", "user", "displayName");
                if (name.Length == 0) name = J.S(o, "data", "user", "login");
                s = new string[] { name, J.S(o, "data", "user", "profileImageURL") };
            }
            catch (Exception e) { hub.Log("Chat compartido: no se pudo saber el nombre del canal " + id + " (" + e.Message + ")."); }
            lock (sources) sources[id] = s;
            return s;
        }

        // Marca el mensaje si viene de otro canal del chat compartido. Devuelve true en ese caso.
        bool MarkSource(Irc m, ChatItem it)
        {
            string sid = Tag(m, "source-room-id"), rid = Tag(m, "room-id");
            if (sid.Length == 0 || sid == rid || sid.Length > 20) return false;
            foreach (char c in sid) if (c < '0' || c > '9') return false;
            var s = SourceChannel(sid);
            it.src = s[0].Length > 0 ? s[0] : "otro canal";
            it.srci = s[1].Length > 0 ? s[1] : null;
            return true;
        }

        ChatItem Base(Irc m, string t)
        {
            return new ChatItem
            {
                t = t, p = "twitch", id = Tag(m, "id"), uid = Tag(m, "user-id"),
                user = UserName(m), color = Tag(m, "color"), roles = Roles(Tag(m, "badges")),
                bd = BadgeImages(Tag(m, "badges"))
            };
        }

        void Message(Irc m)
        {
            string text = m.Trailing ?? "";
            if (text.StartsWith("\u0001ACTION ") && text.EndsWith("\u0001")) text = text.Substring(8, text.Length - 9);
            var it = Base(m, "msg");
            it.parts = Parts(text, Tag(m, "emotes"));
            // Los bits enviados en otro canal del chat compartido son para ese canal, no para ti.
            if (MarkSource(m, it)) { hub.Emit(it); return; }
            string bits = Tag(m, "bits");
            if (bits.Length > 0 && bits != "0")
            {
                it.t = "ev"; it.kind = "bits"; it.amount = bits + " bits";
                it.text = it.user + " ha enviado " + bits + " bits";
            }
            hub.Emit(it);
        }

        void Notice(Irc m)
        {
            string id = Tag(m, "msg-id");
            var it = Base(m, "ev");
            string user = it.user;
            if (Tag(m, "login") == "ananonymousgifter") user = it.user = "Anónimo";
            // Avisos de otro canal del chat compartido (subs, raids… de ese canal): se enseñan con su
            // texto, pero no cuentan como tuyos (alertas, objetivo, resumen).
            if (MarkSource(m, it) && id != "announcement")
            {
                string sys = Tag(m, "system-msg");
                if (sys.Length == 0) return;
                it.kind = "other"; it.text = sys;
                if (!string.IsNullOrEmpty(m.Trailing)) it.parts = Parts(m.Trailing, Tag(m, "emotes"));
                hub.Emit(it);
                return;
            }
            string plan = Tag(m, "msg-param-sub-plan");
            string tier = plan == "Prime" ? " con Prime" : plan == "2000" ? " de nivel 2" : plan == "3000" ? " de nivel 3" : "";
            switch (id)
            {
                case "sub":
                    it.kind = "sub"; it.text = user + " se ha suscrito" + tier; break;
                case "resub":
                    it.kind = "resub";
                    it.text = user + " lleva " + Tag(m, "msg-param-cumulative-months") + " meses suscrito" + tier; break;
                case "subgift":
                    // Los regalos de una "lluvia" llegan uno a uno después del aviso general: no se repiten.
                    if (Tag(m, "msg-param-community-gift-id").Length > 0) return;
                    it.kind = "gift"; it.text = user + " ha regalado una suscripción a " + Tag(m, "msg-param-recipient-display-name"); break;
                case "submysterygift":
                    it.kind = "giftbomb";
                    string n = Tag(m, "msg-param-mass-gift-count");
                    it.amount = n;
                    it.text = user + " ha regalado " + n + (n == "1" ? " suscripción" : " suscripciones") + " al chat"; break;
                case "raid":
                    it.kind = "raid";
                    it.user = Tag(m, "msg-param-displayName").Length > 0 ? Tag(m, "msg-param-displayName") : user;
                    it.amount = Tag(m, "msg-param-viewerCount");
                    it.text = it.user + " hace raid con " + it.amount + " espectadores"; break;
                case "giftpaidupgrade":
                case "anongiftpaidupgrade":
                case "primepaidupgrade":
                    it.kind = "sub"; it.text = user + " continúa su suscripción"; break;
                case "announcement":
                    it.t = "msg"; it.kind = "announce"; break;
                default:
                    string sys = Tag(m, "system-msg");
                    if (sys.Length == 0) return;
                    it.kind = "other"; it.text = sys; break;
            }
            if (!string.IsNullOrEmpty(m.Trailing)) it.parts = Parts(m.Trailing, Tag(m, "emotes"));
            hub.Emit(it);
        }

        // Espectadores con la API pública de la web de Twitch (sin iniciar sesión).
        public int FetchViewers()
        {
            string body = "{\"query\":\"query{user(login:\\\"" + channel + "\\\"){id stream{viewersCount}}}\"}";
            var h = new Dictionary<string, string> { { "Client-Id", "kimne78kx3ncx6brgo4mv6wki5h1ko" } };
            var o = Web.PostJson("https://gql.twitch.tv/gql", body, h, null);
            string uid = J.S(o, "data", "user", "id");
            if (uid.Length == 0) { hub.SetState("twitch", "error: el canal \"" + channel + "\" no existe"); return -1; }
            if (roomId.Length == 0) { roomId = uid; ChatHub.Background(() => hub.Emotes.LoadTwitch(uid, hub.Log)); }
            if (J.At(o, "data", "user", "stream") == null) return -2;
            return (int)J.N(o, -1, "data", "user", "stream", "viewersCount");
        }
    }
}
