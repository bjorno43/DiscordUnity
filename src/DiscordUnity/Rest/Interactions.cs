using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        public static event Action<DiscordInteraction> InteractionCreated;

        private static string CommandsRoute(string applicationId, string guildId)
            => $"/applications/{applicationId}" + (guildId == null ? "" : $"/guilds/{guildId}") + "/commands";

        public static Task<RestResult<JObject[]>> GetApplicationCommands(string applicationId, string guildId = null)
            => Get<JObject[]>(CommandsRoute(applicationId, guildId));

        public static Task<RestResult<JObject>> CreateApplicationCommand(string applicationId, object command, string guildId = null)
            => Post<JObject>(CommandsRoute(applicationId, guildId), command);

        public static Task<RestResult<JObject>> EditApplicationCommand(string applicationId, string commandId, object command, string guildId = null)
            => Patch<JObject>(CommandsRoute(applicationId, guildId) + "/" + commandId, command);

        public static async Task<RestResult<bool>> DeleteApplicationCommand(string applicationId, string commandId, string guildId = null)
            => Success(await Delete<object>(CommandsRoute(applicationId, guildId) + "/" + commandId).ConfigureAwait(false));

        public static Task<RestResult<JObject[]>> OverwriteApplicationCommands(string applicationId, object[] commands, string guildId = null)
            => Put<JObject[]>(CommandsRoute(applicationId, guildId), commands);

        // Interaction acknowledgements must bypass ordinary REST requests waiting on a global limit.
        // Token endpoints are unauthenticated and not bound to the bot's global rate limit.
        private static Task<RestResult<T>> InteractionHttp<T>(HttpMethod method, string endpoint, object body)
        {
            var current = InteractionRest;
            return current == null ? Task.FromResult(RestResult<T>.FromException(new InvalidOperationException("Discord bot is not active.")))
                : current.Http<T>(method, endpoint, body, authenticate: false);
        }

        public static async Task<RestResult<bool>> CreateInteractionResponse(string interactionId, string interactionToken, int type, object data = null)
            => Success(await InteractionHttp<object>(HttpMethod.Post, $"/interactions/{interactionId}/{Uri.EscapeDataString(interactionToken)}/callback", new { type, data }).ConfigureAwait(false));

        public static Task<RestResult<JObject>> EditOriginalInteractionResponse(string applicationId, string interactionToken, object data)
            => InteractionHttp<JObject>(new HttpMethod("PATCH"), $"/webhooks/{applicationId}/{Uri.EscapeDataString(interactionToken)}/messages/@original", data);

        public static Task<RestResult<JObject>> CreateInteractionFollowup(string applicationId, string interactionToken, object data)
            => InteractionHttp<JObject>(HttpMethod.Post, $"/webhooks/{applicationId}/{Uri.EscapeDataString(interactionToken)}", data);

        private static RestResult<bool> Success(RestResult<object> result)
            => result ? RestResult<bool>.FromResult(true) : RestResult<bool>.FromException(result.Exception);
    }

    public sealed class DiscordInteraction
    {
        public string Id { get; }
        public string ApplicationId { get; }
        public string GuildId { get; }
        public string ChannelId { get; }
        public int Type { get; }
        public JObject Data { get; }
        public JObject Member { get; }
        public string UserId { get; }
        private readonly string token;

        internal DiscordInteraction(JObject payload)
        {
            Id = (string)payload["id"];
            ApplicationId = (string)payload["application_id"];
            GuildId = (string)payload["guild_id"];
            ChannelId = (string)payload["channel_id"];
            Type = (int)payload["type"];
            Data = payload["data"] as JObject;
            Member = payload["member"] as JObject;
            UserId = (string)(Member?["user"]?["id"] ?? payload["user"]?["id"]);
            token = (string)payload["token"];
        }

        /// <summary>Send the initial reply within 3 seconds of receiving the interaction.</summary>
        public Task<RestResult<bool>> Respond(string content, bool ephemeral = false)
            => DiscordAPI.CreateInteractionResponse(Id, token, 4, new { content, flags = ephemeral ? 64 : 0 });

        /// <summary>Acknowledge within 3 seconds, then finish via EditOriginalResponse.</summary>
        public Task<RestResult<bool>> Defer(bool ephemeral = false)
            => DiscordAPI.CreateInteractionResponse(Id, token, 5, new { flags = ephemeral ? 64 : 0 });

        public Task<RestResult<JObject>> EditOriginalResponse(string content)
            => DiscordAPI.EditOriginalInteractionResponse(ApplicationId, token, new { content });

        /// <summary>Edit a deferred reply, including allowed_mentions and other message fields.</summary>
        public Task<RestResult<JObject>> EditOriginalResponse(object data)
            => DiscordAPI.EditOriginalInteractionResponse(ApplicationId, token, data);
    }
}
