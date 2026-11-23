using System;

namespace StreamDelay
{
    // Follows de Twitch (EventSub por websocket) con la cuenta conectada en la pestaña "Título".
    class TwitchFollows : WsClient
    {
        readonly TwitchPlatform api;
        readonly string channel;
        string reconnectUrl;
        volatile bool migrating;   // Twitch pidió cambiar de servidor: la suscripción se conserva

        public TwitchFollows(ChatHub h, TwitchPlatform api, string channel) : base(h) { this.api = api; this.channel = channel; IdleTimeoutMs = 30000; }

        protected override string Name { get { return "twitch-follows"; } }
        protected override void State(string s) { hub.SetAlertState("follows", s); }

        // Canjes de puntos del canal por la misma conexión. Si falla, los follows siguen igual.
        void SubscribeRedemptions(string sid)
        {
            try
            {
                if (!api.IsOwnChannel(channel)) return;   // solo el dueño del canal ve los canjes
                if (!api.HasScope("channel:read:redemptions"))
                {
                    hub.Log("Canjes de puntos de Twitch: vuelve a conectar Twitch una vez (Directo → Título y categoría) para verlos en el chat.");
                    return;
                }
                api.SubscribeRedemptions(sid, channel);
            }
            catch (Exception ex) { hub.Log("Canjes de puntos de Twitch: " + ex.Message); }
        }

        protected override string Url()
        {
            string u = reconnectUrl ?? "wss://eventsub.wss.twitch.tv/ws";
            reconnectUrl = null;
            return u;
        }

        protected override void OnText(string s)
        {
            var o = Web.Parse(s);
            string type = J.S(o, "metadata", "message_type");
            if (type == "session_welcome")
            {
                long ka = J.N(o, 10, "payload", "session", "keepalive_timeout_seconds");
                IdleTimeoutMs = (int)(ka + 15) * 1000;
                if (migrating) { migrating = false; State("activo"); return; }
                string sid = J.S(o, "payload", "session", "id");
                // Twitch da 10 s para suscribirse tras la bienvenida.
                ChatHub.Background(() =>
                {
                    try
                    {
                        if (!api.CanReadFollows()) { Fatal("falta permiso: vuelve a conectar Twitch"); return; }
                        api.SubscribeFollows(sid, channel);
                        State("activo");
                        SubscribeRedemptions(sid);
                    }
                    catch (ApiException ex)
                    {
                        if (ex.Status == 403) Fatal("solo el dueño del canal o un moderador puede ver sus follows");
                        else if (ex.Status == 401) Fatal("sesión de Twitch caducada: vuelve a conectar Twitch");
                        else State("error: " + ex.Message);
                    }
                    catch (Exception ex) { State("error: " + ex.Message); }
                });
            }
            else if (type == "session_reconnect")
            {
                reconnectUrl = J.S(o, "payload", "session", "reconnect_url");
                migrating = true;
                try { ws.Abort(); } catch { }
            }
            else if (type == "notification" && J.S(o, "metadata", "subscription_type") == "channel.follow")
            {
                string u = J.S(o, "payload", "event", "user_name");
                if (u.Length == 0) u = J.S(o, "payload", "event", "user_login");
                hub.Emit(new ChatItem { t = "ev", p = "twitch", kind = "follow", user = u, uid = J.S(o, "payload", "event", "user_id"), text = u + " te sigue" });
            }
            else if (type == "notification" && J.S(o, "metadata", "subscription_type") == "channel.channel_points_custom_reward_redemption.add")
            {
                string u = J.S(o, "payload", "event", "user_name");
                if (u.Length == 0) u = J.S(o, "payload", "event", "user_login");
                string title = J.S(o, "payload", "event", "reward", "title");
                long cost = J.N(o, 0, "payload", "event", "reward", "cost");
                // Lo que escribe el espectador (si la recompensa lo pide) llega también como mensaje
                // normal por el chat, con sus emotes: aquí solo va el canje.
                hub.Emit(new ChatItem
                {
                    t = "ev", p = "twitch", kind = "reward", user = u, uid = J.S(o, "payload", "event", "user_id"),
                    text = u + " ha canjeado " + title, amount = cost > 0 ? cost.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-ES")) + " pts" : null
                });
            }
            else if (type == "revocation" && J.S(o, "payload", "subscription", "type") == "channel.channel_points_custom_reward_redemption.add")
            {
                hub.Log("Canjes de puntos de Twitch: Twitch retiró el permiso. Vuelve a conectar Twitch en Directo → Título y categoría.");
            }
            else if (type == "revocation")
            {
                Fatal("Twitch retiró el permiso de follows: vuelve a conectar Twitch");
            }
        }
    }
}
