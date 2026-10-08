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

        private static readonly Dictionary<MsgType, Handler> handlers = new Dictionary<MsgType, Handler>();

        public static void On(MsgType type, Handler handler) => handlers[type] = handler;

        public static void Dispatch(CSteamID sender, byte[] data)
        {
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                var type = (MsgType)reader.ReadByte();
                if (handlers.TryGetValue(type, out Handler handler))
                    handler(sender, reader);
                else
                    ModLog.Warn($"No handler for message {type} from {SteamSession.NameOf(sender)}");
            }
        }
    }
}
