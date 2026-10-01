using System;
using System.Linq;
using UnityEngine;
using ValheimDiscordChat;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string name)
    {
        assertions++;
        if (!value) throw new Exception(name);
    }
    private static ZRoutedRpc.RoutedRPCData Packet(string method, int type, ZDOID target)
    {
        var data = new ZRoutedRpc.RoutedRPCData
        {
            m_msgID = 123, m_senderPeerID = 10, m_targetPeerID = 20,
            m_targetZDO = target, m_methodHash = method.GetStableHashCode()
        };
        var user = new UserInfo { Name = "Forged Name", UserId = new Splatform.PlatformUserID("Steam_76561198000000000") };
        var arguments = method == "ChatMessage"
            ? new object[] { new Vector3(1, 2, 3), type, user, "Hello Discord" }
            : new object[] { type, user, "Hello Discord" };
        ZRpc.Serialize(arguments, ref data.m_parameters);
        data.m_parameters.SetPos(0);
        // Exercise the exact game serialization/deserialization used by the live RPC.
        var wire = new ZPackage();
        data.Serialize(wire); wire.SetPos(0);
        var received = new ZRoutedRpc.RoutedRPCData(); received.Deserialize(wire);
        return received;
    }
    public static int Main()
    {
        try
        {
            int type; string text;
            var character = new ZDOID(10, 1);
            var shout = Packet("ChatMessage", 2, ZDOID.None);
            var before = shout.m_parameters.GetArray();
            Check(NativeChatReader.TryRead(shout, 10, character, out type, out text) && type == 2 && text == "Hello Discord", "native shout");
            Check(before.SequenceEqual(shout.m_parameters.GetArray()) && shout.m_parameters.ReadVector3() == new Vector3(1, 2, 3), "vanilla bytes and read position preserved");
            Check(!NativeChatReader.TryRead(shout, 99, character, out type, out text), "forged routed sender rejected");
            Check(NativeChatReader.TryRead(Packet("Say", 1, character), 10, character, out type, out text) && type == 1, "normal chat available to admin log");
            Check(NativeChatReader.TryRead(Packet("Say", 0, character), 10, character, out type, out text) && type == 0, "whispers available to admin log");
            Check(!NativeChatReader.TryRead(Packet("ChatMessage", 3, ZDOID.None), 10, character, out type, out text), "map pings excluded");
            Check(NativeChatReader.TryRead(Packet("Say", 2, character), 10, character, out type, out text), "player-object shout supported");
            Check(!NativeChatReader.TryRead(Packet("Say", 2, new ZDOID(99, 1)), 10, character, out type, out text), "other player's object rejected");
            Check(!NativeChatReader.TryRead(Packet("ChatMessage", 2, character), 10, character, out type, out text), "incorrect global object rejected");
            Check(!NativeChatReader.TryRead(Packet("Unrelated", 2, ZDOID.None), 10, character, out type, out text), "other RPC ignored");
            var malformed = Packet("ChatMessage", 2, ZDOID.None); malformed.m_parameters = new ZPackage(new byte[] { 1 });
            Check(!NativeChatReader.TryRead(malformed, 10, character, out type, out text), "truncated packet tolerated");
            malformed.m_parameters = new ZPackage(new byte[16385]);
            Check(!NativeChatReader.TryRead(malformed, 10, character, out type, out text), "oversized packet ignored");
            Check(!NativeChatReader.TryRead(null, 10, character, out type, out text), "null packet tolerated");
            System.Console.WriteLine("PASS native Valheim chat packets: " + assertions + " assertions.");
            return 0;
        }
        catch (Exception exception) { System.Console.Error.WriteLine(exception); return 1; }
    }
}
