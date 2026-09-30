namespace DiscordUnity.Models
{
    internal class OverwriteModel
    {
        public string Id { get; set; }
        public int Type { get; set; }
        public ulong Allow { get; set; }
        public ulong Deny { get; set; }
    }
}
