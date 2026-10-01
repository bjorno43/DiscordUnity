using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DiscordUnity;
using DiscordUnity.Models;
using DiscordUnity.State;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace ValheimDiscordChat
{
    [BepInPlugin(Guid, "Valheim Discord Chat", "0.2.2")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "icecub.ValheimDiscordChat";
        internal static Plugin Instance { get; private set; }
        private const int QueueLimit = 100;
        private readonly Queue<string> outgoing = new Queue<string>();
        private readonly Queue<string> incoming = new Queue<string>();
        private readonly Queue<string> adminOutgoing = new Queue<string>();
        private readonly ChatCoalescer coalescer = new ChatCoalescer();
        private readonly RecentIds gameIds = new RecentIds();
        private readonly RecentIds discordIds = new RecentIds();
        private ConfigEntry<bool> modEnabled, toDiscord, toGame, soloChatRelay;
        private ConfigEntry<string> token, channel, adminChannel, commandsChannel, allowedRoleIds;
        private ConfigEntry<int> maxLength;
        private Harmony harmony;
        private ZNet network;
        private bool attempted, ownsConnection, ready, clientWarning;
        private string channelId, guildId, adminChannelId, commandsChannelId, applicationId;
        private int textLimit;
        private float nextQueueWarning;
        private Task<bool> startup;
        private Task<RestResult<DiscordChannel>> channelLookup;
        private Task<RestResult<DiscordMessage>> send;
        private Task<RestResult<DiscordMessage>> adminSend;
        private Task<RestResult<DiscordChannel>> adminLookup, commandsLookup;
        private bool adminReady, commandsReady;
        private ServerStatistics statistics;
        private float nextStatisticsTick;
        private DiscordUnity.ILogger previousLogger;
        private BotLogger botLogger;

        internal bool CanRelay => ready && ownsConnection && DiscordAPI.IsActive && network &&
            network == ZNet.instance && network.IsServer() && network.IsDedicated();
        internal bool CanObserveGame => modEnabled != null && modEnabled.Value && network &&
            network == ZNet.instance && network.IsServer() && network.IsDedicated();

        private void Awake()
        {
            Instance = this;
            modEnabled = Config.Bind("General", "Enabled", true, "Enable the bridge on a dedicated server. Restart after changing settings.");
            token = Config.Bind("Discord", "BotToken", "", "Private Discord bot token. DISCORD_BOT_TOKEN overrides this value if set.");
            channel = Config.Bind("Discord", "ChannelId", "", "Discord text channel ID. Enable Developer Mode and use Copy Channel ID.");
            adminChannel = Config.Bind("Discord", "AdminChannelId", "", "Optional separate channel for all game chat, including normal chat and whispers. One-way only.");
            commandsChannel = Config.Bind("Discord", "CommandsChannelId", "", "Optional separate channel where slash commands may be used. Must be in the same Discord server.");
            allowedRoleIds = Config.Bind("Commands", "AllowedRoleIds", "", "Comma-separated Discord role IDs allowed to kick, ban, alert and setatspawn. Empty denies all moderation actions.");
            toDiscord = Config.Bind("Chat", "GameToDiscord", true, "Forward only shouts (/s) to Discord. Normal chat, whispers and pings are excluded.");
            toGame = Config.Bind("Chat", "DiscordToGame", true, "Forward human text messages from the configured channel to all connected players.");
            soloChatRelay = Config.Bind("Chat", "SoloChatRelay", true, "Experimental: add DiscordBot to the solo client's roster so vanilla chat reaches the server. Client player count and one-per-player drops increase by one; server statistics stay unchanged. Restart after changing settings.");
            maxLength = Config.Bind("Chat", "MaxMessageLength", 500, new ConfigDescription("Maximum relayed message length, excluding name/prefix.", new AcceptableValueRange<int>(32, 1500)));
            harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo("Valheim Discord Chat loaded. Waiting for a dedicated server.");
        }

        private void Update()
        {
            var current = ZNet.instance;
            if (current != network)
            {
                statistics?.Save(); statistics = null;
                StopBridge();
                network = current;
                attempted = false;
            }
            if (!network) return;
            if (!network.IsServer() || !network.IsDedicated())
            {
                if (!clientWarning) { clientWarning = true; Logger.LogWarning("This plugin runs only on dedicated servers. The bridge is inactive here."); }
                return;
            }
            if (!modEnabled.Value) { if (ownsConnection) StopBridge(); return; }
            if (statistics == null && network.GetWorld() != null)
            {
                statistics = new ServerStatistics(message => Logger.LogWarning(message));
                statistics.Open(Path.Combine(Paths.ConfigPath, "ValheimDiscordChat", network.GetWorldUID() + ".json"));
            }
            if (statistics != null && Time.realtimeSinceStartup >= nextStatisticsTick)
            {
                nextStatisticsTick = Time.realtimeSinceStartup + 1;
                statistics.Tick(network);
            }
            if (!attempted) StartBridge();
            if (!ownsConnection) return;
            DiscordAPI.Update();
            CheckStartup();
            if (!ownsConnection) return;
            if (!DiscordAPI.IsActive)
            {
                Logger.LogWarning("Discord disconnected permanently. Check the preceding errors and restart after correcting the configuration.");
                StopBridge();
                return;
            }
            if (!CanRelay) return;
            UpdateCommands();
            if (send != null && send.IsCompleted)
            {
                ReportResult(send, "Discord message");
                send = null;
            }
            if (send == null && outgoing.Count != 0)
                send = DiscordAPI.CreateMessage(channelId, new DiscordMessageOptions
                {
                    Content = outgoing.Dequeue(), AllowedMentions = new { parse = new string[0] }
                });
            if (adminSend != null && adminSend.IsCompleted) { ReportResult(adminSend, "Admin chat message"); adminSend = null; }
            if (adminReady && adminSend == null && adminOutgoing.Count != 0)
                adminSend = DiscordAPI.CreateMessage(adminChannelId, new DiscordMessageOptions
                { Content = adminOutgoing.Dequeue(), AllowedMentions = new { parse = new string[0] } });
            for (int i = 0; i < 8 && incoming.Count != 0; i++) Broadcast(incoming.Dequeue());
        }

        private void StartBridge()
        {
            attempted = true;
            string secret = (Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? token.Value).Trim();
            channelId = channel.Value.Trim();
            adminChannelId = adminChannel.Value.Trim(); commandsChannelId = commandsChannel.Value.Trim();
            try { roles = CommandPolicy.ParseRoles(allowedRoleIds.Value); }
            catch (FormatException) { roles = new HashSet<string>(); Logger.LogError("AllowedRoleIds contains an invalid ID. All moderation commands are denied."); }
            if (!OptionalChannel(adminChannelId, "AdminChannelId") || !OptionalChannel(commandsChannelId, "CommandsChannelId")) return;
            if ((adminChannelId.Length != 0 && adminChannelId == channelId) ||
                (commandsChannelId.Length != 0 && (commandsChannelId == channelId || commandsChannelId == adminChannelId)))
            { Logger.LogError("Public chat, admin chat and commands must use separate channel IDs."); return; }
            textLimit = Math.Max(32, Math.Min(1500, maxLength.Value));
            if (secret.Length == 0 || !ChatPolicy.IsSnowflake(channelId))
            {
                Logger.LogWarning("Set Discord.BotToken and Discord.ChannelId in " + Config.ConfigFilePath + ", then restart the server.");
                return;
            }
            if (DiscordAPI.IsActive)
            {
                Logger.LogError("Another plugin already owns DiscordUnity's connection. Run one DiscordUnity bot per server process.");
                return;
            }
            previousLogger = DiscordAPI.Logger;
            botLogger = new BotLogger(Logger, secret);
            DiscordAPI.Logger = botLogger;
            DiscordAPI.GatewayEventReceived += OnDiscordEvent;
            DiscordAPI.InteractionCreated += OnInteraction;
            ownsConnection = true;
            startup = DiscordAPI.StartWithBot(secret, new DiscordBotOptions
            {
                Intents = GatewayIntents.Guilds | GatewayIntents.GuildMessages |
                    (toGame.Value ? GatewayIntents.MessageContent : GatewayIntents.None)
            });
            Logger.LogInfo("Connecting the Discord chat bridge.");
        }

        private void CheckStartup()
        {
            if (startup != null && startup.IsCompleted)
            {
                var completed = startup;
                startup = null;
                if (completed.Status != TaskStatus.RanToCompletion || !completed.Result)
                {
                    if (completed.IsFaulted) { var ignored = completed.Exception; }
                    Logger.LogError("Discord startup failed. Verify the token and Message Content Intent. Restart after correcting the configuration.");
                    StopBridge();
                    return;
                }
                channelLookup = DiscordAPI.GetChannel(channelId);
                if (adminChannelId.Length != 0) adminLookup = DiscordAPI.GetChannel(adminChannelId);
                if (commandsChannelId.Length != 0) commandsLookup = DiscordAPI.GetChannel(commandsChannelId);
            }
            if (channelLookup != null && channelLookup.IsCompleted)
            {
                var completed = channelLookup;
                channelLookup = null;
                if (!ReportResult(completed, "Discord channel lookup")) { StopBridge(); return; }
                var data = completed.Result.Data;
                if (!ChatPolicy.IsSnowflake(data.GuildId) || (data.Type != ChannelType.GUILD_TEXT && data.Type != ChannelType.GUILD_NEWS))
                {
                    Logger.LogError("ChannelId must identify a server text or announcement channel. Threads, forums, voice channels and DMs are not supported.");
                    StopBridge();
                    return;
                }
                guildId = data.GuildId;
                ready = true;
                Logger.LogInfo("Discord chat bridge ready. Valheim shouts and the configured Discord channel are linked.");
            }
            if (!ready) return;
            CheckExtraChannel(ref adminLookup, ref adminReady, "Admin chat");
            CheckExtraChannel(ref commandsLookup, ref commandsReady, "Commands");
            RegisterCommands();
        }

        private bool OptionalChannel(string id, string setting)
        {
            if (id.Length == 0 || ChatPolicy.IsSnowflake(id)) return true;
            Logger.LogError(setting + " must be empty or a numeric Discord channel ID."); return false;
        }
        private void CheckExtraChannel(ref Task<RestResult<DiscordChannel>> lookup, ref bool available, string label)
        {
            if (lookup == null || !lookup.IsCompleted) return;
            var result = lookup; lookup = null;
            if (!ReportResult(result, label + " channel lookup")) return;
            var data = result.Result.Data;
            available = data.GuildId == guildId && (data.Type == ChannelType.GUILD_TEXT || data.Type == ChannelType.GUILD_NEWS);
            if (available) Logger.LogInfo(label + " channel validated.");
            else Logger.LogError(label + " channel must be a text or announcement channel in the public channel's Discord server. This feature is disabled.");
        }

        internal void Capture(ZRpc source, ZRoutedRpc.RoutedRPCData packet)
        {
            if (!CanObserveGame) return;
            try
            {
                ZNetPeer sender = null;
                foreach (var peer in network.GetPeers())
                    if (ReferenceEquals(peer.m_rpc, source) && peer.IsReady() && source.IsConnected()) { sender = peer; break; }
                if (sender == null) return;
                if (NativeChatReader.IsDeath(packet, sender.m_uid, sender.m_characterID)) statistics?.Death(network, sender);
                if (!CanRelay || (!toDiscord.Value && !adminReady)) return;
                int type; string text;
                if (!NativeChatReader.TryRead(packet, sender.m_uid, sender.m_characterID, out type, out text)) return;
                if (!gameIds.Accept(sender.m_uid + ":" + packet.m_msgID)) return;
                if (!coalescer.Accept(sender.m_uid, type, text, packet.m_targetPeerID, Time.realtimeSinceStartup)) return;
                var line = ChatPolicy.ToDiscord(sender.m_playerName, text, textLimit);
                if (line == null) return;
                if (toDiscord.Value && type == (int)Talker.Type.Shout) Enqueue(outgoing, line);
                if (adminReady) Enqueue(adminOutgoing, ChatPolicy.ToDiscord(sender.m_playerName, text, textLimit, "[Valheim " + ((Talker.Type)type) + "]"));
            }
            catch (Exception exception) { Logger.LogWarning("A chat packet could not be relayed (" + exception.GetType().Name + "). Vanilla delivery continues."); }
        }

        private void OnDiscordEvent(string name, JToken data)
        {
            if (name == "READY") { applicationId = (string)data["application"]?["id"]; return; }
            if (!CanRelay || !toGame.Value || name != "MESSAGE_CREATE") return;
            var line = ChatPolicy.FromDiscord(data, guildId, channelId, textLimit);
            if (line != null && discordIds.Accept((string)data["id"]))
            {
                Enqueue(incoming, line);
                if (adminReady) Enqueue(adminOutgoing, ChatPolicy.AuditDiscord(line));
            }
        }

        private void Enqueue(Queue<string> queue, string line)
        {
            if (queue.Count < QueueLimit) { queue.Enqueue(line); return; }
            if (Time.realtimeSinceStartup < nextQueueWarning) return;
            nextQueueWarning = Time.realtimeSinceStartup + 10;
            Logger.LogWarning("The chat relay queue is full. New messages are dropped until it catches up.");
        }

        private void Broadcast(string line)
        {
            if (!CanRelay || ZRoutedRpc.instance == null) return;
            foreach (var peer in network.GetPeers())
            {
                if (!peer.IsReady() || !peer.m_rpc.IsConnected() || peer.m_characterID.IsNone()) continue;
                foreach (var player in network.GetPlayerList())
                {
                    if (player.m_characterID != peer.m_characterID || !player.m_userInfo.m_id.IsValid) continue;
                    try
                    {
                        // Vanilla resolves the chat title from a known platform user.
                        // Use this recipient's identity; the body explicitly labels the Discord author.
                        var user = new UserInfo { Name = "Discord", UserId = player.m_userInfo.m_id };
                        ZRoutedRpc.instance.InvokeRoutedRPC(peer.m_uid, "ChatMessage", peer.m_refPos + Vector3.up * 2,
                            (int)Talker.Type.Normal, user, line);
                    }
                    catch (Exception exception) { Logger.LogWarning("Game chat delivery failed (" + exception.GetType().Name + ")."); }
                    break;
                }
            }
        }

        private bool ReportResult<T>(Task<RestResult<T>> task, string operation)
        {
            if (task.Status != TaskStatus.RanToCompletion)
            {
                if (task.IsFaulted) { var ignored = task.Exception; }
                Logger.LogWarning(operation + " failed or was cancelled.");
                return false;
            }
            if (task.Result.Success) return true;
            var http = task.Result.Exception as DiscordHttpException;
            Logger.LogWarning(operation + " failed" + (http == null ? "." : " (HTTP " + (int)http.StatusCode + ", Discord code " + http.DiscordCode + "). Check channel permissions."));
            return false;
        }

        private void StopBridge()
        {
            ready = false; adminReady = false; commandsReady = false;
            if (ownsConnection)
            {
                DiscordAPI.GatewayEventReceived -= OnDiscordEvent;
                DiscordAPI.InteractionCreated -= OnInteraction;
                DiscordAPI.Stop();
                DiscordAPI.Update();
                if (ReferenceEquals(DiscordAPI.Logger, botLogger)) DiscordAPI.Logger = previousLogger;
            }
            ownsConnection = false;
            // Observe faults without waiting or carrying messages into a subsequent world session.
            Observe(startup); Observe(channelLookup); Observe(send); Observe(adminSend); Observe(adminLookup); Observe(commandsLookup);
            startup = null; channelLookup = null; send = null;
            adminSend = null; adminLookup = null; commandsLookup = null; applicationId = null;
            ClearCommands();
            adminOutgoing.Clear();
            outgoing.Clear(); incoming.Clear(); coalescer.Clear(); gameIds.Clear(); discordIds.Clear();
        }

        private static void Observe(Task task)
        {
            if (task != null) _ = task.ContinueWith(t => { var ignored = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private void OnDestroy()
        {
            statistics?.Save();
            StopBridge();
            harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }

        private sealed class BotLogger : DiscordUnity.ILogger
        {
            private readonly ManualLogSource log;
            private readonly string secret;
            internal BotLogger(ManualLogSource log, string secret) { this.log = log; this.secret = secret; }
            private string Safe(string message) => (message ?? "").Replace(secret, "[redacted]");
            public void Log(string message) => log.LogInfo(Safe(message));
            public void LogWarning(string message) => log.LogWarning(Safe(message));
            public void LogError(string message, Exception exception = null) => log.LogError(Safe(message) + (exception == null ? "" : " (" + exception.GetType().Name + ")"));
        }
    }
}
