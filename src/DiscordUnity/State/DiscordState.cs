using DiscordUnity.State;
using System.Collections.Generic;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        public static int Version { get; private set; }
        public static DiscordUser User { get; private set; }
        public static Dictionary<string, DiscordServer> Servers { get; private set; }
        public static Dictionary<string, DiscordChannel> PrivateChannels { get; private set; }
        internal static Dictionary<string, DiscordUser> Users { get; private set; }

        internal static DiscordServer FindServer(string id)
            => id != null && Servers != null && Servers.TryGetValue(id, out var server) ? server : null;

        internal static DiscordChannel FindChannel(string guildId, string channelId)
        {
            if (channelId == null) return null;
            var server = FindServer(guildId);
            if (server != null && server.Channels.TryGetValue(channelId, out var channel)) return channel;
            if (PrivateChannels != null && PrivateChannels.TryGetValue(channelId, out var dm)) return dm;
            // REST messages omit guild_id, and a DM/thread may not be cached yet.
            if (Servers != null)
                foreach (var guild in Servers.Values)
                    if (guild.Channels.TryGetValue(channelId, out var candidate)) return candidate;
            return null;
        }

        internal static void InitializeState()
        {
            Version = -1;
            User = null;
            Servers = new Dictionary<string, DiscordServer>();
            PrivateChannels = new Dictionary<string, DiscordChannel>();
            Users = new Dictionary<string, DiscordUser>();
        }
    }
}
