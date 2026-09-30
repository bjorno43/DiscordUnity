using System;

namespace DiscordUnity
{
    [Flags]
    public enum GatewayIntents : long
    {
        None = 0,
        Guilds = 1L << 0,
        GuildMembers = 1L << 1,
        GuildModeration = 1L << 2,
        GuildExpressions = 1L << 3,
        GuildIntegrations = 1L << 4,
        GuildWebhooks = 1L << 5,
        GuildInvites = 1L << 6,
        GuildVoiceStates = 1L << 7,
        GuildPresences = 1L << 8,
        GuildMessages = 1L << 9,
        GuildMessageReactions = 1L << 10,
        GuildMessageTyping = 1L << 11,
        DirectMessages = 1L << 12,
        DirectMessageReactions = 1L << 13,
        DirectMessageTyping = 1L << 14,
        MessageContent = 1L << 15,
        GuildScheduledEvents = 1L << 16,
        AutoModerationConfiguration = 1L << 20,
        AutoModerationExecution = 1L << 21,
        GuildMessagePolls = 1L << 24,
        DirectMessagePolls = 1L << 25
    }

    public sealed class DiscordBotOptions
    {
        // Privileged intents must be requested AND enabled in Discord's developer portal.
        public GatewayIntents Intents { get; set; } = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages;
        public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
        public int MaxGatewayMessageBytes { get; set; } = 16 * 1024 * 1024;

        internal DiscordBotOptions Copy() => (DiscordBotOptions)MemberwiseClone();
        internal void Validate()
        {
            if (StartupTimeout.TotalMilliseconds <= 0 || StartupTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(StartupTimeout));
            if (ReconnectDelay.TotalMilliseconds < 0 || ReconnectDelay.TotalMilliseconds > 30000)
                throw new ArgumentOutOfRangeException(nameof(ReconnectDelay));
            if (MaxGatewayMessageBytes < 8192) throw new ArgumentOutOfRangeException(nameof(MaxGatewayMessageBytes));
        }
    }
}
