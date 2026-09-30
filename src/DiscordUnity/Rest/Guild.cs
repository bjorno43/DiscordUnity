using DiscordUnity.Models;
using DiscordUnity.State;
using System.Linq;
using System.Threading.Tasks;
using System.Globalization;
using System.Net.Http;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        public static Task<RestResult<DiscordServer>> GetServer(string guildId, bool? withcounts = null)
            => SyncInherit(Get<GuildModel>($"/guilds/{guildId}", new { with_counts = withcounts }), r => new DiscordServer(r));
        public static Task<RestResult<DiscordServer>> GetServerPreview(string guildId)
            => SyncInherit(Get<GuildModel>($"/guilds/{guildId}/preview"), r => new DiscordServer(r));
        // TODO Edits
        public static Task<RestResult<DiscordServer>> ModifyServer(string guildId, object edits)
            => SyncInherit(Patch<GuildModel>($"/guilds/{guildId}", edits), r => new DiscordServer(r));
        public static Task<RestResult<bool>> DeleteServer(string guildId)
            => SyncInherit(Delete<object>($"/guilds/{guildId}"), r => true);

        public static Task<RestResult<DiscordChannel[]>> GetServerChannels(string guildId)
            => SyncInherit(Get<ChannelModel[]>($"/guilds/{guildId}/channels"), r => r.Select(x => new DiscordChannel(x)).ToArray());
        public static Task<RestResult<DiscordChannel>> CreateServerChannel(string guildId, string name, int? type, string topic, int? bitrate, int? userLimit, int? rateLimitPerUser, int? position, 
            object[] permissionOverwrites, string parentid, bool? nsfw)
            => SyncInherit(Post<ChannelModel>($"/guilds/{guildId}/channels", new { name, type, topic, bitrate, userLimit, rateLimitPerUser, position, permissionOverwrites, parent_id = parentid, nsfw }), r => new DiscordChannel(r));
        public static Task<RestResult<bool>> ModifyServerChannelPositions(string guildId, string channelId, int? position)
            => SyncInherit(Patch<object>($"/guilds/{guildId}/channels", new[] { new { id = channelId, position } }), r => true);

        public static Task<RestResult<DiscordServerMember>> GetServerMember(string guildId, string userId)
            => SyncInherit(Get<GuildMemberModel>($"/guilds/{guildId}/members/{userId}"), r => { if (r == null) return null; r.GuildId = guildId; return new DiscordServerMember(r); });
        public static Task<RestResult<DiscordServerMember[]>> GetServerMembers(string guildId, int? limit, string after)
            => SyncInherit(Get<GuildMemberModel[]>($"/guilds/{guildId}/members", new { limit, after }), r => r.Select(x => { x.GuildId = guildId; return new DiscordServerMember(x); }).ToArray());
        public static Task<RestResult<DiscordServerMember>> AddServerMember(string guildId, string userId, string accessToken, string nick, string[] roles, bool? mute, bool? deaf)
            => SyncInherit(Put<GuildMemberModel>($"/guilds/{guildId}/members/{userId}", new { accessToken, nick, roles, mute, deaf }), r => { if (r == null) return null; r.GuildId = guildId; return new DiscordServerMember(r); });
        public static Task<RestResult<DiscordServerMember>> ModifyServerMember(string guildId, string userId, string nick, string[] roles, bool? mute, bool? deaf, string channelId)
            => SyncInherit(Patch<GuildMemberModel>($"/guilds/{guildId}/members/{userId}", new { nick, roles, mute, deaf, channelId }), r => { if (r == null) return null; r.GuildId = guildId; return new DiscordServerMember(r); });
        public static Task<RestResult<DiscordServerMember>> ModifyServerMember(string guildId, string userId, object edits)
            => SyncInherit(Patch<GuildMemberModel>($"/guilds/{guildId}/members/{userId}", edits), r => { r.GuildId = guildId; return new DiscordServerMember(r); });
        public static Task<RestResult<bool>> ModifyUserNick(string guildId, string nick)
            => SyncInherit(Patch<object>($"/guilds/{guildId}/members/@me", new { nick }), r => true);
        public static Task<RestResult<bool>> RemoveServerMember(string guildId, string userId)
            => SyncInherit(Delete<object>($"/guilds/{guildId}/members/{userId}"), r => true);

        public static Task<RestResult<bool>> AddServerMemberRole(string guildId, string userId, string roleId)
            => SyncInherit(Put<object>($"/guilds/{guildId}/members/{userId}/roles/{roleId}", null), r => true);
        public static Task<RestResult<bool>> RemoveServerMemberRole(string guildId, string userId, string roleId)
            => SyncInherit(Delete<object>($"/guilds/{guildId}/members/{userId}/roles/{roleId}", null), r => true);

        public static Task<RestResult<object[]>> GetServerBans(string guildId)
            => SyncInherit(Get<GuildBanModel[]>($"/guilds/{guildId}/bans"), r => (object[])r);
        public static Task<RestResult<object>> GetServerBan(string guildId, string userId)
            => SyncInherit(Get<GuildBanModel>($"/guilds/{guildId}/bans/{userId}"), r => (object)r);
        public static Task<RestResult<bool>> CreateServerBan(string guildId, string userId, int? deleteMessageDays, string reason)
            => SyncInherit(Http<object>(HttpMethod.Put, $"/guilds/{guildId}/bans/{userId}", new { delete_message_seconds = deleteMessageDays * 86400 }, auditReason: reason), r => true);
        public static Task<RestResult<bool>> RemoveServerBan(string guildId, string userId)
            => SyncInherit(Delete<object>($"/guilds/{guildId}/bans/{userId}"), r => true);

        public static Task<RestResult<DiscordRole[]>> GetServerRoles(string guildId)
            => SyncInherit(Get<RoleModel[]>($"/guilds/{guildId}/roles"), r => r.Select(x => new DiscordRole(x)).ToArray());
        public static Task<RestResult<DiscordRole>> CreateServerRole(string guildId, string name, ulong? permissions, int? color, bool? hoist, bool? mentionable)
            => SyncInherit(Post<RoleModel>($"/guilds/{guildId}/roles", new { name, permissions = permissions?.ToString(CultureInfo.InvariantCulture), color, hoist, mentionable }), r => new DiscordRole(r));
        public static Task<RestResult<DiscordRole>> ModifyServerRole(string guildId, string roleId, string name, ulong? permissions, int? color, bool? hoist, bool? mentionable)
            => SyncInherit(Patch<RoleModel>($"/guilds/{guildId}/roles/{roleId}", new { name, permissions = permissions?.ToString(CultureInfo.InvariantCulture), color, hoist, mentionable }), r => new DiscordRole(r));
        public static Task<RestResult<DiscordRole[]>> ModifyServerRolePositions(string guildId, string roleId, int? position)
            => SyncInherit(Patch<RoleModel[]>($"/guilds/{guildId}/roles", new[] { new { id = roleId, position } }), r => r.Select(x => new DiscordRole(x)).ToArray());
        public static Task<RestResult<bool>> DeleteServerRole(string guildId, string roleId)
            => SyncInherit(Delete<object>($"/guilds/{guildId}/roles/{roleId}"), r => true);

        // TODO From https://discord.com/developers/docs/resources/guild#get-guild-prune-count
    }
}
