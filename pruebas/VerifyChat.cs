// Banco de pruebas: pasa los mismos datos de ejemplo por las clases del chat de un VortexsLive.exe
// (cargado por reflexión) e imprime todo lo que producen. Se ejecuta con la 3.0 y con la versión
// con el código original, y se comparan las salidas: deben ser idénticas.
// Uso: verifychat.exe VortexsLive.exe [salida.txt]. GitHub compara la salida con pruebas\salida-esperada.txt.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

static class VerifyChat
{
    static Assembly A;
    static object hub;
    static readonly StringBuilder Out = new StringBuilder();
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    static Type T(string n) { return A.GetType("StreamDelay." + n, true); }
    static object Call(object o, string m, params object[] args)
    {
        var t = o as Type ?? o.GetType();
        var mi = t.GetMethods(F).First(x => x.Name == m && x.GetParameters().Length == args.Length);
        try { return mi.Invoke(o is Type ? null : o, args); }
        catch (TargetInvocationException e) { Out.AppendLine("  EXCEPCIÓN " + m + ": " + e.InnerException.GetType().Name + ": " + e.InnerException.Message); return null; }
    }
    static void SetField(object o, string f, object v) { o.GetType().GetField(f, F).SetValue(o, v); }
    static object Make(string type, params object[] args) { return Activator.CreateInstance(T(type), F, null, args, null); }

    static string Dump(object o)
    {
        if (o == null) return "null";
        if (o is string) return "\"" + o + "\"";
        if (o is IEnumerable && !(o is string)) { var l = new List<string>(); foreach (var x in (IEnumerable)o) l.Add(Dump(x)); return "[" + string.Join(", ", l) + "]"; }
        var t = o.GetType();
        if (t.Namespace == "StreamDelay")
            return "{" + string.Join(" ", t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name).Select(f => f.Name + "=" + Dump(f.GetValue(o)))) + "}";
        return Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture);
    }

    static void Section(string s) { Out.AppendLine("== " + s); }

    static int Main(string[] a)
    {
        A = Assembly.LoadFile(Path.GetFullPath(a[0]));
        hub = Make("ChatHub");
        // Emitir y registrar: todo queda en la salida.
        var itemT = typeof(Action<>).MakeGenericType(T("ChatItem"));
        var logField = T("ChatHub").GetField("Log", F); var itemField = T("ChatHub").GetField("Item", F);
        Action<string> log = s => Out.AppendLine("  log: " + s);
        logField.SetValue(hub, log);
        var sink = typeof(VerifyChat).GetMethod("OnItem", F).MakeGenericMethod(T("ChatItem"));
        itemField.SetValue(hub, Delegate.CreateDelegate(itemT, sink));
        // Emotes de terceros de prueba (sin internet): 7TV "KEKW" y "Pog".
        var em = T("ChatHub").GetField("Emotes", F).GetValue(hub);
        var fake = new Dictionary<string, string> { { "KEKW", "https://cdn.7tv.app/emote/K/2x.webp" }, { "Pog", "https://cdn.betterttv.net/emote/P/2x" } };
        SetField(em, "global", fake);

        Section("Emotes.AddText / For");
        foreach (var txt in new[] { "hola KEKW que tal", "KEKW", " Pog  Pog ", "", "sin emotes aquí", "KEKWKEKW Pog." })
        {
            var parts = Activator.CreateInstance(typeof(List<>).MakeGenericType(T("ChatPart")));
            Call(T("Emotes"), "AddText", parts, txt, fake); Out.AppendLine("  " + Dump(parts));
        }
        Out.AppendLine("  For(kick): " + Dump(((IDictionary)Call(em, "For", "kick")).Count));

        Section("J");
        var js = Json.DeserializeObject("{\"a\":{\"b\":[1,{\"c\":\"x\"}]},\"runs\":[{\"text\":\"ho\"},{\"text\":\"la\"}],\"simpleText\":\"\"}");
        Out.AppendLine("  " + Dump(Call(T("J"), "S", js, new object[] { "a", "b", 1, "c" })) + " " + Dump(Call(T("J"), "N", js, 7L, new object[] { "a", "b", 0 })) + " " + Dump(Call(T("J"), "Text", js)) + " " + Dump(Call(T("J"), "L", js, new object[] { "runs" })));

        Section("ChatHub.Clean / SetState / SetAlertState");
        foreach (var c in new[] { "  @Ibai ", "https://twitch.tv/Ibai/", "#canal", "kick.com/xqc", "", null }) Out.AppendLine("  " + Dump(Call(T("ChatHub"), "Clean", c)));
        Call(hub, "SetState", "twitch", "conectado");   // sin arrancar: no hace nada
        SetField(hub, "running", true);
        Call(hub, "SetState", "twitch", "conectado"); Call(hub, "SetState", "twitch", "conectado"); Call(hub, "SetState", "kick", "sin directo"); Call(hub, "SetState", "youtube", "error: x");
        Call(hub, "SetAlertState", "follows", "activo"); Call(hub, "SetAlertState", "streamlabs", "token incorrecto"); Call(hub, "SetAlertState", "streamelements", "conectado");
        Out.AppendLine("  TwitchFollowsActive=" + T("ChatHub").GetProperty("TwitchFollowsActive").GetValue(hub, null));
        SetField(hub, "FollowsState", "");

        Section("TwitchChat");
        var tw = Make("TwitchChat", hub, "canal");
        foreach (var s in new[] { "hola\\sque\\:tal\\\\", "sin escapes", "a\\", "\\r\\n" }) Out.AppendLine("  Unescape " + Dump(Call(T("TwitchChat"), "Unescape", s)));
        foreach (var b in new[] { "broadcaster/1,subscriber/12", "moderator/1,vip/1,founder/0", "partner/1", "verified/1,premium/1", "" }) Out.AppendLine("  Roles " + Dump(Call(T("TwitchChat"), "Roles", b)));
        var wr = new StreamWriter(Stream.Null);
        string[] irc =
        {
            "PING :tmi.twitch.tv",
            "@badge-info=subscriber/8;badges=subscriber/6,premium/1;color=#1E90FF;display-name=Laura;emotes=25:6-10;id=a1;login=laura;msg-id=resub;msg-param-cumulative-months=8;msg-param-sub-plan=1000;room-id=1;system-msg=Laura\\ssubscribed;user-id=11 :tmi.twitch.tv USERNOTICE #canal :Hola! Kappa KEKW qué bien",
            "@badges=;color=;display-name=Pepe;emotes=;id=a2;login=pepe;msg-id=sub;msg-param-sub-plan=Prime;room-id=1;system-msg=x;user-id=12 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Pepe2;id=a2b;login=pepe2;msg-id=sub;msg-param-sub-plan=3000;user-id=12 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=vip/1;display-name=Ana;id=a3;login=ana;msg-id=submysterygift;msg-param-mass-gift-count=5;msg-param-sub-plan=1000;room-id=1;system-msg=x;user-id=13 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Ana;id=a3b;login=ana;msg-id=submysterygift;msg-param-mass-gift-count=1;user-id=13 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Ana;id=a4;login=ana;msg-id=subgift;msg-param-community-gift-id=999;msg-param-recipient-display-name=Luis;room-id=1;system-msg=x;user-id=13 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=AnAnonymousGifter;id=a5;login=ananonymousgifter;msg-id=subgift;msg-param-recipient-display-name=Marta;room-id=1;system-msg=x;user-id=274598607 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=broadcaster/1;display-name=OtroStreamer;id=a6;login=otrostreamer;msg-id=raid;msg-param-displayName=OtroStreamer;msg-param-viewerCount=320;room-id=1;system-msg=x;user-id=14 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=X;id=a6b;login=x;msg-id=raid;msg-param-viewerCount=5;user-id=14 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Y;id=a6c;login=y;msg-id=primepaidupgrade;user-id=15 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=Z;id=a6d;login=z;msg-id=announcement;emotes=;user-id=15 :tmi.twitch.tv USERNOTICE #canal :Anuncio importante Pog",
            "@badges=;display-name=W;id=a6e;login=w;msg-id=bitsbadgetier;system-msg=W\\sdesbloqueó\\sinsignia;user-id=15 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=;display-name=V;id=a6f;login=v;msg-id=desconocido;user-id=15 :tmi.twitch.tv USERNOTICE #canal",
            "@badges=moderator/1;bits=500;color=#00FF7F;display-name=Mod1;emotes=;id=a7;user-id=15 :mod1!mod1@mod1.tmi.twitch.tv PRIVMSG #canal :Cheer500 vamos!",
            "@badges=;bits=0;display-name=;emotes=;id=a7b;user-id=15 :sinnombre!x@x.tmi.twitch.tv PRIVMSG #canal :bits cero",
            "@badges=;color=#000000;display-name=Oscuro;emotes=1902:0-4,9-13;id=a8;user-id=16 :oscuro!oscuro@oscuro.tmi.twitch.tv PRIVMSG #canal :Keepo y😀 Keepo fin KEKW",
            "@badges=;display-name=Act;emotes=;id=a9;user-id=17 :act!act@act.tmi.twitch.tv PRIVMSG #canal :\u0001ACTION baila\u0001",
            "@badges=;display-name=Mal;emotes=25:99-120/abc;id=a10;user-id=18 :mal!m@m PRIVMSG #canal :texto corto",
            "@login=x;target-msg-id=abc-123 :tmi.twitch.tv CLEARMSG #canal :borrado",
            "@target-user-id=999 :tmi.twitch.tv CLEARCHAT #canal :baneado",
            ":tmi.twitch.tv CLEARCHAT #canal",
            "@msg-id=msg_channel_suspended :tmi.twitch.tv NOTICE #canal :This channel has been suspended.",
            "@msg-id=otro :tmi.twitch.tv NOTICE #canal :otro aviso",
        };
        foreach (var l in irc) { Out.AppendLine(" > " + (l.Length > 60 ? l.Substring(0, 60) : l)); Call(tw, "Handle", l, wr); }
        try { Call(tw, "Handle", "RECONNECT", wr); } catch { }

        Section("KickChat");
        var kc = Make("KickChat", hub, "canal");
        string[] kick =
        {
            "{\"event\":\"App\\\\Events\\\\ChatMessageEvent\",\"data\":\"{\\\"id\\\":\\\"m1\\\",\\\"content\\\":\\\"hola [emote:37226:KEKW] y KEKW Pog [emote:1:x]\\\",\\\"sender\\\":{\\\"id\\\":7,\\\"username\\\":\\\"fan\\\",\\\"identity\\\":{\\\"color\\\":\\\"#FF0000\\\",\\\"badges\\\":[{\\\"type\\\":\\\"moderator\\\"},{\\\"type\\\":\\\"og\\\"},{\\\"type\\\":\\\"subscriber\\\",\\\"count\\\":3},{\\\"type\\\":\\\"broadcaster\\\"},{\\\"type\\\":\\\"vip\\\"},{\\\"type\\\":\\\"founder\\\"},{\\\"type\\\":\\\"verified\\\"},{\\\"type\\\":\\\"raro\\\"}]}}}\"}",
            "{\"event\":\"App\\\\Events\\\\ChatMessageEvent\",\"data\":{\"id\":\"m2\",\"content\":\"\",\"sender\":{\"username\":\"vacío\"}}}",
            "{\"event\":\"App\\\\Events\\\\MessageDeletedEvent\",\"data\":{\"message\":{\"id\":\"m1\"}}}",
            "{\"event\":\"App\\\\Events\\\\UserBannedEvent\",\"data\":{\"user\":{\"id\":7,\"username\":\"fan\"}}}",
            "{\"event\":\"App\\\\Events\\\\SubscriptionEvent\",\"data\":{\"username\":\"sub1\",\"months\":1}}",
            "{\"event\":\"App\\\\Events\\\\SubscriptionEvent\",\"data\":{\"username\":\"sub2\",\"months\":6}}",
            "{\"event\":\"App\\\\Events\\\\GiftedSubscriptionsEvent\",\"data\":{\"gifter_username\":\"rico\",\"gifted_usernames\":[\"a\",\"b\",\"c\"]}}",
            "{\"event\":\"App\\\\Events\\\\GiftedSubscriptionsEvent\",\"data\":{\"gifter_username\":\"\",\"gifted_usernames\":[\"a\"]}}",
            "{\"event\":\"App\\\\Events\\\\StreamHostEvent\",\"data\":{\"host_username\":\"amigo\",\"number_viewers\":42}}",
            "{\"event\":\"App\\\\Events\\\\FollowersUpdated\",\"data\":{\"username\":\"nuevo\",\"followed\":true}}",
            "{\"event\":\"App\\\\Events\\\\FollowersUpdated\",\"data\":{\"username\":\"adios\",\"followed\":false}}",
            "{\"event\":\"App\\\\Events\\\\KicksGifted\",\"data\":{\"sender\":{\"username\":\"generoso\"},\"gift\":{\"amount\":100}}}",
            "{\"event\":\"App\\\\Events\\\\GiftOtro\",\"data\":{\"sender\":{\"username\":\"\"},\"gift\":{\"amount\":1}}}",
            "{\"event\":\"pusher:error\",\"data\":{\"message\":\"fallo\"}}",
            "{\"event\":\"pusher_internal:subscription_succeeded\",\"data\":{}}",
            "{\"event\":\"desconocido\",\"data\":{}}",
            "no es json",
        };
        foreach (var k in kick) { Out.AppendLine(" > " + (k.Length > 60 ? k.Substring(0, 60) : k)); Call(kc, "Handle", k); }

        Section("YouTubeChat");
        var yt = Make("YouTubeChat", hub, "@canal");
        foreach (var inp in new[] { "@LofiGirl", "https://www.youtube.com/watch?v=4t4AQ15yj1M", "https://youtu.be/4t4AQ15yj1M", "4t4AQ15yj1M", "UCSJ4gkVC6NrvII8umztf0Ow", "https://www.youtube.com/c/Canal", "https://www.youtube.com/@a.b-c/live", "canal" })
            Out.AppendLine("  PageUrl(" + inp + ") = " + Call(Make("YouTubeChat", hub, inp), "PageUrl"));
        foreach (var v in new long[] { 0, 4280391411, 4294953512, 1 }) Out.AppendLine("  Argb " + Dump(Call(T("YouTubeChat"), "Argb", v)));
        string[] ytActs =
        {
            "{\"addChatItemAction\":{\"item\":{\"liveChatTextMessageRenderer\":{\"id\":\"y1\",\"authorExternalChannelId\":\"UC1\",\"authorName\":{\"simpleText\":\"@Pepa\"},\"authorBadges\":[{\"liveChatAuthorBadgeRenderer\":{\"icon\":{\"iconType\":\"MODERATOR\"}}},{\"liveChatAuthorBadgeRenderer\":{\"customThumbnail\":{}}},{\"liveChatAuthorBadgeRenderer\":{\"icon\":{\"iconType\":\"OWNER\"}}},{\"liveChatAuthorBadgeRenderer\":{\"icon\":{\"iconType\":\"VERIFIED\"}}}],\"message\":{\"runs\":[{\"text\":\"hola KEKW \"},{\"emoji\":{\"emojiId\":\"😀\",\"isCustomEmoji\":false}},{\"emoji\":{\"emojiId\":\"UCx/abc\",\"isCustomEmoji\":true,\"shortcuts\":[\":gato:\"],\"image\":{\"thumbnails\":[{\"url\":\"s\"},{\"url\":\"grande\"}]}}}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatTextMessageRenderer\":{\"id\":\"y1b\",\"authorName\":{\"simpleText\":\"SinTexto\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatPaidMessageRenderer\":{\"id\":\"y2\",\"authorName\":{\"simpleText\":\"Rico\"},\"purchaseAmountText\":{\"simpleText\":\"5,00 €\"},\"bodyBackgroundColor\":4280391411,\"message\":{\"runs\":[{\"text\":\"gracias\"}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatPaidMessageRenderer\":{\"id\":\"y2b\",\"authorName\":{\"simpleText\":\"Rico2\"},\"purchaseAmountText\":{\"simpleText\":\"$2.00\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatPaidStickerRenderer\":{\"id\":\"y3\",\"authorName\":{\"simpleText\":\"Pega\"},\"purchaseAmountText\":{\"simpleText\":\"1,99 €\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatMembershipItemRenderer\":{\"id\":\"y4\",\"authorName\":{\"simpleText\":\"Miembro\"},\"headerSubtext\":{\"runs\":[{\"text\":\"Te damos la \"},{\"text\":\"bienvenida\"}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatMembershipItemRenderer\":{\"id\":\"y4b\",\"authorName\":{\"simpleText\":\"Veterano\"},\"headerPrimaryText\":{\"runs\":[{\"text\":\"Miembro durante 5 meses\"}]},\"message\":{\"runs\":[{\"text\":\"hola\"}]}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatMembershipItemRenderer\":{\"id\":\"y4c\",\"authorName\":{\"simpleText\":\"Nadie\"}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatSponsorshipsGiftPurchaseAnnouncementRenderer\":{\"id\":\"y5\",\"header\":{\"liveChatSponsorshipsHeaderRenderer\":{\"authorName\":{\"simpleText\":\"Gen\"},\"primaryText\":{\"runs\":[{\"text\":\"ha regalado 5 membresías\"}]}}}}}}}",
            "{\"addChatItemAction\":{\"item\":{\"liveChatViewerEngagementMessageRenderer\":{}}}}",
            "{\"markChatItemAsDeletedAction\":{\"targetItemId\":\"y1\"}}",
            "{\"markChatItemsByAuthorAsDeletedAction\":{\"externalChannelId\":\"UC1\"}}",
            "{\"otraAccion\":{}}",
        };
        foreach (var s in ytActs) { Out.AppendLine(" > " + s.Substring(0, Math.Min(60, s.Length))); Call(yt, "Action", Json.DeserializeObject(s)); }

        Section("Money");
        foreach (var m in new object[] { 5, 12.5, "3", "abc", null, 1234567.891 }) foreach (var c in new[] { "EUR", "usd", "GBP", "MXN", "ARS", "BRL", "JPY", "", null })
            Out.AppendLine("  " + Dump(m) + " " + Dump(c) + " → " + Dump(Call(T("WsClient"), "Money", m, c)));

        Section("Avisos");
        var tf = Make("TwitchFollows", hub, null, "canal");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"notification\",\"subscription_type\":\"channel.follow\"},\"payload\":{\"event\":{\"user_id\":\"123\",\"user_login\":\"nuevo_seguidor\",\"user_name\":\"Nuevo_Seguidor\"}}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"notification\",\"subscription_type\":\"channel.follow\"},\"payload\":{\"event\":{\"user_id\":\"124\",\"user_login\":\"solo_login\",\"user_name\":\"\"}}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"notification\",\"subscription_type\":\"otra\"},\"payload\":{}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"revocation\"}}");
        Call(tf, "OnText", "{\"metadata\":{\"message_type\":\"session_keepalive\"}}");
        var se = Make("StreamElementsAlerts", hub, " tok ");
        foreach (var s in new[] {
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"tip\",\"provider\":\"twitch\",\"data\":{\"username\":\"donante\",\"displayName\":\"Donante\",\"amount\":5,\"currency\":\"EUR\",\"message\":\"Ánimo KEKW\"}}}",
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"tip\",\"data\":{\"amount\":\"7.5\",\"currency\":\"USD\"}}}",
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"follower\",\"provider\":\"twitch\",\"data\":{\"username\":\"fan2\"}}}",
            "{\"type\":\"message\",\"topic\":\"channel.activities\",\"data\":{\"type\":\"follower\",\"provider\":\"youtube\",\"data\":{\"username\":\"fan3\"}}}",
            "{\"type\":\"message\",\"topic\":\"otro\",\"data\":{}}",
            "{\"type\":\"response\",\"error\":\"err_bad_request\"}",
            "{\"type\":\"response\"}",
            "{\"type\":\"response\",\"error\":\"err_unauthorized\"}" }) Call(se, "OnText", s);
        var sl = Make("StreamlabsAlerts", hub, "tok");
        foreach (var s in new[] {
            "0{\"sid\":\"x\",\"pingInterval\":10000}", "0{\"pingInterval\":999999}", "40", "3",
            "42[\"event\",{\"type\":\"donation\",\"message\":[{\"name\":\"Pablo\",\"amount\":\"12.5\",\"formatted_amount\":\"12,50 €\",\"currency\":\"EUR\",\"message\":\"Para un café Pog\"},{\"name\":\"\",\"amount\":\"3\",\"currency\":\"USD\",\"message\":\"\"}],\"for\":\"streamlabs\"}]",
            "42[\"event\",{\"type\":\"follow\",\"message\":[{\"name\":\"segui\"}],\"for\":\"twitch_account\"}]",
            "42[\"event\",{\"type\":\"follow\",\"message\":[{\"name\":\"yt\"}],\"for\":\"youtube_account\"}]",
            "42[\"otro\",{}]", "44\"Authentication error\"" }) Call(sl, "OnText", s);
        Out.AppendLine("  PingMs=" + sl.GetType().BaseType.GetField("PingMs", F).GetValue(sl));

        Section("TikTokChat");
        var tk = Make("TikTokChat", hub, "vortexs", "clave");
        string[] tiktok =
        {
            "{\"type\":\"tiktok.connect\",\"data\":{\"agentId\":\"a1\"}}",
            "{\"timestamp\":1,\"messages\":[{\"type\":\"WebcastChatMessage\",\"data\":{\"common\":{\"msgId\":\"7300000000000000001\"},\"user\":{\"id\":\"6800000000000000001\",\"nickname\":\"Laura 🌸\",\"uniqueId\":\"laura_tt\"},\"content\":\"hola KEKW qué tal\",\"emotes\":[{\"index\":5,\"emote\":{\"emoteId\":\"e1\",\"image\":{\"url\":[\"https://p16.tiktokcdn.com/e1.webp\"]}}}],\"userIdentity\":{\"isModeratorOfAnchor\":true,\"isSubscriberOfAnchor\":true}}},{\"type\":\"WebcastChatMessage\",\"data\":{\"common\":{\"msgId\":\"7300000000000000002\"},\"user\":{\"idStr\":\"6800000000000000002\",\"displayId\":\"solo_id\",\"nickname\":\"\"},\"comment\":\"emote al final\",\"emotes\":[{\"placeInComment\":99,\"emote\":{\"image\":{\"imageUrl\":\"https://p16.tiktokcdn.com/e2.webp\"}}}],\"UserIdentity\":{\"isAnchor\":true}}}]}",
            "{\"messages\":[{\"type\":\"WebcastGiftMessage\",\"data\":{\"user\":{\"nickname\":\"Racha\"},\"repeatCount\":3,\"repeatEnd\":false,\"gift\":{\"name\":\"Rosa\",\"diamondCount\":1,\"type\":1}}},{\"type\":\"WebcastGiftMessage\",\"data\":{\"user\":{\"nickname\":\"Racha\"},\"repeatCount\":5,\"repeatEnd\":1,\"gift\":{\"name\":\"Rosa\",\"diamondCount\":1,\"type\":1}}},{\"type\":\"WebcastGiftMessage\",\"data\":{\"user\":{\"nickname\":\"Ballena\"},\"repeatCount\":1,\"giftDetails\":{\"giftName\":\"León\",\"diamondCount\":29999,\"giftType\":2}}},{\"type\":\"WebcastGiftMessage\",\"data\":{\"user\":{},\"repeatCount\":0,\"gift\":{}}}]}",
            "{\"messages\":[{\"type\":\"WebcastSubNotifyMessage\",\"data\":{\"user\":{\"nickname\":\"Fiel\"},\"subMonth\":\"3\"}},{\"type\":\"WebcastSubNotifyMessage\",\"data\":{\"user\":{\"uniqueId\":\"nuevo_sub\"}}}]}",
            "{\"messages\":[{\"type\":\"WebcastSocialMessage\",\"data\":{\"common\":{\"displayText\":{\"key\":\"pm_main_follow_message_viewer_2\"}},\"user\":{\"nickname\":\"Seguidora\"}}},{\"type\":\"WebcastSocialMessage\",\"data\":{\"common\":{\"displayText\":{\"key\":\"pm_mt_guidance_share\"}},\"user\":{\"nickname\":\"Comparte\"}}}]}",
            "{\"messages\":[{\"type\":\"WebcastLikeMessage\",\"data\":{\"count\":15}},{\"type\":\"WebcastMemberMessage\",\"data\":{\"action\":1}},{\"type\":\"WebcastRoomUserSeqMessage\",\"data\":{\"viewerCount\":312}}]}",
            "{\"messages\":[{\"type\":\"WebcastRoomUserSeqMessage\",\"data\":{\"total\":\"1500\",\"totalUser\":9000}}]}",
            "{\"messages\":[{\"type\":\"WebcastEmoteChatMessage\",\"data\":{\"common\":{\"msgId\":\"7300000000000000003\"},\"user\":{\"id\":\"9\",\"nickname\":\"Sticker\"},\"emoteList\":[{\"emoteId\":\"s1\",\"image\":{\"urlList\":[\"https://p16.tiktokcdn.com/s1.webp\"]}}]}}]}",
            "{\"messages\":[{\"type\":\"WebcastImDeleteMessage\",\"data\":{\"deleteMsgIdsList\":[\"7300000000000000001\"],\"deleteUserIdsList\":[\"6800000000000000002\"]}}]}",
            "{\"type\":\"WebcastChatMessage\",\"data\":{\"user\":{\"nickname\":\"Suelto\"},\"content\":\"sin paquete Pog\"}}",
            "{\"messages\":[{\"type\":\"WebcastChatMessage\",\"data\":{\"user\":{\"nickname\":\"Vacío\"},\"content\":\"\"}}]}",
            "{\"messages\":[{\"type\":\"WebcastControlMessage\",\"data\":{\"action\":\"CONTROL_ACTION_STREAM_ENDED\"}}]}",
            "{\"type\":\"room.status\",\"data\":{\"state\":\"connected\"}}",
            "{\"type\":\"workerInfo\",\"data\":{}}",
            "no es json",
        };
        foreach (var s in tiktok) { Out.AppendLine(" > " + (s.Length > 60 ? s.Substring(0, 60) : s)); Call(tk, "Handle", s); }
        Out.AppendLine("  TikTokViewers=" + T("ChatHub").GetField("TikTokViewers", F).GetValue(hub));
        foreach (var c in new object[] { 4404, 4005, 4555, 4429, 1000, 4401 }) { Out.AppendLine(" > cierre " + c); Out.AppendLine("  espera " + Dump(Call(tk, "Closed", c, c.ToString() == "1000" ? "adiós" : ""))); }
        Call(tk, "State", "error: The server returned status code '401' when status code '101' was expected.");

        Section("AutoModRules");
        var am = Activator.CreateInstance(T("AutoModRules"));
        Func<string, string, string[], object[], object> item = (p, user, roles, parts) =>
        {
            var it = Activator.CreateInstance(T("ChatItem"));
            SetField(it, "t", "msg"); SetField(it, "p", p); SetField(it, "user", user); SetField(it, "roles", roles);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(T("ChatPart")));
            foreach (var x in parts)
            {
                var cp = Activator.CreateInstance(T("ChatPart"));
                var arr = x as string[];
                if (arr != null) { SetField(cp, "e", arr[0]); SetField(cp, "n", arr[1]); } else SetField(cp, "x", (string)x);
                list.Add(cp);
            }
            SetField(it, "parts", list);
            return it;
        };
        Action<string, object> check = (label, it) =>
        {
            var args = new object[] { it, 0 };
            var mi = T("AutoModRules").GetMethod("Check", F);
            var r = mi.Invoke(am, args);
            Out.AppendLine("  " + label + " → " + Dump(r) + " act=" + args[1]);
        };
        check("apagada", item("twitch", "pepe", new string[0], new object[] { "eres tonto" }));
        SetField(am, "On", true);
        SetField(am, "Words", "tonto\r\npalabr*\r\n  \r\nmala frase");
        check("palabra", item("twitch", "pepe", new string[0], new object[] { "eres TÓNTO" }));
        check("dentro de otra", item("twitch", "pepe", new string[0], new object[] { "tontorrón" }));
        check("comodín", item("kick", "pepe", new string[0], new object[] { "qué palabrota" }));
        check("frase", item("youtube", "pepe", new string[0], new object[] { "esto es una mala frase, sí" }));
        check("mod", item("twitch", "moderador", new[] { "mod" }, new object[] { "tonto" }));
        check("streamer", item("twitch", "yo", new[] { "broadcaster" }, new object[] { "tonto" }));
        check("sub (exento)", item("twitch", "subby", new[] { "sub" }, new object[] { "tonto" }));
        SetField(am, "ExemptSubs", false);
        check("sub (no exento)", item("twitch", "subby", new[] { "sub" }, new object[] { "tonto" }));
        check("enlace", item("twitch", "spam1", new string[0], new object[] { "mira mi canal en http://x.com/a" }));
        check("dominio", item("twitch", "spam1", new string[0], new object[] { "entra en gratis.gg ya" }));
        check("sin enlace", item("twitch", "spam1", new string[0], new object[] { "son las 3.30 de la tarde" }));
        SetField(am, "Caps", true);
        check("mayúsculas", item("twitch", "grita", new string[0], new object[] { "HOLA A TODOS QUE TAL" }));
        check("pocas letras", item("twitch", "grita", new string[0], new object[] { "GG WP" }));
        SetField(am, "Emotes", true);
        var many = new List<object>(); for (int i = 0; i < 12; i++) many.Add(new[] { "https://e/" + i, "KEKW" });
        check("emotes", item("twitch", "emoter", new string[0], many.ToArray()));
        check("repetido 1", item("kick", "rep", new string[0], new object[] { "compra ya" }));
        check("repetido 2", item("kick", "rep", new string[0], new object[] { "Compra ya" }));
        check("repetido 3", item("kick", "rep", new string[0], new object[] { "compra ya" }));
        SetField(am, "HideBots", true);
        check("bot", item("twitch", "Nightbot", new[] { "mod" }, new object[] { "!comandos" }));
        var ign = (HashSet<string>)T("AutoModRules").GetField("Ignored", F).GetValue(am); ign.Add("pesado");
        check("ignorado", item("tiktok", "@Pesado", new string[0], new object[] { "hola" }));
        SetField(am, "On", false);
        check("ignorado (apagada)", item("tiktok", "pesado", new string[0], new object[] { "hola" }));
        Out.AppendLine("  Fold " + Dump(Call(T("AutoModRules"), "Fold", "ÁÉÍÓÚ Ñandú Ü")));

        Section("ChatStyle");
        var csT = T("ChatStyle");
        Out.AppendLine("  defecto " + Call(Activator.CreateInstance(csT), "ToJson"));
        for (int i = 0; i < 8; i++) Out.AppendLine("  " + i + " " + Call(Call(csT, "Preset", i), "ToJson"));
        var raro = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"font\":\"Arial\",\"size\":999,\"tx\":\"rojo\",\"bg\":\"x\",\"op\":-5,\"fade\":99999,\"anim\":\"bailar\",\"panel\":true}"));
        Out.AppendLine("  valores raros " + Call(Call(csT, "Load", raro), "ToJson"));
        Out.AppendLine("  no base64 " + Call(Call(csT, "Load", "%%%"), "ToJson"));
        var p5 = Call(csT, "Preset", 5);
        Out.AppendLine("  ida y vuelta " + (Call(Call(csT, "Load", Call(p5, "Save")), "ToJson").Equals(Call(p5, "ToJson"))));

        Section("License");
        var lic = T("License");
        // Claves de prueba que caducaron el 1/2/2026 (Completo a nombre de "Banco de pruebas" y Básico sin nombre).
        const string kFull = "SD1-AQIAH88iu8BCYW5jbyBkZSBwcnVlYmFzx24FN8GxudFAAJTPDV2YL0LMuUbco5Vdx9Tlr_UGDhRnPbsHsfEXhkUQjFc4efHtgr82zUavxrxOrlu07Zhj_p13vMU2gAhR-83hpY4TvO0Ysfn46dCP68bRQEIBTf94PHhEvE0Gfh2zAA1jf8PTnAW7GtMr1wsCpqXkNbc96Lw";
        const string kBasic = "SD1-AQEAH-JLiIXemjDIuwV5Ovv_oPnT9pkrJbOjAKV5qAJUfIUUZDhl0xXcFighLve9IdsSrwvXli-Rejf_aH8WKpvzqSm3rkHku0wZ0AKzzIjXnpbJ8h6wTSkUQ8iBEONEaBUeOXkbqHTzogCW1r8YhK2i2JVtguwnYvIS4SM4ZrctNCzqcv6z6A";
        var enero = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var marzo = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        Action<string, string, DateTime> chk = (name, k, when) => Out.AppendLine("  " + name + " " + Dump(Call(lic, "CheckSigned", k, when)));
        chk("completo (enero)", kFull, enero);
        chk("completo (marzo)", kFull, marzo);
        chk("básico (enero)", kBasic, enero);
        chk("con espacios y saltos", "  " + kFull.Substring(0, 60) + "\r\n " + kFull.Substring(60) + " ", enero);
        chk("en minúsculas el prefijo", "sd1-" + kFull.Substring(4), enero);
        chk("un carácter cambiado", kFull.Substring(0, 30) + (kFull[30] == 'A' ? 'B' : 'A') + kFull.Substring(31), enero);
        chk("nombre cambiado", "SD1-AQIAH88iu8BDYW5jbyBkZSBwcnVlYmFz" + kFull.Substring(40), enero);
        chk("cortada", kFull.Substring(0, 120), enero);
        // Primera clave de regalo (anulada el 18/11/2026: los regalos son ahora códigos de Polar de un solo uso).
        chk("regalo anulada", "SD1-AQIAAPPQ9Y9SZWdhbG8gZGUgVm9ydGV4cyAwMdJT31DIkLUCytW8qqgdj5oNS046S8_QdkcFvY4Nm3O_0je03xskzgFm2pjbf2sTm3srjK83Z7_g8dlgVQ8sPv44qHA3aJS1edXg9_XNF20Ox962BF51UVjh1cZ_LqKWVxRwHBFitvHvMqU8wnFMaDNQu_Y9Wk_jBoJxUUwo3ASd", marzo);
        chk("otra cosa", "POLAR-1234-5678", enero);
        chk("vacía", "", enero);
        Out.AppendLine("  IsSigned " + Call(lic, "IsSigned", kFull) + " " + Call(lic, "IsSigned", "ABCD") + " " + Call(lic, "IsSigned", (object)null));
        var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        foreach (var h in new[] { -2.0, 0.0, 1.0, 47.5, 71.9, 72.0, 200.0 })
            Out.AppendLine("  prueba +" + h + " h " + Dump(Call(lic, "Trial", start, start.AddHours(h))));
        foreach (var h in new[] { 72.0, 49.0, 24.0, 23.5, 5.2, 0.5 })
            Out.AppendLine("  quedan " + h + " h: " + Call(lic, "Left", start.AddHours(h), start));
        Func<string, Dictionary<string, object>> pj = s => (Dictionary<string, object>)Json.DeserializeObject(s);
        Out.AppendLine("  polar (ventaja de otro producto) " + Dump(Call(lic, "FromPolar", pj("{\"benefit_id\":\"b1\",\"status\":\"granted\",\"customer\":{\"email\":\"a@b.c\"}}"), enero)));
        const string bB = "fbce853e-32ae-4d4e-acce-558838710977", bC = "3346c6b5-e14c-4fd1-8412-7b47ff344101";
        Out.AppendLine("  polar completo sin activar " + Dump(Call(lic, "FromPolar", pj("{\"benefit_id\":\"" + bC + "\",\"status\":\"granted\",\"limit_activations\":2,\"activation\":null,\"expires_at\":null,\"customer\":{\"email\":\"a@b.c\"}}"), enero)));
        Out.AppendLine("  polar básico activado " + Dump(Call(lic, "FromPolar", pj("{\"benefit_id\":\"" + bB + "\",\"status\":\"granted\",\"limit_activations\":2,\"activation\":{\"id\":\"x\"},\"customer\":{}}"), enero)));
        Out.AppendLine("  polar regalo " + Dump(Call(lic, "FromPolar", pj("{\"benefit_id\":\"b96352a3-f89d-43a9-b129-3708a34e538a\",\"status\":\"granted\",\"limit_activations\":1,\"activation\":{\"id\":\"x\"},\"expires_at\":null,\"customer\":{\"email\":\"amigo@ejemplo.com\"}}"), enero)));
        Out.AppendLine("  polar revocada " + Dump(Call(lic, "FromPolar", pj("{\"benefit_id\":\"" + bC + "\",\"status\":\"revoked\"}"), enero)));
        Out.AppendLine("  polar caducada " + Dump(Call(lic, "FromPolar", pj("{\"benefit_id\":\"" + bB + "\",\"status\":\"granted\",\"expires_at\":\"2026-01-10T00:00:00Z\"}"), enero)));
        Out.AppendLine("  polar null " + Dump(Call(lic, "FromPolar", null, enero)));
        Out.AppendLine("  PlanForBenefit '' " + Call(lic, "PlanForBenefit", "") + " · PolarReady " + lic.GetProperty("PolarReady", F).GetValue(null, null));
        foreach (var p in new[] { 0, 1, 2 }) Out.AppendLine("  PlanName " + p + " " + Call(lic, "PlanName", Enum.ToObject(T("Plan"), p)));

        // Chat compartido de Twitch: los mensajes de otros canales llevan su nombre e imagen (caché
        // rellenada aquí para no depender de internet) y no cuentan como bits ni avisos propios.
        Section("TwitchChat compartido");
        var srcCache = (IDictionary)T("TwitchChat").GetField("sources", F).GetValue(tw);
        srcCache["200"] = new[] { "CanalAmigo", "https://static-cdn.jtvnw.net/x.png" };
        srcCache["300"] = new[] { "", "" };
        string[] shared =
        {
            "@badges=;display-name=Propio;emotes=;id=s1;room-id=100;source-room-id=100;user-id=30 :propio!p@p PRIVMSG #canal :del canal propio",
            "@badges=subscriber/6;bits=100;display-name=Visita;emotes=;id=s2;room-id=100;source-room-id=200;user-id=31 :visita!v@v PRIVMSG #canal :Cheer100 hola",
            "@badges=;display-name=SinNombre;emotes=;id=s3;room-id=100;source-room-id=300;user-id=33 :sn!s@s PRIVMSG #canal :canal desconocido",
            "@badges=;display-name=Fan;emotes=;id=s4;login=fan;msg-id=sharedchatnotice;room-id=100;source-room-id=200;source-msg-id=sub;system-msg=Fan\\ssubscribed.;user-id=32 :tmi.twitch.tv USERNOTICE #canal",
        };
        foreach (var l in shared) { Out.AppendLine(" > " + (l.Length > 60 ? l.Substring(0, 60) : l)); Call(tw, "Handle", l, wr); }

        // Con un segundo argumento se guarda en ese archivo (UTF-8), sin depender de la consola.
        if (a.Length > 1) File.WriteAllText(a[1], Out.ToString(), new UTF8Encoding(false));
        else { Console.OutputEncoding = Encoding.UTF8; Console.Write(Out.ToString()); }
        return 0;
    }

    static void OnItem<T>(T it) { Out.AppendLine("  item " + Dump(it)); }
}
