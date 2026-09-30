using DiscordUnity.Models;
using DiscordUnity.State;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        public static Task<RestResult<DiscordUser>> GetCurrentUser()
            => GetUser(null);
        public static Task<RestResult<DiscordUser>> GetUser(string userId = null)
            => SyncInherit(Get<UserModel>($"/users/{userId ?? "@me"}"), r => new DiscordUser(r));
        public static Task<RestResult<DiscordUser>> ModifyUser(string username, object avatar)
            => SyncInherit(Patch<UserModel>($"/users/@me", new { username, avatar }), r => new DiscordUser(r));
        public static Task<RestResult<DiscordServer[]>> GeUserServers(string before, string after, int? limit)
            => SyncInherit(Get<GuildModel[]>($"/users/@me/guilds", new { before, after, limit }), r => r.Select(x => new DiscordServer(x)).ToArray());
        public static Task<RestResult<DiscordServer[]>> GetUserServers(string before = null, string after = null, int? limit = null)
            => GeUserServers(before, after, limit);
        public static Task<RestResult<bool>> LeaveServer(string serverId)
            => SyncInherit(Delete<object>($"/users/@me/guilds/{serverId}"), r => true);
        public static Task<RestResult<DiscordChannel[]>> GetUserDMs()
            => Task.FromResult(RestResult<DiscordChannel[]>.FromException(new System.NotSupportedException("Bots cannot list user DMs. Use PrivateChannels for observed DMs, or CreateDM.")));
        public static Task<RestResult<DiscordChannel>> CreateDM(string recipientId)
            => SyncInherit(Post<ChannelModel>($"/users/@me/channels", new { recipientId }), r => new DiscordChannel(r));
        public static Task<RestResult<DiscordChannel>> CreateGroupDM(string accessTokens, Dictionary<string, string> nicks)
            => Task.FromResult(RestResult<DiscordChannel>.FromException(new System.NotSupportedException("Group DM creation requires user OAuth2 access and is not available to this bot-only client.")));
        public static Task<RestResult<object[]>> GetUserConnections()
            => Task.FromResult(RestResult<object[]>.FromException(new System.NotSupportedException("User connections require OAuth2; a bot token cannot access them.")));
    }
}
