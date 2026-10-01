using HarmonyLib;
using System;
using System.Collections.Generic;

namespace ValheimDiscordChat
{
    public sealed partial class Plugin
    {
        // An unused recipient routes through the real server, where Capture observes it before
        // vanilla discards the packet. It has no socket, character object or server player entry.
        private readonly long soloRecipientId = BitConverter.ToInt64(System.Guid.NewGuid().ToByteArray(), 0) & long.MaxValue;
        private bool soloRosterActive, soloRosterErrorLogged;

        internal ZPackage AddSoloChatRecipient(ZNet current, List<ZNet.PlayerInfo> players, ZPackage original)
        {
            bool eligible = CanRelay && current == network && soloChatRelay.Value && (toDiscord.Value || adminReady) &&
                ReferenceEquals(players, current.GetPlayerList()) && players.Count == 1 && original != null && soloRecipientId != 0 &&
                (ZDOMan.instance == null || soloRecipientId != ZNet.GetUID());
            ZNetPeer recipient = null;
            if (eligible)
            {
                foreach (var peer in current.GetPeers())
                {
                    if (peer.m_uid == soloRecipientId || peer.m_characterID.UserID == soloRecipientId) { eligible = false; break; }
                    if (!peer.IsReady() || !peer.m_rpc.IsConnected()) continue;
                    if (recipient != null) { eligible = false; break; }
                    recipient = peer;
                }
                eligible &= recipient != null && !recipient.m_characterID.IsNone() &&
                    players[0].m_characterID == recipient.m_characterID && players[0].m_userInfo.m_id.IsValid;
            }
            if (!eligible)
            {
                if (soloRosterActive) Logger.LogInfo("DiscordBot solo chat recipient removed from outgoing player lists.");
                soloRosterActive = false;
                return original;
            }
            try
            {
                // Preserve vanilla's real-player bytes, public-position flag and account data.
                // Copying the local account permits text communication without a fake platform
                // identity, keeps history deduplicated, and leaves the real row first for lookups.
                var vanilla = original.GetArray();
                if (vanilla.Length < 4 || new ZPackage(vanilla).ReadInt() != 1) return original;
                var bot = new ZPackage();
                bot.Write("DiscordBot");
                bot.Write(new ZDOID(soloRecipientId, 1));
                var account = players[0].m_userInfo;
                bot.Write(account.m_id.ToString());
                bot.Write(account.m_displayName ?? "");
                bot.Write(account.m_serverAssignedDisplayName ?? "");
                bot.Write(account.m_playfabId ?? "");
                bot.Write(false); // No public map position; no world object is created.
                var extra = bot.GetArray();
                var count = new ZPackage(); count.Write(2);
                var bytes = new byte[vanilla.Length + extra.Length];
                Buffer.BlockCopy(count.GetArray(), 0, bytes, 0, 4);
                Buffer.BlockCopy(vanilla, 4, bytes, 4, vanilla.Length - 4);
                Buffer.BlockCopy(extra, 0, bytes, vanilla.Length, extra.Length);
                if (!soloRosterActive) Logger.LogInfo("Experimental solo chat active: DiscordBot added only to the client's player list. One-per-player drops may increase.");
                soloRosterActive = true;
                return new ZPackage(bytes);
            }
            catch (Exception exception)
            {
                if (!soloRosterErrorLogged) Logger.LogWarning("Solo chat roster could not be extended (" + exception.GetType().Name + "). Vanilla player list retained.");
                soloRosterErrorLogged = true;
                return original;
            }
        }
    }

    // UpdatePlayerList/history and native SendPlayerList still run. Only the serialized
    // outgoing roster is extended; GetPeers/GetPlayerList and server statistics stay real.
    [HarmonyPatch(typeof(ZNet), "WritePlayerInfo")]
    internal static class SoloChatPlayerList
    {
        private static void Postfix(ZNet __instance, List<ZNet.PlayerInfo> playerInfoList, ref ZPackage __result)
        {
            if (Plugin.Instance != null)
                __result = Plugin.Instance.AddSoloChatRecipient(__instance, playerInfoList, __result);
        }
    }
}
