using System.Collections.Generic;

namespace StreamDelay
{
    // Lo que recibe la página (nombres cortos: se envía como JSON).
    class ChatItem
    {
        public string t;          // "msg" mensaje · "ev" evento · "del" borrado · "viewers" · "status"
        public string p;          // "twitch" · "kick" · "youtube"
        public string id, uid;    // id del mensaje y del usuario (para borrar)
        public string user, color;
        public string kind;       // eventos: sub, resub, gift, giftbomb, raid, bits, follow, superchat, sticker, member, giftmember, kicks, announce
        public string text, amount;
        public string[] roles;    // broadcaster, mod, vip, sub, founder, member, verified, og
        public string src, srci;  // chat compartido de Twitch: canal del que viene (nombre e imagen); null si es del tuyo
        public List<string[]> bd; // Twitch: imágenes de los emblemas { url, título } (si se pudieron pedir)
        public List<ChatPart> parts;
        public Dictionary<string, object> data;
    }
}
