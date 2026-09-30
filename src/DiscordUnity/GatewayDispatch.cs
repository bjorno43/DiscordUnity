using DiscordUnity.Models;
using DiscordUnity.State;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        /// <summary>All dispatches, including events without a typed wrapper, on Update's thread.</summary>
        public static event Action<string, JToken> GatewayEventReceived;

        internal static void ProcessDispatch(GatewayConnection current, PayloadModel payload)
        {
            var data = payload.Data as JObject;
            if (data == null) return;
            var guildId = (string)data["guild_id"];
            switch (payload.Event)
            {
                case "INTERACTION_CREATE":
                    InteractionCreated?.Invoke(new DiscordInteraction(data));
                    break;
                case "READY":
                    var ready = payload.As<ReadyModel>().Data;
                    InitializeState();
                    Version = ready.Version;
                    User = new DiscordUser(ready.User);
                    Users[User.Id] = User;
                    foreach (var privateChannel in ready.PrivateChannels ?? new ChannelModel[0]) PrivateChannels[privateChannel.Id] = new DiscordChannel(privateChannel);
                    foreach (var guild in ready.Guilds ?? new GuildModel[0]) Servers[guild.Id] = new DiscordServer(guild);
                    current.Ready.TrySetResult(true);
                    interfaces.OnDiscordAPIOpen();
                    break;
                case "RESUMED":
                    interfaces.OnDiscordAPIResumed();
                    break;
                case "GUILD_CREATE":
                    var createdGuild = payload.As<GuildModel>().Data;
                    Servers[createdGuild.Id] = new DiscordServer(createdGuild);
                    interfaces.OnServerJoined(Servers[createdGuild.Id]);
                    break;
                case "GUILD_UPDATE":
                    var id = (string)data["id"];
                    Servers[id] = FindServer(id)?.WithUpdate(data) ?? new DiscordServer(payload.As<GuildModel>().Data);
                    interfaces.OnServerUpdated(Servers[id]);
                    break;
                case "GUILD_DELETE":
                    var deleted = FindServer((string)data["id"]);
                    if (deleted == null) break;
                    if ((bool?)data["unavailable"] == true) deleted.Unavailable = true;
                    else { Servers.Remove(deleted.Id); interfaces.OnServerLeft(deleted); }
                    break;
                case "CHANNEL_CREATE":
                case "CHANNEL_UPDATE":
                case "THREAD_CREATE":
                case "THREAD_UPDATE":
                    var channelModel = payload.As<ChannelModel>().Data;
                    var channel = CacheChannel(channelModel);
                    if (payload.Event.EndsWith("CREATE", StringComparison.Ordinal)) interfaces.OnChannelCreated(channel);
                    else interfaces.OnChannelUpdated(channel);
                    break;
                case "CHANNEL_DELETE":
                case "THREAD_DELETE":
                    var channelId = (string)data["id"];
                    var removedChannel = FindChannel(guildId, channelId) ?? new DiscordChannel(payload.As<ChannelModel>().Data);
                    if (guildId == null) PrivateChannels.Remove(channelId);
                    else FindServer(guildId)?.Channels.Remove(channelId);
                    interfaces.OnChannelDeleted(removedChannel);
                    break;
                case "THREAD_LIST_SYNC":
                    if (data["threads"] is JArray threads)
                        foreach (var thread in threads)
                        {
                            var model = thread.ToObject<ChannelModel>(JsonSerializer);
                            model.GuildId = guildId;
                            CacheChannel(model);
                        }
                    break;
                case "CHANNEL_PINS_UPDATE":
                    var pins = payload.As<ChannelPinsModel>().Data;
                    var pinnedChannel = FindChannel(pins.GuildId, pins.ChannelId);
                    if (pinnedChannel != null)
                    {
                        pinnedChannel.LastPinTimestamp = pins.LastPinTimestamp;
                        interfaces.OnChannelPinsUpdated(pinnedChannel, pins.LastPinTimestamp);
                    }
                    break;
                case "GUILD_BAN_ADD":
                case "GUILD_BAN_REMOVE":
                    var ban = payload.As<GuildBanModel>().Data;
                    var banServer = FindServer(ban.GuildId);
                    if (banServer == null) break;
                    var bannedUser = new DiscordUser(ban.User);
                    if (payload.Event == "GUILD_BAN_ADD") { banServer.Bans[bannedUser.Id] = bannedUser; interfaces.OnServerBan(banServer, bannedUser); }
                    else { banServer.Bans.Remove(bannedUser.Id); interfaces.OnServerUnban(banServer, bannedUser); }
                    break;
                case "GUILD_EMOJIS_UPDATE":
                    var emojis = payload.As<GuildEmojisModel>().Data;
                    var emojiServer = FindServer(guildId);
                    if (emojiServer == null) break;
                    emojiServer.Emojis = (emojis.Emojis ?? new EmojiModel[0]).ToDictionary(x => x.Id, x => new DiscordEmoji(x));
                    interfaces.OnServerEmojisUpdated(emojiServer, emojiServer.Emojis.Values.ToArray());
                    break;
                case "GUILD_MEMBER_ADD":
                case "GUILD_MEMBER_UPDATE":
                case "GUILD_MEMBER_REMOVE":
                    var memberModel = payload.As<GuildMemberModel>().Data;
                    var memberServer = FindServer(guildId);
                    if (memberServer == null || memberModel.User == null) break;
                    var member = new DiscordServerMember(memberModel);
                    Users[member.User.Id] = member.User;
                    if (payload.Event == "GUILD_MEMBER_REMOVE")
                    {
                        if (memberServer.Members.TryGetValue(member.User.Id, out var old)) member = old;
                        memberServer.Members.Remove(member.User.Id);
                        interfaces.OnServerMemberLeft(memberServer, member);
                    }
                    else
                    {
                        memberServer.Members[member.User.Id] = member;
                        if (payload.Event == "GUILD_MEMBER_ADD") interfaces.OnServerMemberJoined(memberServer, member);
                        else interfaces.OnServerMemberUpdated(memberServer, member);
                    }
                    break;
                case "GUILD_MEMBERS_CHUNK":
                    var chunk = payload.As<GuildMembersChunkModel>().Data;
                    var chunkServer = FindServer(guildId);
                    if (chunkServer == null) break;
                    var members = (chunk.Members ?? new GuildMemberModel[0]).Select(x => { x.GuildId = guildId; return new DiscordServerMember(x); }).ToArray();
                    foreach (var entry in members) if (entry.User != null) chunkServer.Members[entry.User.Id] = entry;
                    var presences = (chunk.Presences ?? new PresenceModel[0]).Select(x => { x.GuildId = guildId; return new DiscordPresence(x); }).ToArray();
                    foreach (var entry in presences) chunkServer.Presences[entry.User.Id] = entry;
                    interfaces.OnServerMembersChunk(chunkServer, members, chunk.NotFound ?? new string[0], presences);
                    break;
                case "GUILD_ROLE_CREATE":
                case "GUILD_ROLE_UPDATE":
                    var roleModel = payload.As<GuildRoleModel>().Data;
                    var roleServer = FindServer(guildId);
                    if (roleServer == null) break;
                    var role = new DiscordRole(roleModel.Role);
                    roleServer.Roles[role.Id] = role;
                    if (payload.Event == "GUILD_ROLE_CREATE") interfaces.OnServerRoleCreated(roleServer, role);
                    else interfaces.OnServerRoleUpdated(roleServer, role);
                    break;
                case "GUILD_ROLE_DELETE":
                    var removedRoleServer = FindServer(guildId);
                    var roleId = (string)data["role_id"];
                    if (removedRoleServer == null) break;
                    if (!removedRoleServer.Roles.TryGetValue(roleId, out var removedRole)) removedRole = new DiscordRole(new RoleModel { Id = roleId });
                    removedRoleServer.Roles.Remove(roleId);
                    interfaces.OnServerRoleRemove(removedRoleServer, removedRole);
                    break;
                case "INVITE_CREATE":
                case "INVITE_DELETE":
                    var invite = new DiscordInvite(payload.As<InviteModel>().Data);
                    var inviteServer = FindServer(guildId);
                    if (inviteServer == null) break;
                    if (payload.Event == "INVITE_CREATE") { inviteServer.Invites[invite.Code] = invite; interfaces.InviteCreated(inviteServer, invite); }
                    else { inviteServer.Invites.Remove(invite.Code); interfaces.InviteDeleted(inviteServer, invite); }
                    break;
                case "MESSAGE_CREATE":
                case "MESSAGE_UPDATE":
                case "MESSAGE_DELETE":
                    var messageModel = payload.As<MessageModel>().Data;
                    if (messageModel.Author != null) Users[messageModel.Author.Id] = new DiscordUser(messageModel.Author);
                    // DMs are not included in bot READY; threads can arrive before cache synchronization.
                    if (FindChannel(messageModel.GuildId, messageModel.ChannelId) == null && messageModel.ChannelId != null)
                        CacheChannel(new ChannelModel { Id = messageModel.ChannelId, GuildId = messageModel.GuildId, Type = messageModel.GuildId == null ? ChannelType.DM : ChannelType.GUILD_TEXT });
                    var message = new DiscordMessage(messageModel);
                    if (payload.Event == "MESSAGE_CREATE") interfaces.OnMessageCreated(message);
                    else if (payload.Event == "MESSAGE_UPDATE") interfaces.OnMessageUpdated(message);
                    else interfaces.OnMessageDeleted(message);
                    break;
                case "MESSAGE_DELETE_BULK":
                    interfaces.OnMessageDeletedBulk(payload.As<MessageBulkModel>().Data.Ids);
                    break;
                case "MESSAGE_REACTION_ADD":
                case "MESSAGE_REACTION_REMOVE":
                case "MESSAGE_REACTION_REMOVE_ALL":
                case "MESSAGE_REACTION_REMOVE_EMOJI":
                    var reactionModel = payload.As<MessageReactionModel>().Data;
                    var reactedMessage = new DiscordMessage(new MessageModel { Id = reactionModel.MessageId, ChannelId = reactionModel.ChannelId, GuildId = guildId });
                    var reaction = new DiscordReaction(new ReactionModel { Emoji = reactionModel.Emoji }) { UserId = reactionModel.UserId };
                    if (payload.Event == "MESSAGE_REACTION_ADD") interfaces.OnMessageReactionAdded(reactedMessage, reaction);
                    else if (payload.Event == "MESSAGE_REACTION_REMOVE") interfaces.OnMessageReactionRemoved(reactedMessage, reaction);
                    else if (payload.Event == "MESSAGE_REACTION_REMOVE_ALL") interfaces.OnMessageAllReactionsRemoved(reactedMessage, reaction);
                    else interfaces.OnMessageEmojiReactionRemoved(reactedMessage, reaction);
                    break;
                case "PRESENCE_UPDATE":
                    var presence = new DiscordPresence(payload.As<PresenceModel>().Data);
                    var presenceServer = FindServer(guildId);
                    if (presenceServer != null) presenceServer.Presences[presence.User.Id] = presence;
                    interfaces.OnPresenceUpdated(presence);
                    break;
                case "TYPING_START":
                    var typing = payload.As<TypingModel>().Data;
                    var typingChannel = FindChannel(guildId, typing.ChannelId);
                    var timestamp = DateTimeOffset.FromUnixTimeSeconds(typing.Timestamp).UtcDateTime;
                    if (typingChannel == null) break;
                    if (typing.Member != null)
                    {
                        typing.Member.GuildId = guildId;
                        interfaces.OnServerTypingStarted(typingChannel, new DiscordServerMember(typing.Member), timestamp);
                    }
                    else interfaces.OnTypingStarted(typingChannel, Users.TryGetValue(typing.UserId, out var user) ? user : new DiscordUser(new UserModel { Id = typing.UserId }), timestamp);
                    break;
                case "USER_UPDATE":
                    var updatedUser = new DiscordUser(payload.As<UserModel>().Data);
                    Users[updatedUser.Id] = updatedUser;
                    if (User?.Id == updatedUser.Id) User = updatedUser;
                    interfaces.OnUserUpdated(updatedUser);
                    break;
                case "VOICE_STATE_UPDATE":
                    var voiceModel = payload.As<VoiceStateModel>().Data;
                    var voice = new DiscordVoiceState(voiceModel);
                    var voiceServer = FindServer(guildId);
                    if (voiceServer != null)
                    {
                        if (voiceModel.ChannelId == null) voiceServer.VoiceStates.Remove(voiceModel.UserId);
                        else voiceServer.VoiceStates[voiceModel.UserId] = voice;
                    }
                    interfaces.OnVoiceStateUpdated(voice);
                    break;
                case "VOICE_SERVER_UPDATE":
                    var voiceEndpoint = payload.As<VoiceServerModel>().Data;
                    interfaces.OnVoiceServerUpdated(FindServer(guildId), voiceEndpoint.Token, voiceEndpoint.Endpoint);
                    break;
                case "WEBHOOKS_UPDATE":
                    interfaces.OnWebhooksUpdated(FindChannel(guildId, (string)data["channel_id"]));
                    break;
            }
            GatewayEventReceived?.Invoke(payload.Event, data);
        }

        private static DiscordChannel CacheChannel(ChannelModel model)
        {
            var channel = new DiscordChannel(model);
            if (model.GuildId == null) PrivateChannels[model.Id] = channel;
            else
            {
                var server = FindServer(model.GuildId);
                if (server == null) Servers[model.GuildId] = server = new DiscordServer(new GuildModel { Id = model.GuildId });
                server.Channels[model.Id] = channel;
                channel.Parent = FindChannel(model.GuildId, model.ParentId);
            }
            return channel;
        }
    }
}
