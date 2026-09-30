using DiscordUnity.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordUnity.State
{
    public class DiscordChannel
    {
        public string Id { get; internal set; }
        public ChannelType Type { get; internal set; }
        public DiscordServer Server => DiscordAPI.FindServer(GuildId);
        public int? Position { get; internal set; }
        public DiscordOverwrite[] PermissionOverwrites { get; internal set; }
        public string Name { get; internal set; }
        public string Topic { get; internal set; }
        public bool? Nsfw { get; internal set; }
        public string LastMessageId { get; internal set; }
        public int? Bitrate { get; internal set; }
        public int? UserLimit { get; internal set; }
        public int? RateLimitPerUser { get; internal set; }
        public Dictionary<string, DiscordUser> Recipients { get; internal set; }
        public string Icon { get; internal set; }
        public DiscordUser Owner { get; internal set; }
        public string ApplicationId { get; internal set; }
        public DiscordChannel Parent { get; internal set; }
        public DateTime? LastPinTimestamp { get; internal set; }

        public string GuildId { get; }

        internal DiscordChannel(ChannelModel model)
        {
            Id = model.Id;
            Type = model.Type;
            GuildId = model.GuildId;
            Position = model.Position;
            Name = model.Name;
            PermissionOverwrites = model.PermissionOverwrites?.Select(x => new DiscordOverwrite(x)).ToArray();
            Topic = model.Topic;
            Nsfw = model.Nsfw;
            LastMessageId = model.LastMessageId;
            Bitrate = model.Bitrate;
            UserLimit = model.UserLimit;
            RateLimitPerUser = model.RateLimitPerUser;
            Recipients = model.Recipients?.ToDictionary(x => x.Id, x => new DiscordUser(x)) ?? new Dictionary<string, DiscordUser>();
            Icon = model.Icon;
            if (model.OwnerId != null && Recipients.TryGetValue(model.OwnerId, out var owner)) Owner = owner;
            ApplicationId = model.ApplicationId;
            LastPinTimestamp = model.LastPinTimestamp;
        }

        public Task<RestResult<DiscordMessage>> CreateMessage(string content, string nonce, bool? tts, object file, object embed, string payload_json, object allowed_mentions)
            => DiscordAPI.CreateMessage(Id, content, nonce, tts, file, embed, payload_json, allowed_mentions);

        public Task<RestResult<DiscordMessage>> CreateMessage(string content)
            => DiscordAPI.CreateMessage(Id, content);
    }

    public class DiscordOverwrite
    {
        public string Id { get; internal set; }
        public int Type { get; internal set; }
        public ulong Allow { get; internal set; }
        public ulong Deny { get; internal set; }

        internal DiscordOverwrite(OverwriteModel model)
        {
            Id = model.Id;
            Type = model.Type;
            Allow = model.Allow;
            Deny = model.Deny;
        }
    }
}
