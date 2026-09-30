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
using System.Threading.Tasks;
using UnityEngine;

namespace ValheimDiscordChat
{
    [BepInPlugin(Guid, "Valheim Discord Chat", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "icecub.ValheimDiscordChat";
        internal static Plugin Instance { get; private set; }
        private const int QueueLimit = 100;
        private readonly Queue<string> outgoing = new Queue<string>();
        private readonly Queue<string> incoming = new Queue<string>();
        private readonly ChatCoalescer coalescer = new ChatCoalescer();
        private readonly RecentIds gameIds = new RecentIds();
        private readonly RecentIds discordIds = new RecentIds();
        private ConfigEntry<bool> modEnabled, toDiscord, toGame;
        private ConfigEntry<string> token, channel;
        private ConfigEntry<int> maxLength;
        private Harmony harmony;
        private ZNet network;
        private bool attempted, ownsConnection, ready, clientWarning;
        private string channelId, guildId;
        private int textLimit;
        private float nextQueueWarning;
        private Task<bool> startup;
        private Task<RestResult<DiscordChannel>> channelLookup;
        private Task<RestResult<DiscordMessage>> send;
        private DiscordUnity.ILogger previousLogger;
        private BotLogger botLogger;

        internal bool CanRelay => ready && ownsConnection && DiscordAPI.IsActive && network &&
            network == ZNet.instance && network.IsServer() && network.IsDedicated();

        private void Awake()
        {
            Instance = this;
            modEnabled = Config.Bind("General", "Enabled", true, "Enable the bridge on a dedicated server. Restart after changing settings.");
            token = Config.Bind("Discord", "BotToken", "", "Private Discord bot token. DISCORD_BOT_TOKEN overrides this value if set.");
            channel = Config.Bind("Discord", "ChannelId", "", "Discord text channel ID. Enable Developer Mode and use Copy Channel ID.");
            toDiscord = Config.Bind("Chat", "GameToDiscord", true, "Forward only shouts (/s) to Discord. Normal chat, whispers and pings are excluded.");
            toGame = Config.Bind("Chat", "DiscordToGame", true, "Forward human text messages from the configured channel to all connected players.");
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
            for (int i = 0; i < 8 && incoming.Count != 0; i++) Broadcast(incoming.Dequeue());
        }

        private void StartBridge()
        {
            attempted = true;
            string secret = (Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? token.Value).Trim();
            channelId = channel.Value.Trim();
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
        }

        internal void Capture(ZRpc source, ZRoutedRpc.RoutedRPCData packet)
        {
            if (!CanRelay || !toDiscord.Value) return;
            try
            {
                ZNetPeer sender = null;
                foreach (var peer in network.GetPeers())
                    if (ReferenceEquals(peer.m_rpc, source) && peer.IsReady() && source.IsConnected()) { sender = peer; break; }
                if (sender == null) return;
                int type; string text;
                if (!NativeChatReader.TryRead(packet, sender.m_uid, sender.m_characterID, out type, out text)) return;
                if (!gameIds.Accept(sender.m_uid + ":" + packet.m_msgID)) return;
                if (!coalescer.Accept(sender.m_uid, type, text, packet.m_targetPeerID, Time.realtimeSinceStartup)) return;
                var line = ChatPolicy.ToDiscord(sender.m_playerName, text, textLimit);
                if (line != null) Enqueue(outgoing, line);
            }
            catch (Exception exception) { Logger.LogWarning("A chat packet could not be relayed (" + exception.GetType().Name + "). Vanilla delivery continues."); }
        }

        private void OnDiscordEvent(string name, JToken data)
        {
            if (!CanRelay || !toGame.Value || name != "MESSAGE_CREATE") return;
            var line = ChatPolicy.FromDiscord(data, guildId, channelId, textLimit);
            if (line != null && discordIds.Accept((string)data["id"])) Enqueue(incoming, line);
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
            ready = false;
            if (ownsConnection)
            {
                DiscordAPI.GatewayEventReceived -= OnDiscordEvent;
                DiscordAPI.Stop();
                DiscordAPI.Update();
                if (ReferenceEquals(DiscordAPI.Logger, botLogger)) DiscordAPI.Logger = previousLogger;
            }
            ownsConnection = false;
            // Observe faults without waiting or carrying messages into a subsequent world session.
            Observe(startup); Observe(channelLookup); Observe(send);
            startup = null; channelLookup = null; send = null;
            outgoing.Clear(); incoming.Clear(); coalescer.Clear(); gameIds.Clear(); discordIds.Clear();
        }

        private static void Observe(Task task)
        {
            if (task != null) _ = task.ContinueWith(t => { var ignored = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private void OnDestroy()
        {
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
