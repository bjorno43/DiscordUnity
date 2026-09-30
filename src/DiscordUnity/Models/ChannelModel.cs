using System;

namespace DiscordUnity.Models
{
    internal class ChannelModel
    {
        public string Id { get; set; }
        public ChannelType Type { get; set; }
        public string GuildId { get; set; }
        public int? Position { get; set; }
        public OverwriteModel[] PermissionOverwrites { get; set; }
        public string Name { get; set; }
        public string Topic { get; set; }
        public bool? Nsfw { get; set; }
        public string LastMessageId { get; set; }
        public int? Bitrate { get; set; }
        public int? UserLimit { get; set; }
        public int? RateLimitPerUser { get; set; }
        public UserModel[] Recipients { get; set; }
        public string Icon { get; set; }
        public string OwnerId { get; set; }
        public string ApplicationId { get; set; }
        public string ParentId { get; set; }
        public DateTime? LastPinTimestamp { get; set; }
    }

    public enum ChannelType
    {
        GUILD_TEXT = 0,     // A text channel within a server
        DM = 1,             // A direct message between users
        GUILD_VOICE = 2,    // A voice channel within a server
        GROUP_DM = 3,       // A direct message between multiple users
        GUILD_CATEGORY = 4, // An organizational category that contains up to 50 channels
        GUILD_NEWS = 5,     // A channel that users can follow and crosspost into their own server
        GUILD_STORE = 6,    // Legacy type, no longer created
        GUILD_NEWS_THREAD = 10,
        GUILD_PUBLIC_THREAD = 11,
        GUILD_PRIVATE_THREAD = 12,
        GUILD_STAGE_VOICE = 13,
        GUILD_DIRECTORY = 14,
        GUILD_FORUM = 15,
        GUILD_MEDIA = 16
    }

    internal class ChannelPinsModel
    {
        public string GuildId { get; set; }
        public string ChannelId { get; set; }
        public DateTime? LastPinTimestamp { get; set; }
    }
}
