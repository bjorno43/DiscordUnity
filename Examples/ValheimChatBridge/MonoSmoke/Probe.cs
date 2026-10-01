using BepInEx;
using DiscordUnity;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Splatform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using ValheimDiscordChat;

public static class ChatBridgeProbe
{
    private static readonly List<string> Checks = new List<string>();
    private static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        Checks.Add(label);
    }
    private static void Set(Type type, object target, string name, object value) => AccessTools.Field(type, name).SetValue(target, value);
    private static object Call(object target, string method, params object[] args) => AccessTools.Method(target.GetType(), method).Invoke(target, args);
    // Only platform identity and the text privilege are supplied. A profile lookup fails the
    // test, proving that the native self-account permission path needs no fabricated profile.
    private sealed class MemoryPlatform : IDistributionPlatform, ILocalUser, IPrivilegeProvider, IRelationsProvider
    {
        internal PlatformUserID Identity;
        internal PrivilegeResult TextPrivilege = PrivilegeResult.Granted;
        public Platform Platform => new Platform("Steam");
        public ILocalUser LocalUser => this;
        public IUIProvider UIProvider => null;
        public IPrivilegeProvider PrivilegeProvider => this;
        public IRelationsProvider RelationsProvider => this;
        public IHardwareInfoProvider HardwareInfoProvider => null;
        public IPerformanceCharacteristicsProvider PerformanceCharacteristicsProvider => null;
        public IPLMProvider PLMProvider => null;
        public ISaveDataProvider SaveDataProvider => null;
        public IAchievementManager AchievementManager => null;
        public IMatchmakingProvider MatchmakingProvider => null;
        public IAuthenticationProvider AuthenticationProvider => null;
        public IInputDeviceManager InputDeviceManager => null;
        public IPreferencesProvider PreferencesProvider => null;
        public IActivityProvider ActivityProvider => null;
        public string DisplayName => "Memory player";
        public string Locale => "en-US";
        public PlatformUserID PlatformUserID => Identity;
        public bool IsSignedIn => true;
        public event SignedInHandler SignedIn { add { } remove { } }
        public event PlatformSignOutHandler PlatformSignOut { add { } remove { } }
        public event RelationsChangedHandler RelationsChanged { add { } remove { } }
        public PrivilegeResult CheckPrivilege(Privilege privilege) => TextPrivilege;
        public void SetMultiplayerUsage(MultiplayerUsage usage) { }
        public void SetCrossplayPrivilege(PrivilegeResult privilege) { }
        public void Update() { }
        public void InitializeAsync(PlatformConfiguration configuration, AsyncOperationCompletedHandler completed) => completed(true);
        public void Dispose() { }
        public void GetUserProfileAsync(PlatformUserID user, GetUserProfileCompletedHandler completed, GetUserProfileFailedHandler failed)
            => throw new Exception("Unexpected external profile lookup for a solo recipient");
        public bool TryGetUserProfile(PlatformUserID user, out IUserProfile profile) { profile = null; return false; }
        public bool IsFriend(PlatformUserID user) => false;
        public bool IsBlocked(PlatformUserID user) => false;
        public PlatformUserID[] GetFriends() => new PlatformUserID[0];
    }
    private sealed class MemorySocket : ISocket
    {
        internal readonly List<byte[]> Sent = new List<byte[]>();
        internal bool Connected = true;
        internal string SteamId;
        public bool IsConnected() => Connected;
        public void Send(ZPackage p) => Sent.Add(p.GetArray());
        public ZPackage Recv() => null;
        public int GetSendQueueSize() => 0;
        public int GetCurrentSendRate() => 0;
        public bool IsHost() => false;
        public void Dispose() { }
        public bool GotNewData() => false;
        public void Close() { }
        public string GetEndPointString() => "isolated-memory-chat-socket";
        public void GetAndResetStats(out int sent, out int recv) { sent = recv = 0; }
        public void GetConnectionQuality(out float a, out float b, out int c, out float d, out float e) { a = b = d = e = 0; c = 0; }
        public ISocket Accept() => null;
        public int GetHostPort() => 0;
        public bool Flush() => true;
        public string GetHostName() => SteamId;
        public void VersionMatch() { }
        internal List<ZRoutedRpc.RoutedRPCData> Routed()
        {
            var result = new List<ZRoutedRpc.RoutedRPCData>();
            foreach (var bytes in Sent)
            {
                var p = new ZPackage(bytes);
                if (p.ReadInt() != "RoutedRPC".GetStableHashCode()) continue;
                var inner = p.ReadPackage();
                var data = new ZRoutedRpc.RoutedRPCData(); data.Deserialize(inner); result.Add(data);
            }
            return result;
        }
    }
    private sealed class MemoryHttp : HttpMessageHandler
    {
        internal readonly List<string> Bodies = new List<string>();
        internal readonly List<string> Routes = new List<string>();
        internal readonly List<string> Methods = new List<string>();
        internal TaskCompletionSource<HttpResponseMessage> Gate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync());
            Routes.Add(request.RequestUri.AbsolutePath);
            Methods.Add(request.Method.Method);
            if (request.RequestUri.AbsolutePath.EndsWith("/commands"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(new { id = (string)JObject.Parse(Bodies.Last())["name"] + "1" })) };
            if (Gate != null && request.RequestUri.AbsolutePath.EndsWith("/callback")) return await Gate.Task;
            return Response(request.RequestUri.AbsolutePath.EndsWith("/callback") ? HttpStatusCode.NoContent : HttpStatusCode.OK);
        }
        internal static HttpResponseMessage Response(HttpStatusCode code) => new HttpResponseMessage(code) { Content = new StringContent("{}") };
    }
    private static object Field(object target, string name) => AccessTools.Field(target.GetType(), name).GetValue(target);
    private static void Wait(Task task) { if (!task.Wait(3000)) throw new Exception("Memory HTTP task timed out"); }
    private static DiscordInteraction Interaction(string id, string command, string role = "555", string channel = "888", string player = "Viking Two")
    {
        var body = JObject.Parse("{'id':'" + id + "','application_id':'777','guild_id':'111','channel_id':'" + channel + "','type':2,'token':'memory-only-interaction','member':{'user':{'id':'444'},'roles':['" + role + "']},'data':{'id':'" + command + "1','name':'" + command + "','type':1}}");
        if (command != "stats" && command != "online") body["data"]["options"] = new JArray(new JObject
        { ["name"] = command == "alert" ? "message" : "player", ["type"] = 3, ["value"] = player });
        return (DiscordInteraction)Activator.CreateInstance(typeof(DiscordInteraction), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object[] { body }, null);
    }
    private static void PumpCommand(Plugin plugin, DiscordInteraction interaction)
    {
        Call(plugin, "OnInteraction", interaction);
        var queue = (System.Collections.IList)Field(plugin, "commands");
        if (queue.Count != 0) Wait((Task)Field(queue[queue.Count - 1], "Acknowledgement"));
        Call(plugin, "UpdateCommands");
        foreach (var reply in (List<Task<RestResult<JObject>>>)Field(plugin, "replies")) Wait(reply);
    }
    private static bool SpawnIcon(string name, ref Vector3 pos, ref bool __result)
    { Check(name == "StartTemple", "World-spawn lookup requests vanilla StartTemple"); pos = new Vector3(10, 20, 30); __result = true; return false; }
    private static void Receive(ZRoutedRpc router, ZNetPeer sender, long msgId, long target, int type, string text, bool local = false, long claimedSender = 11)
    {
        var data = new ZRoutedRpc.RoutedRPCData
        {
            m_msgID = msgId, m_senderPeerID = claimedSender, m_targetPeerID = target,
            m_targetZDO = local ? sender.m_characterID : ZDOID.None,
            m_methodHash = (local ? "Say" : "ChatMessage").GetStableHashCode()
        };
        var user = new UserInfo { Name = "Forged Name", UserId = new PlatformUserID("Steam_76561198000000011") };
        var parameters = local ? new object[] { type, user, text } : new object[] { Vector3.zero, type, user, text };
        ZRpc.Serialize(parameters, ref data.m_parameters);
        var pkg = new ZPackage(); data.Serialize(pkg); pkg.SetPos(0);
        Call(router, "RPC_RoutedRPC", sender.m_rpc, pkg);
    }

    public static void Run()
    {
        string error = "";
        string root = Environment.GetEnvironmentVariable("VALHEIM_DISCORD_CHAT_PROBE");
        try
        {
            AccessTools.Method(typeof(Paths), "SetExecutablePath").Invoke(null, new object[]
            {
                Path.Combine(root, "runtime/ValheimLab.exe"), Path.Combine(root, "isolated-bepinex"), null, null
            });
            var go = new GameObject("isolated chat probe"); go.SetActive(false);
            var network = go.AddComponent<ZNet>();
            Set(typeof(ZNet), null, "m_instance", network); Set(typeof(ZNet), null, "m_isServer", true);
            var router = new ZRoutedRpc(true); router.SetUID(100);
            var a = new ZNetPeer(new MemorySocket(), false) { m_uid = 11, m_playerName = "Viking One", m_characterID = new ZDOID(11, 1) };
            var b = new ZNetPeer(new MemorySocket(), false) { m_uid = 22, m_playerName = "Viking Two", m_characterID = new ZDOID(22, 1) };
            var c = new ZNetPeer(new MemorySocket(), false) { m_uid = 33, m_playerName = "Viking Three", m_characterID = new ZDOID(33, 1) };
            foreach (var peer in new[] { a, b, c })
            {
                ((MemorySocket)peer.m_socket).SteamId = "765611980000000" + peer.m_uid;
                network.GetPeers().Add(peer); router.AddPeer(peer);
                var p = default(ZNet.PlayerInfo); p.m_name = peer.m_playerName; p.m_characterID = peer.m_characterID;
                p.m_userInfo.m_id = new PlatformUserID("Steam_765611980000000" + peer.m_uid);
                p.m_userInfo.m_displayName = peer.m_playerName;
                p.m_userInfo.m_serverAssignedDisplayName = peer.m_playerName;
                p.m_userInfo.m_playfabId = "memory-playfab-" + peer.m_uid;
                network.GetPlayerList().Add(p);
            }
            var plugin = go.AddComponent<Plugin>();
            Call(plugin, "Awake"); // Actual BepInEx config binding and production Harmony installation.
            Check(plugin.Config.ContainsKey(new BepInEx.Configuration.ConfigDefinition("Discord", "BotToken")), "BepInEx creates token configuration without a real token");
            Check(network.IsDedicated() && network.IsServer(), "Actual dedicated-server assembly activates the server guard");
            Set(typeof(Plugin), plugin, "network", network); Set(typeof(Plugin), plugin, "ready", true);
            Set(typeof(Plugin), plugin, "ownsConnection", true); Set(typeof(Plugin), plugin, "channelId", "222");
            Set(typeof(Plugin), plugin, "guildId", "111"); Set(typeof(Plugin), plugin, "textLimit", 500);
            Set(typeof(Plugin), plugin, "adminReady", true); Set(typeof(Plugin), plugin, "adminChannelId", "333");
            var statsType = typeof(Plugin).Assembly.GetType("ValheimDiscordChat.ServerStatistics");
            var stats = Activator.CreateInstance(statsType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                new object[] { new Action<string>(s => { throw new Exception(s); }) }, null);
            string statsFile = Path.Combine(root, "isolated-bepinex", "stats-probe-" + System.Guid.NewGuid().ToString("N") + ".json");
            Call(stats, "Open", statsFile); Call(stats, "Tick", network); Set(typeof(Plugin), plugin, "statistics", stats);
            AccessTools.Property(typeof(DiscordAPI), "IsActive").SetValue(null, true);
            var outgoing = (Queue<string>)AccessTools.Field(typeof(Plugin), "outgoing").GetValue(plugin);
            Receive(router, a, 1, 22, 2, "Hello");
            Check(outgoing.Count == 1 && outgoing.Peek() == "[Valheim] Viking One: Hello", "Production Harmony captures native shout and uses authenticated peer name");
            var vanilla = ((MemorySocket)b.m_socket).Routed().Single();
            vanilla.m_parameters.ReadVector3(); Check(vanilla.m_parameters.ReadInt() == 2, "Vanilla routed delivery retains shout type");
            var untouched = new UserInfo(); untouched.Deserialize(ref vanilla.m_parameters);
            Check(untouched.Name == "Forged Name" && vanilla.m_parameters.ReadString() == "Hello", "Vanilla routed delivery retains untouched user/text data");
            Receive(router, a, 2, 33, 2, "Hello");
            Check(outgoing.Count == 1, "Multiple recipient packets produce one Discord relay");
            Receive(router, a, 3, 22, 2, "Hello");
            Check(outgoing.Count == 2, "Intentional repeated shout is preserved");
            Receive(router, a, 4, 22, 1, "Local", true); Receive(router, a, 5, 22, 0, "Whisper", true);
            Receive(router, a, 6, 22, 3, "Ping");
            Check(outgoing.Count == 2, "Normal chat, whispers and pings are excluded by production hooks");
            var admin = (Queue<string>)Field(plugin, "adminOutgoing");
            Check(admin.Count == 4 && admin.Contains("[Valheim Normal] Viking One: Local") && admin.Contains("[Valheim Whisper] Viking One: Whisper"), "Admin channel captures all chat types once and excludes map pings");
            Receive(router, a, 7, 22, 2, "Spoofed", claimedSender: 99);
            Check(outgoing.Count == 2, "Routed sender spoofing cannot publish to Discord");
            Set(typeof(ZNet), null, "m_isServer", false);
            Receive(router, a, 8, 22, 2, "Client");
            Check(outgoing.Count == 2, "Production client guard prevents chat forwarding");
            Set(typeof(ZNet), null, "m_isServer", true);
            var message = JObject.Parse("{'id':'999','guild_id':'111','channel_id':'222','type':0,'author':{'id':'444','username':'Login'},'member':{'nick':'Nick'},'content':'Hi Vikings'}");
            Call(plugin, "OnDiscordEvent", "MESSAGE_CREATE", message);
            var incoming = (Queue<string>)AccessTools.Field(typeof(Plugin), "incoming").GetValue(plugin);
            Check(incoming.Count == 1, "Production Discord event accepts configured channel and nickname");
            Call(plugin, "OnDiscordEvent", "MESSAGE_CREATE", message);
            Check(incoming.Count == 1, "Production Discord event deduplicates replayed messages");
            Check(admin.Count == 5 && admin.Last().Contains("Nick: Hi Vikings"), "Admin log mirrors public Discord chat once");
            message["channel_id"] = "333"; message["id"] = "998";
            Call(plugin, "OnDiscordEvent", "MESSAGE_CREATE", message);
            Check(incoming.Count == 1 && admin.Count == 5, "Admin channel is one-way and does not create relay loops");
            foreach (var peer in new[] { a, b, c }) ((MemorySocket)peer.m_socket).Sent.Clear();
            Call(plugin, "Broadcast", incoming.Dequeue());
            foreach (var peer in new[] { a, b, c })
            {
                var sent = ((MemorySocket)peer.m_socket).Routed().Single();
                Check(sent.m_methodHash == "ChatMessage".GetStableHashCode() && sent.m_targetPeerID == peer.m_uid && sent.m_targetZDO.IsNone(), "Discord message uses vanilla per-recipient ChatMessage RPC " + peer.m_uid);
                sent.m_parameters.ReadVector3(); Check(sent.m_parameters.ReadInt() == 1, "Discord delivery preserves case using Normal chat " + peer.m_uid);
                var user = new UserInfo(); user.Deserialize(ref sent.m_parameters);
                var player = network.GetPlayerList().Single(p => p.m_characterID == peer.m_characterID);
                Check(user.UserId == player.m_userInfo.m_id && sent.m_parameters.ReadString() == "[Discord] Nick: Hi Vikings", "Each recipient gets their own known identity and complete Discord body " + peer.m_uid);
            }
            Check(network.GetPlayerList().Count == 3 && network.GetPlayerList()[0].m_name == "Viking One", "Player roster is unchanged");
            var death = new ZRoutedRpc.RoutedRPCData { m_msgID = 90, m_senderPeerID = 11, m_targetPeerID = 0,
                m_targetZDO = a.m_characterID, m_methodHash = "OnDeath".GetStableHashCode(), m_parameters = new ZPackage() };
            Call(plugin, "Capture", a.m_rpc, death); Call(plugin, "Capture", a.m_rpc, death);
            var saved = JObject.Parse(File.ReadAllText(statsFile));
            Check((long)saved["Deaths"] == 1 && ((JObject)saved["Players"]).Count == 3, "Authenticated death is persisted once and Steam players are unique");
            death.m_senderPeerID = 99; Call(plugin, "Capture", a.m_rpc, death);
            Check((long)JObject.Parse(File.ReadAllText(statsFile))["Deaths"] == 1, "Spoofed death ignored");
            var reloaded = Activator.CreateInstance(statsType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                new object[] { new Action<string>(s => { throw new Exception(s); }) }, null);
            Call(reloaded, "Open", statsFile); Call(reloaded, "Death", network, a);
            Check((long)JObject.Parse(File.ReadAllText(statsFile))["Deaths"] == 1, "Restart reload preserves death total and rejects replay of last death");
            var history = new World(); history.m_playerHistory.Add(new ZNet.CrossNetworkUserInfo { m_id = new PlatformUserID("Steam_76561198000000044"), m_displayName = "Past Player" });
            history.m_playerHistory.Add(new ZNet.CrossNetworkUserInfo { m_id = new PlatformUserID("Steam_76561198000000011"), m_displayName = "Old Name" });
            Set(typeof(ZNet), null, "m_world", history); Call(stats, "Tick", network); Call(stats, "Save");
            Check(((JObject)JObject.Parse(File.ReadAllText(statsFile))["Players"]).Count == 4, "World history imports offline accounts and deduplicates online Steam accounts");
            var http = new MemoryHttp();
            var restType = typeof(DiscordAPI).Assembly.GetType("DiscordUnity.RestClient");
            var rest = Activator.CreateInstance(restType, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null,
                new object[] { new HttpClient(http), "memory-only-no-real-token" }, null);
            Set(typeof(DiscordAPI), null, "InteractionRest", rest);
            Set(typeof(Plugin), plugin, "applicationId", "777"); Set(typeof(Plugin), plugin, "commandsChannelId", "888");
            Set(typeof(Plugin), plugin, "commandsReady", true); Set(typeof(Plugin), plugin, "roles", new HashSet<string> { "555" });
            var registered = (Dictionary<string, string>)Field(plugin, "registeredCommands");
            foreach (var name in new[] { "stats", "online", "kick", "ban", "alert", "setatspawn" }) registered[name] = name + "1";
            foreach (var peer in new[] { a, b, c }) ((MemorySocket)peer.m_socket).Sent.Clear();
            PumpCommand(plugin, Interaction("1001", "kick", role: "999"));
            PumpCommand(plugin, Interaction("1002", "kick", channel: "222"));
            Check(((MemorySocket)b.m_socket).Sent.Count == 0 && ((System.Collections.IList)Field(plugin, "commands")).Count == 0, "Wrong role and wrong channel never queue a kick");
            Check(http.Bodies.All(s => (int)JObject.Parse(s)["type"] == 4 && (int)JObject.Parse(s)["data"]["flags"] == 64), "Unauthorized command denials are ephemeral");
            http.Gate = new TaskCompletionSource<HttpResponseMessage>();
            Call(plugin, "OnInteraction", Interaction("1003", "kick")); Call(plugin, "UpdateCommands");
            Check(((MemorySocket)b.m_socket).Sent.Count == 0, "Moderation waits for successful Discord acknowledgement");
            ((MemorySocket)b.m_socket).Connected = false;
            http.Gate.SetResult(MemoryHttp.Response(HttpStatusCode.NoContent));
            var pending = (System.Collections.IList)Field(plugin, "commands"); Wait((Task)Field(pending[0], "Acknowledgement"));
            Call(plugin, "UpdateCommands");
            Check(((MemorySocket)b.m_socket).Sent.Count == 0, "Player disconnect during acknowledgement prevents kick");
            ((MemorySocket)b.m_socket).Connected = true; http.Gate = null;
            http.Gate = new TaskCompletionSource<HttpResponseMessage>();
            Call(plugin, "OnInteraction", Interaction("1010", "kick"));
            http.Gate.SetResult(MemoryHttp.Response(HttpStatusCode.Forbidden));
            pending = (System.Collections.IList)Field(plugin, "commands"); Wait((Task)Field(pending[0], "Acknowledgement"));
            Call(plugin, "UpdateCommands");
            Check(((MemorySocket)b.m_socket).Sent.Count == 0, "Failed acknowledgement prevents moderation action"); http.Gate = null;
            PumpCommand(plugin, Interaction("1004", "alert", player: "Restart in five minutes"));
            foreach (var peer in new[] { a, b, c })
            {
                var rpc = ((MemorySocket)peer.m_socket).Routed().Single();
                Check(rpc.m_methodHash == "ShowMessage".GetStableHashCode() && rpc.m_parameters.ReadInt() == (int)MessageHud.MessageType.Center &&
                    rpc.m_parameters.ReadString() == "Restart in five minutes", "Alert uses vanilla global center-screen RPC " + peer.m_uid);
                ((MemorySocket)peer.m_socket).Sent.Clear();
            }
            PumpCommand(plugin, Interaction("1004", "alert", player: "Repeated"));
            Check(((MemorySocket)a.m_socket).Sent.Count == 0, "Replayed interactions do not repeat game actions");
            var zone = go.AddComponent<ZoneSystem>(); Set(typeof(ZoneSystem), null, "s_instance", zone);
            var fixtureHarmony = new Harmony("isolated-chat-probe-spawn");
            fixtureHarmony.Patch(AccessTools.Method(typeof(ZoneSystem), "GetLocationIcon"), prefix: new HarmonyMethod(AccessTools.Method(typeof(ChatBridgeProbe), "SpawnIcon")));
            PumpCommand(plugin, Interaction("1005", "setatspawn"));
            var teleport = ((MemorySocket)b.m_socket).Routed().Single();
            Check(teleport.m_methodHash == "RPC_TeleportPlayer".GetStableHashCode() && teleport.m_targetPeerID == b.m_uid &&
                teleport.m_parameters.ReadVector3() == new Vector3(10, 22, 30) && teleport.m_parameters.ReadQuaternion() == Quaternion.identity &&
                teleport.m_parameters.ReadBool(), "Teleport targets one client with vanilla world-spawn offset and distant loading");
            Check(((MemorySocket)a.m_socket).Sent.Count == 0 && ((MemorySocket)c.m_socket).Sent.Count == 0, "Teleport leaves other players untouched");
            fixtureHarmony.UnpatchSelf();
            string id, problem;
            // Resolve authenticated IDs through the production method, never the forged chat author.
            var resolveArgs = new object[] { "Viking Two", true, null, null };
            var resolved = Call(plugin, "FindPlayer", resolveArgs);
            id = (string)resolveArgs[2]; problem = (string)resolveArgs[3];
            Check(ReferenceEquals(resolved, b) && id == "76561198000000022" && problem == null, "Player name resolves authenticated Steam ID");
            b.m_playerName = a.m_playerName;
            resolveArgs = new object[] { a.m_playerName, true, null, null }; resolved = Call(plugin, "FindPlayer", resolveArgs);
            Check(resolved == null && ((string)resolveArgs[3]).Contains("ambiguous"), "Duplicate player names refuse moderation"); b.m_playerName = "Viking Two";
            ((MemorySocket)b.m_socket).SteamId = "Xbox_123456789";
            resolveArgs = new object[] { b.m_playerName, true, null, null }; resolved = Call(plugin, "FindPlayer", resolveArgs);
            Check(resolved == null && ((string)resolveArgs[3]).Contains("no Steam ID"), "Non-Steam players cannot be banned by name instead of authenticated Steam ID");
            ((MemorySocket)b.m_socket).SteamId = "76561198000000022";
            Set(typeof(ZNet), null, "m_onlineBackend", OnlineBackendType.Steamworks);
            ((MemorySocket)b.m_socket).Sent.Clear();
            PumpCommand(plugin, Interaction("1006", "kick"));
            var kicked = new ZPackage(((MemorySocket)b.m_socket).Sent.Single());
            Check(kicked.ReadInt() == "Kicked".GetStableHashCode(), "Kick uses native Valheim authenticated Steam ID lookup and Kicked RPC");
            var banFile = Path.Combine(root, "isolated-bepinex", "banned-probe-" + System.Guid.NewGuid().ToString("N") + ".txt");
            Set(typeof(ZNet), network, "m_bannedList", new SyncedList(new FileHelpers.FileLocation(FileHelpers.FileSource.Local, banFile), "isolated probe"));
            PumpCommand(plugin, Interaction("1007", "ban"));
            Check(File.ReadAllLines(banFile).Contains("76561198000000022"), "Ban persists Steam ID through Valheim's native ban list");
            PumpCommand(plugin, Interaction("1008", "stats")); PumpCommand(plugin, Interaction("1009", "online"));
            var statsReply = http.Bodies.Select(JObject.Parse).Last(s => (string)s["embeds"]?.First?["title"] == "Valheim Server Stats");
            var statsFields = (JArray)statsReply["embeds"][0]["fields"];
            Check((string)statsReply["content"] == "" && statsFields.Any(f => (string)f["name"] == "Recorded deaths" && (string)f["value"] == "1") &&
                statsFields.Any(f => (string)f["name"] == "Players online" && (string)f["value"] == "3"), "Stats reply embeds observed deaths and online count in separate fields");
            var onlineReply = http.Bodies.Select(JObject.Parse).Last(s => (string)s["embeds"]?.First?["title"] == "Online Players");
            Check(((JArray)onlineReply["embeds"][0]["fields"]).Count == 3 &&
                onlineReply["embeds"][0]["fields"].Any(f => (string)f["name"] == "Viking Two" && ((string)f["value"]).StartsWith("Online for ")),
                "Online reply embeds each player name and observed session duration");
            Check(http.Bodies.Where(s => s.Contains("allowed_mentions")).All(s => ((JArray)JObject.Parse(s)["allowed_mentions"]["parse"]).Count == 0), "Command result replies suppress Discord mentions");
            Set(typeof(DiscordAPI), null, "Rest", rest);
            registered.Clear();
            for (int attempts = 0; attempts < 100 && registered.Count < 6; attempts++)
            { Call(plugin, "RegisterCommands"); Thread.Sleep(5); }
            Check(registered.Count == 6 && registered["setatspawn"] == "setatspawn1", "All six guild commands register through production Discord REST");
            Check(http.Routes.Count(s => s == "/api/v10/applications/777/guilds/111/commands") == 6 &&
                !http.Methods.Contains("PUT") && !http.Methods.Contains("DELETE"), "Command upserts preserve unrelated commands");
            // Reproduce vanilla's local-only chat route before testing the second-recipient path.
            // This is an actual client ZRoutedRpc transport, not an injected incoming chat packet.
            var clientSocket = new MemorySocket { SteamId = "76561198000000100" };
            var clientRouter = new ZRoutedRpc(false); clientRouter.SetUID(a.m_uid);
            clientRouter.AddPeer(new ZNetPeer(clientSocket, true) { m_uid = 100 });
            var clientUser = new UserInfo { Name = a.m_playerName, UserId = new PlatformUserID("Steam_76561198000000011") };
            int beforeChat = outgoing.Count;
            clientRouter.InvokeRoutedRPC(a.m_uid, "ChatMessage", Vector3.zero, (int)Talker.Type.Shout, clientUser, "Solo routing proof");
            Check(clientSocket.Sent.Count == 0 && outgoing.Count == beforeChat, "Vanilla chat addressed only to the local player sends no server packet");
            clientRouter.InvokeRoutedRPC(b.m_uid, "ChatMessage", Vector3.zero, (int)Talker.Type.Shout, clientUser, "Multiplayer routing proof");
            Set(typeof(ZRoutedRpc), null, "s_instance", router);
            var wire = new ZPackage(clientSocket.Sent.Single()); wire.ReadInt(); var forwarded = wire.ReadPackage();
            Call(router, "RPC_RoutedRPC", a.m_rpc, forwarded);
            Check(outgoing.Count == beforeChat + 1 && outgoing.Last() == "[Valheim] Viking One: Multiplayer routing proof",
                "Vanilla chat addressed to a second player crosses the server and reaches the Discord relay");
            // Exercise the production outgoing roster hook and the native client parser and
            // permission loop, rather than inventing a second recipient inside the test.
            var realRoster = network.GetPlayerList();
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 3,
                "Experimental roster leaves a multiplayer packet unchanged");
            var soloPlayer = realRoster[0];
            soloPlayer.m_publicPosition = true; soloPlayer.m_position = new Vector3(12, 34, 56);
            network.GetPeers().Remove(b); network.GetPeers().Remove(c);
            realRoster.Clear(); realRoster.Add(soloPlayer);
            var soloPacket = (ZPackage)Call(network, "WritePlayerInfo", realRoster);
            Check(new ZPackage(soloPacket.GetArray()).ReadInt() == 2 && network.GetNrOfPlayers() == 1 && network.GetPeers().Count == 1,
                "Production roster hook adds one outgoing row without adding a real server player or peer");
            var soloSetting = (BepInEx.Configuration.ConfigEntry<bool>)Field(plugin, "soloChatRelay");
            soloSetting.Value = false;
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 1,
                "SoloChatRelay=false restores the native solo packet");
            soloSetting.Value = true;
            Set(typeof(Plugin), plugin, "ready", false);
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 1,
                "Disconnected Discord does not advertise a chat recipient");
            Set(typeof(Plugin), plugin, "ready", true);
            a.m_characterID = ZDOID.None;
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 1,
                "Loading players without a character do not receive the experimental row");
            a.m_characterID = soloPlayer.m_characterID;
            network.GetPeers().Add(b); // Another ready connection counts even before its row updates.
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 1,
                "A second ready connection removes DiscordBot before its roster row arrives");
            network.GetPeers().Remove(b);
            var clientObject = new GameObject("isolated solo client"); clientObject.SetActive(false);
            var clientNetwork = clientObject.AddComponent<ZNet>();
            var platformManager = clientObject.AddComponent<PlatformManager>();
            var platform = new MemoryPlatform { Identity = soloPlayer.m_userInfo.m_id };
            Set(typeof(PlatformManager), null, "s_instance", platformManager);
            Set(typeof(PlatformManager), platformManager, "m_distributionPlatform", platform);
            Set(typeof(ZNet), clientNetwork, "m_characterID", a.m_characterID);
            Set(typeof(ZNet), clientNetwork, "m_world", new World());
            Set(typeof(ZNet), null, "m_instance", clientNetwork); Set(typeof(ZNet), null, "m_isServer", false);
            Call(clientNetwork, "RPC_PlayerList", a.m_rpc, new ZPackage(soloPacket.GetArray()));
            var decoded = clientNetwork.GetPlayerList();
            var phantom = decoded[1];
            Check(decoded.Count == 2 && decoded[0].m_name == a.m_playerName && phantom.m_name == "DiscordBot" &&
                decoded[0].m_publicPosition && decoded[0].m_position == soloPlayer.m_position &&
                !phantom.m_characterID.IsNone() && phantom.m_characterID != a.m_characterID && !phantom.m_publicPosition,
                "Native client parser receives DiscordBot after the real player with no public map position");
            Check(phantom.m_userInfo == decoded[0].m_userInfo && phantom.m_characterID.UserID != a.m_uid,
                "DiscordBot copies the complete local account while using a different network recipient");
            ZNet.PlayerInfo found;
            Check(ZNet.TryGetPlayerByPlatformUserID(platform.Identity, out found) && found.m_name == a.m_playerName,
                "Native and BetterChat name lookup still resolves the real player first");
            Call(clientNetwork, "UpdatePlayerHistory");
            Check(clientNetwork.GetWorld().m_playerHistory.Count == 1 &&
                ((List<PlatformUserID>)Field(clientNetwork, "m_recentPlayers")).All(recentId => recentId == platform.Identity),
                "Native history deduplicates the copied account and introduces no extra recent-player identity");
            Check(clientNetwork.GetNrOfPlayers() == 2, "Client count includes the accepted synthetic row");
            Set(typeof(ZRoutedRpc), null, "s_instance", clientRouter);
            clientSocket.Sent.Clear();
            var recipients = new List<long>();
            Chat.CheckPermissionsAndSendChatMessageRPCsAsync((user, filter) =>
            {
                recipients.Add(user);
                Check(!filter, "Native solo recipient self-account permission preserves unfiltered text");
                clientRouter.InvokeRoutedRPC(user, "ChatMessage", Vector3.zero, (int)Talker.Type.Shout, clientUser, "Experimental solo shout");
            });
            Check(recipients.SequenceEqual(new[] { a.m_uid, phantom.m_characterID.UserID }) && clientSocket.Sent.Count == 1,
                "Native chat permission loop sends exactly one solo shout to the real server without a platform profile lookup");
            platform.TextPrivilege = PrivilegeResult.DeniedUnknown;
            Chat.CheckPermissionsAndSendChatMessageRPCsAsync((user, filter) => { throw new Exception("Text privilege denial was bypassed"); });
            Check(clientSocket.Sent.Count == 1, "Experimental roster preserves native text privilege denial");
            platform.TextPrivilege = PrivilegeResult.Granted;
            foreach (var type in new[] { Talker.Type.Normal, Talker.Type.Whisper })
                Chat.CheckPermissionsAndSendChatMessageRPCsAsync((user, filter) =>
                {
                    if (user != a.m_uid) clientRouter.InvokeRoutedRPC(user, a.m_characterID, "Say", (int)type, clientUser, "Experimental solo " + type);
                });
            Set(typeof(ZNet), null, "m_instance", network); Set(typeof(ZNet), null, "m_isServer", true);
            Set(typeof(ZRoutedRpc), null, "s_instance", router);
            int publicBefore = outgoing.Count, adminBefore = admin.Count;
            foreach (var bytes in clientSocket.Sent)
            {
                var envelope = new ZPackage(bytes); envelope.ReadInt();
                Call(router, "RPC_RoutedRPC", a.m_rpc, envelope.ReadPackage());
            }
            Check(outgoing.Count == publicBefore + 1 && outgoing.Last() == "[Valheim] Viking One: Experimental solo shout" &&
                admin.Count == adminBefore + 3 && admin.Last().Contains("Experimental solo Whisper"),
                "Experimental solo packets reach production Discord queues: public shout only, all three types in admin");
            Call(stats, "Tick", network);
            Check(network.GetNrOfPlayers() == 1 && ((System.Collections.IDictionary)Field(stats, "Sessions")).Count == 1 &&
                !JObject.Parse(File.ReadAllText(statsFile))["Players"].ToString().Contains("DiscordBot"),
                "Server statistics and persistent accounts contain only real players");
            PumpCommand(plugin, Interaction("311", "stats")); PumpCommand(plugin, Interaction("312", "online"));
            var soloStatsReply = http.Bodies.Select(JObject.Parse).Last(s => (string)s["embeds"]?.First?["title"] == "Valheim Server Stats");
            var soloOnlineReply = http.Bodies.Select(JObject.Parse).Last(s => (string)s["embeds"]?.First?["title"] == "Online Players");
            Check(soloStatsReply["embeds"][0]["fields"].Any(f => (string)f["name"] == "Players online" && (string)f["value"] == "1") &&
                ((JArray)soloOnlineReply["embeds"][0]["fields"]).Count == 1 && !soloOnlineReply.ToString().Contains("DiscordBot"),
                "Actual stats and online command embeds exclude DiscordBot");
            network.GetPeers().Clear(); realRoster.Clear();
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 0,
                "An empty server advertises no DiscordBot");
            network.GetPeers().Add(a); realRoster.Add(soloPlayer);
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 2,
                "A returning solo player receives DiscordBot again");
            network.GetPeers().Add(b);
            var secondPlayer = soloPlayer; secondPlayer.m_name = b.m_playerName; secondPlayer.m_characterID = b.m_characterID;
            secondPlayer.m_userInfo.m_id = new PlatformUserID("Steam_76561198000000022"); realRoster.Add(secondPlayer);
            Check(new ZPackage(((ZPackage)Call(network, "WritePlayerInfo", realRoster)).GetArray()).ReadInt() == 2 &&
                realRoster.All(p => p.m_name != "DiscordBot"), "Two real players replace the synthetic recipient on the next native roster update");
            Call(plugin, "StopBridge");
            Check(outgoing.Count == 0 && incoming.Count == 0, "Shutdown clears pending relay messages");
        }
        catch (Exception exception) { error = exception.ToString(); }
        File.WriteAllText(Path.Combine(root, "mono-result.json"), JsonConvert.SerializeObject(new { version = global::Version.CurrentVersion.ToString(), error, checks = Checks }, Formatting.Indented));
        Debug.Log("Chat bridge Mono probe: " + Checks.Count + " checks; " + (error.Length == 0 ? "PASS" : error));
        Application.Quit(error.Length == 0 ? 0 : 1);
    }
}
