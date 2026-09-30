using DiscordUnity.State;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DiscordUnity.API
{
    internal class DiscordInterfaces : IDiscordAPIEvents, IDiscordChannelEvents, IDiscordServerEvents, IDiscordInviteEvents, IDiscordMessageEvents, IDiscordStatusEvents, IDiscordVoiceEvents, IDiscordWebhookEvents
    {
        private readonly HashSet<IDiscordAPIEvents> api;
        private readonly HashSet<IDiscordChannelEvents> channel;
        private readonly HashSet<IDiscordServerEvents> server;
        private readonly HashSet<IDiscordInviteEvents> invite;
        private readonly HashSet<IDiscordMessageEvents> message;
        private readonly HashSet<IDiscordStatusEvents> status;
        private readonly HashSet<IDiscordVoiceEvents> voice;
        private readonly HashSet<IDiscordWebhookEvents> webhook;

        internal DiscordInterfaces()
        {
            api = new HashSet<IDiscordAPIEvents>();
            channel = new HashSet<IDiscordChannelEvents>();
            server = new HashSet<IDiscordServerEvents>();
            invite = new HashSet<IDiscordInviteEvents>();
            message = new HashSet<IDiscordMessageEvents>();
            status = new HashSet<IDiscordStatusEvents>();
            voice = new HashSet<IDiscordVoiceEvents>();
            webhook = new HashSet<IDiscordWebhookEvents>();
        }

        internal void AddEventHandler(IDiscordInterface e)
        {
            if (e is IDiscordAPIEvents a) api.Add(a);
            if (e is IDiscordChannelEvents c) channel.Add(c);
            if (e is IDiscordServerEvents s) server.Add(s);
            if (e is IDiscordInviteEvents i) invite.Add(i);
            if (e is IDiscordMessageEvents m) message.Add(m);
            if (e is IDiscordStatusEvents st) status.Add(st);
            if (e is IDiscordVoiceEvents v) voice.Add(v);
            if (e is IDiscordWebhookEvents w) webhook.Add(w);
        }

        internal void RemoveEventHandler(IDiscordInterface e)
        {
            if (e is IDiscordAPIEvents a) api.Remove(a);
            if (e is IDiscordChannelEvents c) channel.Remove(c);
            if (e is IDiscordServerEvents s) server.Remove(s);
            if (e is IDiscordInviteEvents i) invite.Remove(i);
            if (e is IDiscordMessageEvents m) message.Remove(m);
            if (e is IDiscordStatusEvents st) status.Remove(st);
            if (e is IDiscordVoiceEvents v) voice.Remove(v);
            if (e is IDiscordWebhookEvents w) webhook.Remove(w);
        }


        public void OnDiscordAPIClosed()
        {
            foreach (var e in api.ToArray())
                e.OnDiscordAPIClosed();
        }

        public void OnDiscordAPIOpen()
        {
            foreach (var e in api.ToArray())
                e.OnDiscordAPIOpen();
        }

        public void OnDiscordAPIResumed()
        {
            foreach (var e in api.ToArray())
                e.OnDiscordAPIResumed();
        }


        public void OnChannelCreated(DiscordChannel channel)
        {
            foreach (var e in this.channel.ToArray())
                e.OnChannelCreated(channel);
        }

        public void OnChannelUpdated(DiscordChannel channel)
        {
            foreach (var e in this.channel.ToArray())
                e.OnChannelUpdated(channel);
        }

        public void OnChannelDeleted(DiscordChannel channel)
        {
            foreach (var e in this.channel.ToArray())
                e.OnChannelDeleted(channel);
        }

        public void OnChannelPinsUpdated(DiscordChannel channel, DateTime? lastPinTimestamp)
        {
            foreach (var e in this.channel.ToArray())
                e.OnChannelPinsUpdated(channel, lastPinTimestamp);
        }


        public void OnServerJoined(DiscordServer server)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerJoined(server);
        }

        public void OnServerUpdated(DiscordServer server)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerUpdated(server);
        }

        public void OnServerLeft(DiscordServer server)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerLeft(server);
        }

        public void OnServerBan(DiscordServer server, DiscordUser user)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerBan(server, user);
        }

        public void OnServerUnban(DiscordServer server, DiscordUser user)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerUnban(server, user);
        }

        public void OnServerEmojisUpdated(DiscordServer server, DiscordEmoji[] emojis)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerEmojisUpdated(server, emojis);
        }

        public void OnServerMemberJoined(DiscordServer server, DiscordServerMember member)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerMemberJoined(server, member);
        }

        public void OnServerMemberUpdated(DiscordServer server, DiscordServerMember member)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerMemberUpdated(server, member);
        }

        public void OnServerMemberLeft(DiscordServer server, DiscordServerMember member)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerMemberLeft(server, member);
        }

        public void OnServerMembersChunk(DiscordServer server, DiscordServerMember[] members, string[] notFound, DiscordPresence[] presences)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerMembersChunk(server, members, notFound, presences);
        }

        public void OnServerRoleCreated(DiscordServer server, DiscordRole role)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerRoleCreated(server, role);
        }

        public void OnServerRoleUpdated(DiscordServer server, DiscordRole role)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerRoleUpdated(server, role);
        }

        public void OnServerRoleRemove(DiscordServer server, DiscordRole role)
        {
            foreach (var e in this.server.ToArray())
                e.OnServerRoleRemove(server, role);
        }


        public void InviteCreated(DiscordServer server, DiscordInvite invite)
        {
            foreach (var e in this.invite.ToArray())
                e.InviteCreated(server, invite);
        }

        public void InviteDeleted(DiscordServer server, DiscordInvite invite)
        {
            foreach (var e in this.invite.ToArray())
                e.InviteDeleted(server, invite);
        }


        public void OnMessageCreated(DiscordMessage message)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageCreated(message);
        }

        public void OnMessageUpdated(DiscordMessage message)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageUpdated(message);
        }

        public void OnMessageDeleted(DiscordMessage message)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageDeleted(message);
        }

        public void OnMessageDeletedBulk(string[] messageIds)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageDeletedBulk(messageIds);
        }

        public void OnMessageReactionAdded(DiscordMessage message, DiscordReaction reaction)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageReactionAdded(message, reaction);
        }

        public void OnMessageReactionRemoved(DiscordMessage message, DiscordReaction reaction)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageReactionRemoved(message, reaction);
        }

        public void OnMessageAllReactionsRemoved(DiscordMessage message, DiscordReaction reaction)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageAllReactionsRemoved(message, reaction);
        }

        public void OnMessageEmojiReactionRemoved(DiscordMessage message, DiscordReaction reaction)
        {
            foreach (var e in this.message.ToArray())
                e.OnMessageEmojiReactionRemoved(message, reaction);
        }


        public void OnPresenceUpdated(DiscordPresence presence)
        {
            foreach (var e in status.ToArray())
                e.OnPresenceUpdated(presence);
        }

        public void OnTypingStarted(DiscordChannel channel, DiscordUser user, DateTime timestamp)
        {
            foreach (var e in status.ToArray())
                e.OnTypingStarted(channel, user, timestamp);
        }

        public void OnServerTypingStarted(DiscordChannel channel, DiscordServerMember member, DateTime timestamp)
        {
            foreach (var e in status.ToArray())
                e.OnServerTypingStarted(channel, member, timestamp);
        }

        public void OnUserUpdated(DiscordUser user)
        {
            foreach (var e in status.ToArray())
                e.OnUserUpdated(user);
        }


        public void OnVoiceStateUpdated(DiscordVoiceState voiceState)
        {
            foreach (var e in voice.ToArray())
                e.OnVoiceStateUpdated(voiceState);
        }

        public void OnVoiceServerUpdated(DiscordServer server, string token, string endpoint)
        {
            foreach (var e in voice.ToArray())
                e.OnVoiceServerUpdated(server, token, endpoint);
        }


        public void OnWebhooksUpdated(DiscordChannel channel)
        {
            foreach (var e in webhook.ToArray())
                e.OnWebhooksUpdated(channel);
        }
    }
}
