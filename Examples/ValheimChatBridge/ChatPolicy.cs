using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace ValheimDiscordChat
{
    internal static class ChatPolicy
    {
        internal static bool IsSnowflake(string value)
        {
            ulong number;
            return value != null && value.Length > 0 && value.Length <= 20 &&
                ulong.TryParse(value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out number) && number != 0;
        }

        internal static string Plain(string value, int limit)
        {
            var result = new StringBuilder();
            foreach (char c in value ?? "")
            {
                if (c == '<' || c == '>' || char.IsWhiteSpace(c)) result.Append(' ');
                else if (!char.IsControl(c) && !(c >= '\u202a' && c <= '\u202e') && !(c >= '\u2066' && c <= '\u2069')) result.Append(c);
            }
            var text = result.ToString().Trim();
            if (text.Length > limit)
            {
                int end = limit;
                if (end > 0 && char.IsHighSurrogate(text[end - 1])) end--;
                text = text.Substring(0, end);
            }
            return text;
        }

        private static string Markdown(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value)
            {
                if ("\\*_~`|[]".IndexOf(c) >= 0) result.Append('\\');
                result.Append(c);
            }
            return result.ToString();
        }

        internal static string AuditDiscord(string line) => TruncateEscaped(Markdown(line), 2000);

        internal static string ToDiscord(string playerName, string text, int limit, string label = "[Valheim]")
        {
            var body = Plain(text, limit);
            if (body.Length == 0) return null;
            // Escaped text stays below Discord's 2,000-character content limit.
            var name = Plain(playerName, 80);
            if (name.Length == 0) name = "Player";
            var prefix = label + " " + Markdown(name) + ": ";
            return prefix + TruncateEscaped(Markdown(body), 2000 - prefix.Length);
        }

        private static string TruncateEscaped(string text, int limit)
        {
            if (text.Length <= limit) return text;
            int end = limit;
            if (char.IsHighSurrogate(text[end - 1])) end--;
            int slashes = 0;
            for (int i = end - 1; i >= 0 && text[i] == '\\'; i--) slashes++;
            if (slashes % 2 != 0) end--;
            return text.Substring(0, end);
        }

        internal static string FromDiscord(JToken message, string guildId, string channelId, int limit)
        {
            if (message == null || (string)message["guild_id"] != guildId || (string)message["channel_id"] != channelId) return null;
            var author = message["author"];
            if (author == null || !IsSnowflake((string)author["id"]) || (bool?)author["bot"] == true ||
                (bool?)author["system"] == true || message["webhook_id"]?.Type == JTokenType.String) return null;
            int type = (int?)message["type"] ?? 0;
            if (type != 0 && type != 19) return null;
            var content = (string)message["content"];
            if (string.IsNullOrWhiteSpace(content)) return null;
            foreach (var mention in message["mentions"] as JArray ?? new JArray())
            {
                var id = (string)mention["id"];
                if (!IsSnowflake(id)) continue;
                var label = Plain((string)mention["member"]?["nick"] ?? (string)mention["global_name"] ?? (string)mention["username"], 80);
                content = content.Replace("<@" + id + ">", "@" + label).Replace("<@!" + id + ">", "@" + label);
            }
            var body = Plain(content, limit);
            if (body.Length == 0) return null;
            string name = null;
            foreach (var candidate in new[] { (string)message["member"]?["nick"], (string)author["global_name"], (string)author["username"] })
            {
                name = Plain(candidate, 80);
                if (name.Length != 0) break;
            }
            if (string.IsNullOrEmpty(name)) name = "Discord user";
            return "[Discord] " + name + ": " + body;
        }
    }

    // Clients may send the same chat separately to each eligible recipient.
    // Coalesce distinct targets, but preserve a repeated message to the same target.
    internal sealed class ChatCoalescer
    {
        private readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
        private sealed class Group
        {
            internal double Time;
            internal readonly HashSet<long> Targets = new HashSet<long>();
        }
        internal bool Accept(long sender, int type, string text, long target, double now)
        {
            string key = sender + ":" + type + ":" + text;
            Group group;
            if (groups.TryGetValue(key, out group) && now - group.Time <= 1 && group.Targets.Add(target)) return false;
            if (groups.Count >= 256) groups.Clear();
            group = new Group { Time = now };
            group.Targets.Add(target);
            groups[key] = group;
            return true;
        }
        internal void Clear() => groups.Clear();
    }

    internal sealed class RecentIds
    {
        private readonly HashSet<string> values = new HashSet<string>();
        private readonly Queue<string> order = new Queue<string>();
        internal bool Accept(string id)
        {
            if (string.IsNullOrEmpty(id) || !values.Add(id)) return false;
            order.Enqueue(id);
            if (order.Count > 512) values.Remove(order.Dequeue());
            return true;
        }
        internal void Clear() { values.Clear(); order.Clear(); }
    }
}
