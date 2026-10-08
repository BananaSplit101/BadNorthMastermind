using System;
using System.Collections.Generic;
using System.IO;
using Steamworks;

namespace BadNorthMP.Net
{
    /// <summary>Dispatches incoming packets to the handler registered for their message type.</summary>
    internal static class Router
    {
        public delegate void Handler(CSteamID sender, BinaryReader reader);

        /// <summary>Battle stream messages (SquadSpawn and up) are passed on whole, for the puppet world.</summary>
        public static Action<CSteamID, byte[]> BattleMessage = delegate { };

        private static readonly Dictionary<MsgType, Handler> handlers = new Dictionary<MsgType, Handler>();

        public static void On(MsgType type, Handler handler) => handlers[type] = handler;

        public static bool IsBattleMessage(MsgType type) => type >= MsgType.SquadSpawn;

        public static void Dispatch(CSteamID sender, byte[] data)
        {
            if (data.Length == 0)
                return;
            var type = (MsgType)data[0];
            if (IsBattleMessage(type))
            {
                BattleMessage(sender, data);
                return;
            }
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadByte();
                if (handlers.TryGetValue(type, out Handler handler))
                    handler(sender, reader);
                else
                    ModLog.Warn($"No handler for message {type} from {SteamSession.NameOf(sender)}");
            }
        }
    }
}
