namespace DiscordUnity.Models
{
    internal class UserModel
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string GlobalName { get; set; }
        public string Discriminator { get; set; }
        public string Avatar { get; set; }
        public bool? Bot { get; set; }
        public bool? System { get; set; }
        public bool? MfaEnabled { get; set; }
        public string Locale { get; set; }
        public bool? Verified { get; set; }
        public string Email { get; set; }
        public UserFlags? Flags { get; set; }
        public PremiumType? PremiumType { get; set; }
        public UserFlags? PublicFlags { get; set; }
    }

    [System.Flags]
    public enum UserFlags
    {
        None = 0,
        DiscordEmployee = 1,
        DiscordPartner = 2,
        HypeSquadEvents = 4,
        BugHunterLevel1 = 8,
        HouseBravery = 1 << 6,
        HouseBrilliance = 1 << 7,
        HouseBalance = 1 << 8,
        EarlySupporter = 1 << 9,
        TeamUser = 1 << 10,
        System = 1 << 12,
        BugHunterLevel2 = 1 << 14,
        VerifiedBot = 1 << 16,
        VerifiedBotDeveloper = 1 << 17,
        CertifiedModerator = 1 << 18,
        BotHttpInteractions = 1 << 19
    }
}
