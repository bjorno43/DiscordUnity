using DiscordUnity.Models;
using DiscordUnity.State;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        public static Task<RestResult<DiscordPinsPage>> GetChannelPins(string channelId, DateTimeOffset? before = null, int? limit = null)
            => SyncInherit(Get<PinsPageModel>($"/channels/{channelId}/messages/pins", new { before = before?.ToString("o", CultureInfo.InvariantCulture), limit }),
                page => new DiscordPinsPage
                {
                    HasMore = page.HasMore,
                    Items = (page.Items ?? new PinModel[0]).Select(pin => new DiscordMessagePin
                    {
                        PinnedAt = pin.PinnedAt, Message = new DiscordMessage(pin.Message)
                    }).ToArray()
                });

        private static async Task<RestResult<DiscordMessage[]>> GetAllPinnedMessages(string channelId)
        {
            DateTimeOffset? before = null;
            var messages = new List<DiscordMessage>();
            while (true)
            {
                var result = await GetChannelPins(channelId, before).ConfigureAwait(false);
                if (!result) return RestResult<DiscordMessage[]>.FromException(result.Exception);
                messages.AddRange(result.Data.Items.Select(pin => pin.Message));
                if (!result.Data.HasMore) return RestResult<DiscordMessage[]>.FromResult(messages.ToArray());
                if (result.Data.Items.Length == 0 || result.Data.Items.Last().PinnedAt == before)
                    return RestResult<DiscordMessage[]>.FromException(new InvalidOperationException("Pin pagination did not advance."));
                before = result.Data.Items.Last().PinnedAt;
            }
        }
    }

    public sealed class DiscordPinsPage
    {
        public DiscordMessagePin[] Items { get; internal set; }
        public bool HasMore { get; internal set; }
    }

    public sealed class DiscordMessagePin
    {
        public DateTimeOffset PinnedAt { get; internal set; }
        public DiscordMessage Message { get; internal set; }
    }

    internal sealed class PinsPageModel
    {
        public PinModel[] Items { get; set; }
        public bool HasMore { get; set; }
    }

    internal sealed class PinModel
    {
        public DateTimeOffset PinnedAt { get; set; }
        public MessageModel Message { get; set; }
    }
}
