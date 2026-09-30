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
    private sealed class MemorySocket : ISocket
    {
        internal readonly List<byte[]> Sent = new List<byte[]>();
        public bool IsConnected() => true;
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
        public string GetHostName() => "76561198000000000";
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
                network.GetPeers().Add(peer); router.AddPeer(peer);
                var p = default(ZNet.PlayerInfo); p.m_name = peer.m_playerName; p.m_characterID = peer.m_characterID;
                p.m_userInfo.m_id = new PlatformUserID("Steam_765611980000000" + peer.m_uid);
                network.GetPlayerList().Add(p);
            }
            var plugin = go.AddComponent<Plugin>();
            Call(plugin, "Awake"); // Actual BepInEx config binding and production Harmony installation.
            Check(plugin.Config.ContainsKey(new BepInEx.Configuration.ConfigDefinition("Discord", "BotToken")), "BepInEx creates token configuration without a real token");
            Check(network.IsDedicated() && network.IsServer(), "Actual dedicated-server assembly activates the server guard");
            Set(typeof(Plugin), plugin, "network", network); Set(typeof(Plugin), plugin, "ready", true);
            Set(typeof(Plugin), plugin, "ownsConnection", true); Set(typeof(Plugin), plugin, "channelId", "222");
            Set(typeof(Plugin), plugin, "guildId", "111"); Set(typeof(Plugin), plugin, "textLimit", 500);
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
            Call(plugin, "StopBridge");
            Check(outgoing.Count == 0 && incoming.Count == 0, "Shutdown clears pending relay messages");
        }
        catch (Exception exception) { error = exception.ToString(); }
        File.WriteAllText(Path.Combine(root, "mono-result.json"), JsonConvert.SerializeObject(new { version = global::Version.CurrentVersion.ToString(), error, checks = Checks }, Formatting.Indented));
        Debug.Log("Chat bridge Mono probe: " + Checks.Count + " checks; " + (error.Length == 0 ? "PASS" : error));
        Application.Quit(error.Length == 0 ? 0 : 1);
    }
}
