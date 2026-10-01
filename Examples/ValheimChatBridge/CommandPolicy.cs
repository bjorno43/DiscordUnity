using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace ValheimDiscordChat
{
    internal static class CommandPolicy
    {
        internal static readonly string[] Names = { "stats", "online", "kick", "ban", "alert", "setatspawn" };
        internal static bool IsAdmin(string name) => name == "kick" || name == "ban" || name == "alert" || name == "setatspawn";
        internal static HashSet<string> ParseRoles(string value)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in (value ?? "").Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!ChatPolicy.IsSnowflake(item)) throw new FormatException("AllowedRoleIds must contain numeric Discord role IDs.");
                result.Add(item);
            }
            return result;
        }
        internal static bool HasRole(JObject member, HashSet<string> allowed)
        {
            var roles = member?["roles"] as JArray;
            if (roles == null || allowed == null || allowed.Count == 0) return false;
            foreach (var role in roles)
                if (role.Type == JTokenType.String && allowed.Contains((string)role)) return true;
            return false;
        }
        internal static string Option(JObject data, string name)
        {
            var options = data?["options"] as JArray;
            if (options == null || options.Count != 1) return null;
            var option = options[0] as JObject;
            if (option == null) return null;
            return (string)option["name"] == name && (int?)option["type"] == 3 && option["value"]?.Type == JTokenType.String
                ? ((string)option["value"]).Trim() : null;
        }
        internal static bool IsSteamId(string id)
            => id != null && id.Length == 17 && id.StartsWith("7656119", StringComparison.Ordinal) && ChatPolicy.IsSnowflake(id);
        internal static string Duration(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            return (elapsed.Days == 0 ? "" : elapsed.Days + "d ") + elapsed.Hours + "h " + elapsed.Minutes + "m " + elapsed.Seconds + "s";
        }
        internal static object Definition(string name)
        {
            string description = name == "stats" ? "Show Valheim server statistics." : name == "online" ? "List online players and session durations." :
                name == "kick" ? "Kick an online player (configured moderator role required)." :
                name == "ban" ? "Ban an online player's Steam ID (configured moderator role required)." :
                name == "alert" ? "Display a global in-game announcement (configured moderator role required)." :
                "Teleport an online player to world spawn (configured moderator role required).";
            var command = new JObject { ["name"] = name, ["type"] = 1, ["description"] = description };
            if (IsAdmin(name)) command["options"] = new JArray(new JObject
            {
                ["name"] = name == "alert" ? "message" : "player", ["type"] = 3, ["required"] = true,
                ["description"] = name == "alert" ? "Announcement text." : "Exact online player name or Steam ID.",
                ["min_length"] = 1, ["max_length"] = name == "alert" ? 500 : 100
            });
            return command;
        }
    }
}
