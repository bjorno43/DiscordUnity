using DiscordUnity.Models;

namespace DiscordUnity.State
{
    public class DiscordVoiceState
    {
        public DiscordServer Server => DiscordAPI.FindServer(GuildId);
        public DiscordChannel Channel => DiscordAPI.FindChannel(GuildId, ChannelId);
        public DiscordUser User { get; internal set; }
        public DiscordServerMember Member { get; internal set; }
        public string SessionId { get; internal set; }
        public bool Deaf { get; internal set; }
        public bool Mute { get; internal set; }
        public bool SelfDeaf { get; internal set; }
        public bool SelfMute { get; internal set; }
        public bool? SelfStream { get; internal set; }
        public bool Suppress { get; internal set; }

        private readonly string GuildId;
        private readonly string ChannelId;

        internal DiscordVoiceState(VoiceStateModel model)
        {
            GuildId = model.GuildId;
            ChannelId = model.ChannelId;
            if (model.Member != null)
            {
                model.Member.GuildId = model.GuildId;
                Member = new DiscordServerMember(model.Member);
            }
            User = Member?.User ?? new DiscordUser(new UserModel { Id = model.UserId });
            SessionId = model.SessionId;
            Deaf = model.Deaf;
            Mute = model.Mute;
            SelfDeaf = model.SelfDeaf;
            SelfMute = model.SelfMute;
            SelfStream = model.SelfStream;
            Suppress = model.Suppress;
        }
    }
}
