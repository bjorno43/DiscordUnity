using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        internal const string API = "https://discord.com/api/v10";

        private static Task<RestResult<T>> Http<T>(HttpMethod method, string endpoint, object obj = null,
            object query = null, string auditReason = null, bool authenticate = true, Func<HttpContent> contentFactory = null)
        {
            var current = Rest;
            return current == null
                ? Task.FromResult(RestResult<T>.FromException(new InvalidOperationException("Discord bot is not active.")))
                : current.Http<T>(method, endpoint, obj, query, auditReason, authenticate, contentFactory);
        }

        private static Task<RestResult<T>> Get<T>(string endpoint, object query = null) => Http<T>(HttpMethod.Get, endpoint, null, query);
        private static Task<RestResult<T>> Patch<T>(string endpoint, object obj, object query = null) => Http<T>(new HttpMethod("PATCH"), endpoint, obj, query);
        private static Task<RestResult<T>> Post<T>(string endpoint, object obj, object query = null) => Http<T>(HttpMethod.Post, endpoint, obj, query);
        private static Task<RestResult<T>> Put<T>(string endpoint, object obj, object query = null) => Http<T>(HttpMethod.Put, endpoint, obj, query);
        private static Task<RestResult<T>> Delete<T>(string endpoint, object query = null) => Http<T>(HttpMethod.Delete, endpoint, null, query);

        private static async Task<RestResult<R>> SyncInherit<T, R>(Task<RestResult<T>> call, Func<T, R> transform)
        {
            var current = Rest;
            var task = new TaskCompletionSource<RestResult<R>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancel = current == null ? CancellationToken.None : current.Cancellation;
            using (cancel.Register(() => task.TrySetResult(RestResult<R>.FromException(new OperationCanceledException()))))
            {
                var result = await call.ConfigureAwait(false);
                if (!result) return RestResult<R>.FromException(result.Exception);
                Sync(() =>
                {
                    if (task.Task.IsCompleted) return;
                    try { task.TrySetResult(RestResult<R>.FromResult(transform(result.Data))); }
                    catch (Exception exception) { task.TrySetResult(RestResult<R>.FromException(exception)); }
                });
                return await task.Task.ConfigureAwait(false);
            }
        }
    }

    internal sealed class RestClient : IDisposable
    {
        private readonly HttpClient client;
        private readonly string token;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        // Conservative serialization is appropriate for an embedded game bot. It also makes shared
        // bucket/global deadlines atomic without blocking the Unity thread.
        private readonly SemaphoreSlim requestLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, string> buckets = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> deadlines = new Dictionary<string, DateTime>();
        private DateTime globalDeadline;
        internal CancellationToken Cancellation { get; }

        internal RestClient(HttpClient client, string token)
        {
            this.client = client;
            this.token = token;
            Cancellation = cancellation.Token;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/bjorno43/DiscordUnity, 2.0.0)");
        }

        internal void Cancel() => cancellation.Cancel();
        public void Dispose()
        {
            client.Dispose();
            // In-flight requests may still be releasing requestLock or observing Cancellation.
            // Do not dispose these synchronization objects until their references are collected.
        }

        internal static string Query(object query)
        {
            if (query == null) return "";
            return string.Join("&", JObject.FromObject(query, DiscordAPI.JsonSerializer).Properties()
                .Where(p => p.Value.Type != JTokenType.Null)
                .Select(p => Uri.EscapeDataString(p.Name) + "=" + Uri.EscapeDataString(
                    p.Value.Type == JTokenType.Boolean ? p.Value.ToString().ToLowerInvariant() : p.Value.ToString())));
        }

        private static string Route(HttpMethod method, string endpoint, out string major)
        {
            var parts = endpoint.Split('/');
            major = parts.Length > 2 && (parts[1] == "channels" || parts[1] == "guilds" || parts[1] == "webhooks")
                ? parts[1] + "/" + parts[2] : "";
            if (parts.Length > 3 && parts[1] == "webhooks") major += "/" + parts[3];
            for (int i = 2; i < parts.Length; i++)
                if (ulong.TryParse(parts[i], out _) || (parts[1] == "interactions" && i == 3) || (parts[1] == "webhooks" && i == 3)) parts[i] = ":id";
            return method.Method + " " + string.Join("/", parts);
        }

        private static string Header(HttpResponseMessage response, string name)
            => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

        private static double? Number(string value)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number)
                ? (double?)number : null;

        private static DateTime Deadline(double seconds) => DateTime.UtcNow.AddSeconds(Math.Max(0, seconds));

        private async Task WaitUntil(DateTime deadline)
        {
            while (deadline > DateTime.UtcNow)
            {
                var milliseconds = Math.Min((deadline - DateTime.UtcNow).TotalMilliseconds, int.MaxValue - 1);
                if (milliseconds > 0) await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), Cancellation).ConfigureAwait(false);
            }
        }

        internal async Task<RestResult<T>> Http<T>(HttpMethod method, string endpoint, object obj = null,
            object query = null, string auditReason = null, bool authenticate = true, Func<HttpContent> contentFactory = null)
        {
            bool locked = false;
            try
            {
                var q = Query(query);
                var route = Route(method, endpoint, out var major) + " " + major;
                await requestLock.WaitAsync(Cancellation).ConfigureAwait(false);
                locked = true;
                string json = obj == null ? null : JsonConvert.SerializeObject(obj, DiscordAPI.JsonSettings);
                for (int attempt = 0; ; attempt++)
                {
                    await WaitUntil(globalDeadline).ConfigureAwait(false);
                    var key = buckets.TryGetValue(route, out var bucket) ? bucket + " " + major : route;
                    if (deadlines.TryGetValue(key, out var deadline)) await WaitUntil(deadline).ConfigureAwait(false);
                    using (var request = new HttpRequestMessage(method, DiscordAPI.API + endpoint + (q.Length == 0 ? "" : "?" + q)))
                    {
                        if (authenticate) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bot", token);
                        if (!string.IsNullOrEmpty(auditReason)) request.Headers.Add("X-Audit-Log-Reason", Uri.EscapeDataString(auditReason));
                        request.Content = contentFactory != null ? contentFactory() : json == null ? null : new StringContent(json, Encoding.UTF8, "application/json");
                        using (var response = await client.SendAsync(request, Cancellation).ConfigureAwait(false))
                        {
                            string body = response.Content == null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            string receivedBucket = Header(response, "X-RateLimit-Bucket");
                            if (receivedBucket != null)
                            {
                                buckets[route] = receivedBucket;
                                key = receivedBucket + " " + major;
                            }
                            var resetAfter = Number(Header(response, "X-RateLimit-Reset-After"));
                            if (Header(response, "X-RateLimit-Remaining") == "0" && resetAfter.HasValue) deadlines[key] = Deadline(resetAfter.Value);
                            if ((int)response.StatusCode == 429)
                            {
                                JObject rate = null;
                                try { rate = JObject.Parse(body); } catch (JsonException) { }
                                double seconds = Number(rate?["retry_after"]?.ToString()) ?? Number(Header(response, "Retry-After")) ?? 1;
                                bool global = (bool?)rate?["global"] == true || string.Equals(Header(response, "X-RateLimit-Global"), "true", StringComparison.OrdinalIgnoreCase);
                                var retry = Deadline(Math.Max(0.001, seconds));
                                if (global) globalDeadline = retry;
                                else deadlines[key] = retry;
                                if (attempt < 5) continue;
                            }
                            if (!response.IsSuccessStatusCode)
                                return RestResult<T>.FromException(new DiscordHttpException((int)response.StatusCode, body));
                            // Discord's many 204 endpoints have no JSON body.
                            return RestResult<T>.FromResult(string.IsNullOrWhiteSpace(body) ? default(T) : JsonConvert.DeserializeObject<T>(body, DiscordAPI.JsonSettings));
                        }
                    }
                }
            }
            catch (Exception exception) { return RestResult<T>.FromException(exception); }
            finally { if (locked) requestLock.Release(); }
        }
    }

    public sealed class DiscordHttpException : Exception
    {
        public int StatusCode { get; }
        public int? DiscordCode { get; }
        public string ResponseBody { get; }
        internal DiscordHttpException(int statusCode, string responseBody) : base("Discord HTTP request failed (" + statusCode + ").")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            try { DiscordCode = (int?)JObject.Parse(responseBody)["code"]; } catch (JsonException) { }
        }
    }

    public class RestResult<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public Exception Exception { get; set; }
        private RestResult() { }
        internal static RestResult<T> FromResult(T data) => new RestResult<T> { Success = true, Data = data };
        internal static RestResult<T> FromException(Exception exception) => new RestResult<T> { Success = false, Exception = exception };
        public static implicit operator bool(RestResult<T> result) => result != null && result.Success;
    }
}
