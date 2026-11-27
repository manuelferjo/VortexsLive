// Chat conjunto de Twitch, Kick, YouTube y TikTok SIN iniciar sesión: basta con el nombre de cada canal.
// - Twitch: chat IRC anónimo (subs, regalos, raids y bits llegan por el propio chat).
// - Kick: el websocket público de su chat (mensajes, subs, regalos, hosts y follows).
// - YouTube: la API interna de la web (mensajes, Super Chats, miembros y regalos), sin clave ni cupo.
// - TikTok: a través de Euler Stream (necesita una clave gratuita): mensajes, regalos, subs y follows.
// Emotes de 7TV, BetterTTV y FrankerFaceZ en las tres plataformas, y espectadores de cada una.
// Todo se entrega como ChatItem a la página del chat (ChatServer).
using System;
using System.Collections.Generic;
using System.Threading;

namespace StreamDelay
{
    class ChatHub
    {
        public string TwitchChannel = "", KickChannel = "", YouTubeChannel = "", TikTokChannel = "";
        public string TikTokKey = "";       // clave de Euler Stream
        // Avisos extra: follows de Twitch con la cuenta conectada en "Título" y donaciones.
        public TwitchPlatform TwitchApi;
        public string StreamElementsToken = "", StreamlabsToken = "";
        public volatile string FollowsState = "", StreamElementsState = "", StreamlabsState = "";
        public bool TwitchFollowsActive { get { return FollowsState == "activo"; } }
        TwitchFollows follows;
        StreamElementsAlerts se;
        StreamlabsAlerts sl;
        public Action<string> Log = delegate { };
        public Action<ChatItem> Item = delegate { };
        public readonly Emotes Emotes = new Emotes();

        // Espectadores: -1 sin dato, -2 sin directo.
        public volatile int TwitchViewers = -1, KickViewers = -1, YouTubeViewers = -1, TikTokViewers = -1;

        // Juego o categoría de ahora (el de Twitch y, si no, el de Kick); "" si no se sabe.
        public string CurrentGame
        {
            get
            {
                var t = tw; var k = kc;
                string g = (t != null) ? t.Game : "";
                if (string.IsNullOrEmpty(g) && k != null) g = k.Game;
                return g ?? "";
            }
        }
        // Estado de cada conexión, para la interfaz: "" (no configurado), "conectando", "conectado", "sin directo", "error: …"
        public volatile string TwitchState = "", KickState = "", YouTubeState = "", TikTokState = "";

        volatile bool running;
        TwitchChat tw;
        KickChat kc;
        YouTubeChat yt;
        TikTokChat tk;
        Thread viewersThread;
        readonly object stopLock = new object();

        public bool Running { get { return running; } }

        public void Start()
        {
            if (running) return;
            running = true;
            TwitchViewers = KickViewers = YouTubeViewers = TikTokViewers = -1;
            TwitchState = Clean(TwitchChannel).Length > 0 ? "conectando" : "";
            KickState = Clean(KickChannel).Length > 0 ? "conectando" : "";
            YouTubeState = YouTubeChannel.Trim().Length > 0 ? "conectando" : "";
            TikTokState = Clean(TikTokChannel).Length > 0 ? (TikTokKey.Trim().Length > 0 ? "conectando" : "error: falta la clave de Euler Stream") : "";
            Background(() => Emotes.LoadGlobal(Log));
            if (TwitchState.Length > 0) { tw = new TwitchChat(this, Clean(TwitchChannel)); tw.Start(); }
            if (KickState.Length > 0) { kc = new KickChat(this, Clean(KickChannel)); kc.Start(); }
            if (YouTubeState.Length > 0) { yt = new YouTubeChat(this, YouTubeChannel.Trim()); yt.Start(); }
            if (TikTokState == "conectando") { tk = new TikTokChat(this, Clean(TikTokChannel), TikTokKey); tk.Start(); }
            FollowsState = StreamElementsState = StreamlabsState = "";
            if (TwitchState.Length > 0)
            {
                if (TwitchApi != null && TwitchApi.Connected) { FollowsState = "conectando"; follows = new TwitchFollows(this, TwitchApi, Clean(TwitchChannel)); follows.Start(); }
                else FollowsState = "sin conectar";
            }
            if (StreamElementsToken.Trim().Length > 0) { StreamElementsState = "conectando"; se = new StreamElementsAlerts(this, StreamElementsToken); se.Start(); }
            if (StreamlabsToken.Trim().Length > 0) { StreamlabsState = "conectando"; sl = new StreamlabsAlerts(this, StreamlabsToken); sl.Start(); }
            viewersThread = new Thread(ViewersLoop) { IsBackground = true, Name = "chat-espectadores" };
            viewersThread.Start();
            Status();
        }

        public void Stop()
        {
            lock (stopLock)
            {
                running = false;
                if (tw != null) tw.Stop();
                if (kc != null) kc.Stop();
                if (yt != null) yt.Stop();
                if (tk != null) tk.Stop();
                if (follows != null) follows.Stop();
                if (se != null) se.Stop();
                if (sl != null) sl.Stop();
                tw = null; kc = null; yt = null; tk = null; follows = null; se = null; sl = null;
                TwitchState = KickState = YouTubeState = TikTokState = "";
                FollowsState = StreamElementsState = StreamlabsState = "";
                TwitchViewers = KickViewers = YouTubeViewers = TikTokViewers = -1;
            }
            Status();
            Viewers();
        }

        // Nombre de canal limpio: sin "@", sin espacios y sin la URL si pegan el enlace.
        public static string Clean(string s)
        {
            s = (s ?? "").Trim().TrimEnd('/');
            int i = s.LastIndexOf('/');
            if (i >= 0) s = s.Substring(i + 1);
            return s.TrimStart('@', '#').Trim().ToLowerInvariant();
        }

        internal void Emit(ChatItem it) { try { Item(it); } catch { } }

        internal void SetState(string p, string s)
        {
            string old = p == "twitch" ? TwitchState : p == "kick" ? KickState : p == "tiktok" ? TikTokState : YouTubeState;
            if (!running || old == s) return;
            if (p == "twitch") TwitchState = s; else if (p == "kick") KickState = s; else if (p == "tiktok") TikTokState = s; else YouTubeState = s;
            string name = p == "twitch" ? "Twitch" : p == "kick" ? "Kick" : p == "tiktok" ? "TikTok" : "YouTube";
            Log("Chat de " + name + ": " + s + ".");
            Status();
            if (s == "conectado" || s == "sin directo") viewersNow.Set(); // actualizar espectadores ya
        }
        readonly AutoResetEvent viewersNow = new AutoResetEvent(false);

        internal void SetAlertState(string key, string s)
        {
            string old = key == "follows" ? FollowsState : key == "streamelements" ? StreamElementsState : StreamlabsState;
            if (!running || old == s) return;
            if (key == "follows") FollowsState = s; else if (key == "streamelements") StreamElementsState = s; else StreamlabsState = s;
            string name = key == "follows" ? "Follows de Twitch" : key == "streamelements" ? "StreamElements" : "Streamlabs";
            Log(name + ": " + s + ".");
            Status();
        }

        void Status()
        {
            var d = new Dictionary<string, object>();
            d["twitch"] = TwitchState; d["kick"] = KickState; d["youtube"] = YouTubeState; d["tiktok"] = TikTokState;
            d["follows"] = FollowsState; d["streamelements"] = StreamElementsState; d["streamlabs"] = StreamlabsState;
            Emit(new ChatItem { t = "status", data = d });
        }

        void Viewers()
        {
            var d = new Dictionary<string, object>();
            d["twitch"] = TwitchViewers; d["kick"] = KickViewers; d["youtube"] = YouTubeViewers; d["tiktok"] = TikTokViewers;
            Emit(new ChatItem { t = "viewers", data = d });
        }

        // TikTok avisa de los espectadores por su cuenta (cada pocos segundos): se envían a la página
        // enseguida, sin volver a preguntar a las otras plataformas.
        volatile bool tikTokOnly;
        internal void ViewersChanged() { tikTokOnly = true; viewersNow.Set(); }

        // Espectadores cada 30 s (también vale como "¿está en directo?").
        void ViewersLoop()
        {
            int lastSent = -999;
            bool fetch = true;
            while (running)
            {
                if (fetch)
                {
                    var t = tw; var k = kc; var y = yt;
                    if (t != null) TwitchViewers = Try(t.FetchViewers);
                    if (k != null) KickViewers = Try(k.FetchViewers);
                    if (y != null) YouTubeViewers = Try(y.FetchViewers);
                }
                int sig = ((TwitchViewers * 31 + KickViewers) * 17 + YouTubeViewers) * 13 + TikTokViewers;
                if (sig != lastSent) { lastSent = sig; Viewers(); }
                fetch = true;
                for (int i = 0; i < 30 && running; i++)
                {
                    if (!viewersNow.WaitOne(1000)) continue;
                    if (tikTokOnly)
                    {
                        // Solo ha cambiado TikTok: se manda y se sigue esperando a los 30 s.
                        tikTokOnly = false;
                        int s2 = ((TwitchViewers * 31 + KickViewers) * 17 + YouTubeViewers) * 13 + TikTokViewers;
                        if (s2 != lastSent) { lastSent = s2; Viewers(); }
                        continue;
                    }
                    break;
                }
            }
        }

        static int Try(Func<int> f) { try { return f(); } catch { return -1; } }

        internal static void Background(Action a)
        {
            var th = new Thread(() => { try { a(); } catch { } }) { IsBackground = true };
            th.Start();
        }

        internal bool Alive { get { return running; } }
    }
}
