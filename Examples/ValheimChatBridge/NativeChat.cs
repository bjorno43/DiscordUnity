using HarmonyLib;
using System;

namespace ValheimDiscordChat
{
    [HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
    internal static class IncomingRpcContext
    {
        [ThreadStatic] internal static ZRpc Source;
        private static void Prefix(ZRpc rpc, out ZRpc __state)
        {
            __state = Source;
            Source = Plugin.Instance != null && Plugin.Instance.CanObserveGame ? rpc : null;
        }
        private static Exception Finalizer(Exception __exception, ZRpc __state)
        {
            Source = __state;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(ZRoutedRpc.RoutedRPCData), nameof(ZRoutedRpc.RoutedRPCData.Deserialize))]
    internal static class CaptureChatPacket
    {
        private static void Postfix(ZRoutedRpc.RoutedRPCData __instance)
        {
            var source = IncomingRpcContext.Source;
            if (source != null) Plugin.Instance?.Capture(source, __instance);
        }
    }
}
