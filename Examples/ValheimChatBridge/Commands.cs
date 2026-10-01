using DiscordUnity;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace ValheimDiscordChat
{
    public sealed partial class Plugin
    {
        private HashSet<string> roles = new HashSet<string>();
        private readonly RecentIds interactionIds = new RecentIds();
        private readonly Dictionary<string, string> registeredCommands = new Dictionary<string, string>();
        private readonly List<PendingCommand> commands = new List<PendingCommand>();
        private readonly List<Task<RestResult<JObject>>> replies = new List<Task<RestResult<JObject>>>();
        private readonly List<Task<RestResult<bool>>> denials = new List<Task<RestResult<bool>>>();
        private Task<RestResult<JObject>> registration;
        private int registrationIndex;
        private readonly DateTime processStarted = Process.GetCurrentProcess().StartTime.ToUniversalTime();

        private sealed class PendingCommand
        {
            internal DiscordInteraction Interaction;
            internal Task<RestResult<bool>> Acknowledgement;
            internal string Name, Value;
            internal ZNetPeer Target;
            internal string SteamId;
        }

        private void RegisterCommands()
        {
            if (!commandsReady || !ChatPolicy.IsSnowflake(applicationId)) return;
            if (registration != null && registration.IsCompleted)
            {
                if (ReportResult(registration, "Slash command registration"))
                {
                    registeredCommands[CommandPolicy.Names[registrationIndex]] = (string)registration.Result.Data["id"];
                    Logger.LogInfo("Registered /" + CommandPolicy.Names[registrationIndex] + ".");
                }
                registration = null; registrationIndex++;
            }
            if (registration == null && registrationIndex < CommandPolicy.Names.Length)
                registration = DiscordAPI.CreateApplicationCommand(applicationId, CommandPolicy.Definition(CommandPolicy.Names[registrationIndex]), guildId);
        }

        private void OnInteraction(DiscordInteraction interaction)
        {
            if (interaction.Type != 2 || interaction.ApplicationId != applicationId ||
                interaction.Data == null || (int?)interaction.Data["type"] != 1) return;
            string name = (string)interaction.Data["name"], commandId;
            if (name == null || !registeredCommands.TryGetValue(name, out commandId) ||
                commandId != (string)interaction.Data["id"] || !interactionIds.Accept(interaction.Id)) return;
            string denial = null;
            if (!CanRelay || !commandsReady) denial = "The Valheim server is not ready.";
            else if (interaction.GuildId != guildId || interaction.ChannelId != commandsChannelId)
                denial = "Use this command in the configured commands channel.";
            else if (CommandPolicy.IsAdmin(name) && !CommandPolicy.HasRole(interaction.Member, roles))
                denial = "You need one of the server's configured moderator roles to use this command.";
            else if (commands.Count >= 32 || replies.Count >= 32)
                denial = "The command queue is busy. Please try again shortly.";
            var pending = new PendingCommand { Interaction = interaction, Name = name };
            if (denial == null && CommandPolicy.IsAdmin(name))
            {
                pending.Value = CommandPolicy.Option(interaction.Data, name == "alert" ? "message" : "player");
                if (string.IsNullOrWhiteSpace(pending.Value) || pending.Value.Length > (name == "alert" ? 500 : 100))
                    denial = "Supply a valid " + (name == "alert" ? "message (1–500 characters)." : "player name or Steam ID (1–100 characters).");
                else if (name != "alert")
                    pending.Target = FindPlayer(pending.Value, name == "kick" || name == "ban", out pending.SteamId, out denial);
            }
            if (denial != null)
            {
                var response = interaction.Respond(denial, true);
                if (denials.Count < 64) denials.Add(response); else Observe(response);
                return;
            }
            // No Unity or moderation work occurs on an HTTP continuation. Update polls the acknowledgement.
            pending.Acknowledgement = interaction.Defer(CommandPolicy.IsAdmin(name));
            commands.Add(pending);
        }

        private ZNetPeer FindPlayer(string value, bool requireSteam, out string steamId, out string error)
        {
            steamId = null; error = null;
            var matches = network.GetPeers().Where(p => p.IsReady() && p.m_rpc.IsConnected() &&
                (string.Equals(p.m_playerName, value, StringComparison.OrdinalIgnoreCase) || GetSteamId(p) == value)).ToList();
            if (matches.Count == 0) { error = "No online player matches that exact name or Steam ID."; return null; }
            if (matches.Count != 1) { error = "That player name is ambiguous. Use the Steam ID instead."; return null; }
            steamId = GetSteamId(matches[0]);
            if (requireSteam && steamId == null) { error = "This player's authenticated connection has no Steam ID. No action was taken."; return null; }
            return matches[0];
        }
        private static string GetSteamId(ZNetPeer peer)
        {
            // The socket identity is authenticated by Valheim's backend. Never trust a chat UserInfo.
            string id = peer.m_socket.GetHostName();
            if (id != null && id.StartsWith("Steam_", StringComparison.Ordinal)) id = id.Substring(6);
            return CommandPolicy.IsSteamId(id) ? id : null;
        }

        private void UpdateCommands()
        {
            for (int i = denials.Count - 1; i >= 0; i--)
                if (denials[i].IsCompleted) { ReportResult(denials[i], "Command denial"); denials.RemoveAt(i); }
            for (int i = replies.Count - 1; i >= 0; i--)
                if (replies[i].IsCompleted) { ReportResult(replies[i], "Command reply"); replies.RemoveAt(i); }
            for (int i = commands.Count - 1; i >= 0; i--)
            {
                var pending = commands[i];
                if (!pending.Acknowledgement.IsCompleted) continue;
                commands.RemoveAt(i);
                if (!ReportResult(pending.Acknowledgement, "Command acknowledgement")) continue;
                object result;
                try { result = ExecuteCommand(pending.Name, pending.Value, pending.Target, pending.SteamId); }
                catch (Exception ex)
                {
                    Logger.LogError("Command /" + pending.Name + " failed (" + ex.GetType().Name + ").");
                    result = "The command failed. Check the server log before retrying.";
                }
                if (CommandPolicy.IsAdmin(pending.Name)) Logger.LogInfo("Discord /" + pending.Name + " requested by user " + pending.Interaction.UserId + ": " + result);
                var message = result as JObject ?? new JObject { ["content"] = (string)result, ["embeds"] = new JArray() };
                message["allowed_mentions"] = new JObject { ["parse"] = new JArray() };
                replies.Add(pending.Interaction.EditOriginalResponse(message));
            }
        }

        private object ExecuteCommand(string name, string value, ZNetPeer target, string steamId)
        {
            if (!CanRelay || !commandsReady) return "The Valheim server is not ready.";
            if (name == "stats") return Stats();
            if (name == "online") return Online();
            if (ZRoutedRpc.instance == null) return "The game RPC transport is not ready.";
            if (name == "alert")
            {
                string text = ChatPolicy.Plain(value, 500);
                if (string.IsNullOrWhiteSpace(text)) return "The announcement is empty after removing game markup.";
                // '$' starts localization lookups in vanilla HUD text. Treat announcements as literal text.
                text = text.Replace('$', '＄');
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ShowMessage", (int)MessageHud.MessageType.Center, text);
                return "Global announcement sent to connected players.";
            }
            if (target == null || !network.GetPeers().Contains(target) || !target.IsReady() || !target.m_rpc.IsConnected())
                return "That player disconnected before the command could run. No action was taken.";
            if ((name == "kick" || name == "ban") && (steamId == null || steamId != GetSteamId(target)))
                return "The player's Steam identity changed. No action was taken.";
            if (name == "kick") { network.Kick(steamId); return "Kick requested for Steam ID " + steamId + "."; }
            if (name == "ban")
            {
                network.Ban(steamId); network.Kick(steamId);
                return "Steam ID " + steamId + " added to Valheim's ban list; kick requested.";
            }
            if (name == "setatspawn")
            {
                if (target.m_characterID.IsNone()) return "That player has not spawned yet.";
                Vector3 position;
                string location = Game.instance ? Game.instance.m_StartLocation : "StartTemple";
                if (!ZoneSystem.instance || !ZoneSystem.instance.GetLocationIcon(location, out position))
                    return "The world spawn location is not available yet. No teleport was sent.";
                // Match vanilla's world-spawn position. The unmodified client loads the distant area.
                ZRoutedRpc.instance.InvokeRoutedRPC(target.m_uid, "RPC_TeleportPlayer", position + Vector3.up * 2f, Quaternion.identity, true);
                return "Teleport to world spawn requested. The player's client may take a few seconds to load the area.";
            }
            return "Unknown command.";
        }

        private JObject Stats()
        {
            var peers = network.GetPeers().Count(p => p.IsReady() && p.m_rpc.IsConnected());
            var data = statistics?.Data;
            string day = EnvMan.instance ? "Day " + EnvMan.instance.GetDay(network.GetTimeSeconds()) : "Unavailable (world loading)";
            var fields = new JArray
            {
                EmbedField("Server status", "Online"),
                EmbedField("Uptime", CommandPolicy.Duration(DateTime.UtcNow - processStarted)),
                EmbedField("Valheim version", global::Version.GetVersionString()),
                EmbedField("In-game day", day),
                EmbedField("Players online", peers.ToString()),
                EmbedField("Unique players", data == null ? "Unavailable" : data.Players.Count.ToString()),
                EmbedField("Recorded deaths", data == null ? "Unavailable" : data.Deaths.ToString())
            };
            if (data != null) fields.Add(EmbedField("Death tracking since", data.TrackingSinceUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"), false));
            bool storageWarning = statistics != null && !statistics.IsPersistent;
            if (storageWarning) fields.Add(EmbedField("Statistics storage warning", "Statistics could not be loaded or saved. Current totals may not survive a restart.", false));
            return EmbedReply("Valheim Server Stats", fields, "Unique players: world history. Deaths: since tracking began.", storageWarning ? 0xFEE75C : 0x57F287);
        }
        private JObject Online()
        {
            var peers = network.GetPeers().Where(p => p.IsReady() && p.m_rpc.IsConnected()).OrderBy(p => p.m_playerName, StringComparer.OrdinalIgnoreCase).ToList();
            var fields = new JArray();
            foreach (var peer in peers.Take(25))
            {
                DateTime since;
                string duration = statistics != null && statistics.Sessions.TryGetValue(peer, out since)
                    ? "Online for " + CommandPolicy.Duration(DateTime.UtcNow - since) : "Duration unavailable";
                string name = ChatPolicy.Plain(peer.m_playerName, 80);
                fields.Add(EmbedField(ChatPolicy.AuditDiscord(name.Length == 0 ? "Player" : name), duration));
            }
            string description = peers.Count == 0 ? "No players are online." : "**" + peers.Count + "** " + (peers.Count == 1 ? "player connected." : "players connected.");
            if (peers.Count > fields.Count) description += "\nShowing the first 25 players; " + (peers.Count - fields.Count) + " more are online.";
            var reply = EmbedReply("Online Players", fields, "Session durations start when the plugin observes each connection.", 0x5865F2);
            reply["embeds"][0]["description"] = description;
            return reply;
        }
        private static JObject EmbedField(string name, string value, bool inline = true)
            => new JObject { ["name"] = name, ["value"] = value, ["inline"] = inline };
        private static JObject EmbedReply(string title, JArray fields, string footer, int color)
            => new JObject
            {
                ["content"] = "",
                ["embeds"] = new JArray(new JObject
                {
                    ["title"] = title, ["color"] = color, ["fields"] = fields,
                    ["footer"] = new JObject { ["text"] = footer }, ["timestamp"] = DateTime.UtcNow.ToString("o")
                })
            };
        private void ClearCommands()
        {
            Observe(registration); registration = null; registrationIndex = 0; registeredCommands.Clear(); interactionIds.Clear();
            foreach (var item in commands) Observe(item.Acknowledgement);
            foreach (var item in replies) Observe(item);
            foreach (var item in denials) Observe(item);
            commands.Clear(); replies.Clear(); denials.Clear();
        }
    }
}
