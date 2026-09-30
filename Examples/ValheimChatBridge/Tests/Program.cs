using Newtonsoft.Json.Linq;
using System;
using ValheimDiscordChat;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception(name);
    }
    private static JObject Message() => JObject.Parse(@"{
        'id':'333','guild_id':'111','channel_id':'222','type':0,
        'author':{'id':'444','bot':false,'username':'Login','global_name':'Display'},
        'member':{'nick':'Nickname'},'content':'Hello Vikings'}");
    private static string From(JObject value) => ChatPolicy.FromDiscord(value, "111", "222", 500);
    public static int Main()
    {
        try
        {
            var message = Message();
            Check(From(message) == "[Discord] Nickname: Hello Vikings", "guild nickname");
            message["member"]["nick"] = " ";
            Check(From(message).StartsWith("[Discord] Display:"), "display name fallback");
            message["author"]["global_name"] = JValue.CreateNull();
            Check(From(message).StartsWith("[Discord] Login:"), "username fallback");
            message = Message(); message["author"]["bot"] = true;
            Check(From(message) == null, "bot loops suppressed");
            message = Message(); message["webhook_id"] = "555";
            Check(From(message) == null, "webhook loops suppressed");
            message = Message(); message["author"]["system"] = true;
            Check(From(message) == null, "system user suppressed");
            message = Message(); message["channel_id"] = "999";
            Check(From(message) == null, "other channel ignored");
            message = Message(); message["guild_id"] = "999";
            Check(From(message) == null, "other guild ignored");
            message = Message(); message.Remove("guild_id");
            Check(From(message) == null, "DM ignored");
            message = Message(); message["type"] = 7;
            Check(From(message) == null, "join system message ignored");
            message = Message(); message["type"] = 19;
            Check(From(message) != null, "human replies supported");
            message = Message(); message["content"] = "\n\t";
            Check(From(message) == null, "attachment-only/empty text ignored");
            message = Message(); message["content"] = "Hi <@666>";
            message["mentions"] = JArray.Parse("[{'id':'666','username':'Other','member':{'nick':'Friend'}}]");
            Check(From(message).EndsWith("Hi @Friend"), "mentions become readable text");
            message = Message(); message["content"] = "One\r\nTwo\t<color=red>\0";
            var sanitized = From(message);
            Check(!sanitized.Contains("\n") && !sanitized.Contains("<") && !sanitized.Contains("\0"), "plain one-line game text");
            Check(ChatPolicy.Plain("ABC\ud83d\ude00D", 4) == "ABC", "surrogate truncation");
            Check(ChatPolicy.ToDiscord("Viking", "Hello", 500) == "[Valheim] Viking: Hello", "outgoing format");
            Check(ChatPolicy.ToDiscord("Viking", "\t\n", 500) == null, "empty game text ignored");
            Check(ChatPolicy.ToDiscord("Viking", "*test* @everyone", 500) == "[Valheim] Viking: \\*test\\* @everyone", "markdown escaping");
            var maximum = ChatPolicy.ToDiscord(new string('*', 80), new string('*', 1500), 1500);
            Check(maximum.Length <= 2000 && !maximum.EndsWith("\\"), "Discord limit respects escaping");
            Check(ChatPolicy.IsSnowflake("18446744073709551615"), "64-bit Discord ID");
            Check(!ChatPolicy.IsSnowflake("0") && !ChatPolicy.IsSnowflake("-1") && !ChatPolicy.IsSnowflake("channel"), "invalid configuration IDs");

            var groups = new ChatCoalescer();
            Check(groups.Accept(1, 2, "Hello", 10, 0), "first recipient relayed");
            Check(!groups.Accept(1, 2, "Hello", 20, 0.1), "second recipient coalesced");
            Check(!groups.Accept(1, 2, "Hello", 30, 0.2), "third recipient coalesced");
            Check(groups.Accept(1, 2, "Hello", 10, 0.3), "intentional repeated message preserved");
            Check(groups.Accept(2, 2, "Hello", 10, 0.4), "different sender preserved");
            Check(groups.Accept(1, 2, "New", 10, 0.5), "different message preserved");
            Check(groups.Accept(1, 2, "Hello", 40, 2), "coalescing window expires");
            groups.Clear();
            Check(groups.Accept(1, 2, "Hello", 40, 2.1), "new session resets coalescing");
            var ids = new RecentIds();
            Check(ids.Accept("1") && !ids.Accept("1"), "replayed events suppressed");
            for (int i = 2; i <= 513; i++) ids.Accept(i.ToString());
            Check(ids.Accept("1"), "duplicate memory bounded");
            ids.Clear();
            Check(ids.Accept("513"), "new session resets IDs");
            Console.WriteLine("PASS chat policy: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }
}
