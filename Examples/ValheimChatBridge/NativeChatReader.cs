using System;

namespace ValheimDiscordChat
{
    internal static class NativeChatReader
    {
        private static readonly int ChatHash = "ChatMessage".GetStableHashCode();
        private static readonly int SayHash = "Say".GetStableHashCode();

        internal static bool TryRead(ZRoutedRpc.RoutedRPCData data, long authenticatedSender,
            ZDOID playerCharacter, out int type, out string text)
        {
            type = -1; text = null;
            if (data == null || data.m_senderPeerID != authenticatedSender || authenticatedSender == 0 ||
                data.m_parameters == null || data.m_parameters.Size() > 16384) return false;
            bool global = data.m_methodHash == ChatHash && data.m_targetZDO.IsNone();
            bool local = data.m_methodHash == SayHash && !playerCharacter.IsNone() && data.m_targetZDO == playerCharacter;
            if (!global && !local) return false;
            try
            {
                // Never advance the package that vanilla and other mods will consume.
                var parameters = new ZPackage(data.m_parameters.GetArray());
                if (global) parameters.ReadVector3();
                type = parameters.ReadInt();
                if (type < (int)Talker.Type.Whisper || type > (int)Talker.Type.Shout) return false;
                parameters.ReadString(); // UserInfo.Name is untrusted; use the server's peer name.
                parameters.ReadString(); // UserInfo.UserId
                text = parameters.ReadString();
                return !string.IsNullOrWhiteSpace(text) && text.Length <= 4096;
            }
            catch (Exception) { return false; }
        }

        internal static bool IsDeath(ZRoutedRpc.RoutedRPCData data, long sender, ZDOID character)
            => data != null && sender != 0 && data.m_senderPeerID == sender && !character.IsNone() &&
                data.m_targetZDO == character && data.m_targetPeerID == ZRoutedRpc.Everybody &&
                data.m_methodHash == "OnDeath".GetStableHashCode() &&
                data.m_parameters != null && data.m_parameters.Size() == 0;
    }

}
