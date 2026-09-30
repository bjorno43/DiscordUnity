using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DiscordUnity;
using Newtonsoft.Json.Linq;
using System;
using System.Threading.Tasks;

[BepInPlugin("example.discordunity.valheim", "DiscordUnity Valheim Example", "2.0.0")]
public sealed class ValheimBot : BaseUnityPlugin
{
    private ConfigEntry<string> channel;
    private ConfigEntry<string> guild;
    private Task<bool> startup;
    private bool ownsConnection;

    private void Awake()
    {
        var enabled = Config.Bind("Discord", "Enabled", false, "Run a bot in this Valheim process.");
        var token = Config.Bind("Discord", "BotToken", "", "Bot token, or set DISCORD_BOT_TOKEN in the process environment. Keep private.");
        guild = Config.Bind("Discord", "GuildId", "", "Restrict the example to this Discord server.");
        channel = Config.Bind("Discord", "ChannelId", "", "Restrict the example to this Discord channel.");
        var textCommands = Config.Bind("Discord", "TextCommands", false, "Handle !ping. Requires MESSAGE_CONTENT in the developer portal.");
        if (!enabled.Value) return;
        var secret = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? token.Value;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(guild.Value) || string.IsNullOrWhiteSpace(channel.Value))
        {
            Logger.LogWarning("Configure a token, GuildId and ChannelId before enabling the example.");
            return;
        }
        if (DiscordAPI.IsActive)
        {
            Logger.LogWarning("DiscordUnity already has a bot in this process.");
            return;
        }
        DiscordAPI.Logger = new BepLogger(Logger);
        DiscordAPI.GatewayEventReceived += OnGatewayEvent;
        DiscordAPI.InteractionCreated += OnInteraction;
        var options = new DiscordBotOptions();
        if (textCommands.Value) options.Intents |= GatewayIntents.MessageContent;
        startup = DiscordAPI.StartWithBot(secret, options);
        ownsConnection = true;
    }

    private void Update()
    {
        if (!ownsConnection) return;
        DiscordAPI.Update();
        if (startup != null && startup.IsCompleted)
        {
            Logger.LogInfo(startup.Status == TaskStatus.RanToCompletion && startup.Result ? "Discord bot ready." : "Discord bot startup failed.");
            startup = null;
        }
    }

    private void OnGatewayEvent(string name, JToken data)
    {
        if (name != "MESSAGE_CREATE" || (string)data["guild_id"] != guild.Value || (string)data["channel_id"] != channel.Value) return;
        if ((bool?)data["author"]?["bot"] == true || (string)data["content"] != "!ping") return;
        _ = SendPong();
    }

    private async Task SendPong()
    {
        try
        {
            var result = await DiscordAPI.CreateMessage(channel.Value, new DiscordMessageOptions
            {
                Content = "Pong from Valheim!", AllowedMentions = new { parse = new string[0] }
            });
            if (!result) Logger.LogWarning("Discord reply failed: " + result.Exception.GetType().Name);
        }
        catch (Exception exception) { Logger.LogWarning("Discord reply failed: " + exception.GetType().Name); }
    }

    private void OnInteraction(DiscordInteraction interaction)
    {
        if (interaction.Type == 2 && interaction.GuildId == guild.Value && interaction.ChannelId == channel.Value && (string)interaction.Data?["name"] == "ping")
            _ = ReplyToInteraction(interaction);
    }

    private async Task ReplyToInteraction(DiscordInteraction interaction)
    {
        try
        {
            var result = await interaction.Respond("Pong from Valheim!", ephemeral: true);
            if (!result) Logger.LogWarning("Discord interaction reply failed.");
        }
        catch (Exception exception) { Logger.LogWarning("Discord interaction reply failed: " + exception.GetType().Name); }
    }

    private void OnDestroy()
    {
        DiscordAPI.GatewayEventReceived -= OnGatewayEvent;
        DiscordAPI.InteractionCreated -= OnInteraction;
        if (ownsConnection) { DiscordAPI.Stop(); DiscordAPI.Update(); }
    }

    private sealed class BepLogger : ILogger
    {
        private readonly ManualLogSource log;
        internal BepLogger(ManualLogSource log) => this.log = log;
        public void Log(string message) => log.LogInfo(message);
        public void LogWarning(string message) => log.LogWarning(message);
        public void LogError(string message, Exception exception = null) => log.LogError(message);
    }
}
