using DiscordUnity.API;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnity
{
    public static partial class DiscordAPI
    {
        private static readonly object lifecycleLock = new object();
        private static readonly ConcurrentQueue<Action> callbacks = new ConcurrentQueue<Action>();
        internal static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            MaxDepth = 64
        };
        // JsonSerializer is not thread safe. Each conversion gets its own instance.
        internal static JsonSerializer JsonSerializer => JsonSerializer.Create(JsonSettings);
        internal static readonly DiscordInterfaces interfaces = new DiscordInterfaces();
        internal static Func<HttpClient> HttpClientFactory = () => new HttpClient();
        internal static Func<IGatewaySocket> SocketFactory = () => new GatewaySocket();
        private static GatewayConnection connection;
        internal static GatewayConnection CurrentConnection => connection;
        internal static RestClient Rest;
        internal static RestClient InteractionRest;

        public static bool IsActive { get; private set; }
        public static ILogger Logger { get; set; } = new Logger();
        public static int? LastGatewayCloseCode { get; internal set; }

        public static void RegisterEventsHandler(IDiscordInterface handler) => interfaces.AddEventHandler(handler);
        public static void UnregisterEventsHandler(IDiscordInterface handler) => interfaces.RemoveEventHandler(handler);
        internal static void Sync(Action callback) => callbacks.Enqueue(callback);

        public static Task<bool> StartWithBot(string botToken) => StartWithBot(botToken, new DiscordBotOptions());

        /// <summary>Start one bot connection. Call Update every frame on the Unity main thread.</summary>
        public static async Task<bool> StartWithBot(string botToken, DiscordBotOptions options)
        {
            if (string.IsNullOrWhiteSpace(botToken)) return false;
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            GatewayConnection current;
            lock (lifecycleLock)
            {
                if (IsActive) return false;
                InitializeState();
                LastGatewayCloseCode = null;
                Rest = new RestClient(HttpClientFactory(), botToken.Trim());
                InteractionRest = new RestClient(HttpClientFactory(), botToken.Trim());
                current = new GatewayConnection(botToken.Trim(), options.Copy(), Rest, InteractionRest);
                connection = current;
                IsActive = true;
            }
            current.Start();
            using (var timeout = new CancellationTokenSource())
            {
                var delay = Task.Delay(options.StartupTimeout, timeout.Token);
                if (await Task.WhenAny(current.Ready.Task, delay).ConfigureAwait(false) != current.Ready.Task)
                {
                    Logger.LogError("Discord startup timed out. Ensure DiscordAPI.Update() is called each frame.");
                    StopConnection(current);
                    return false;
                }
                timeout.Cancel();
                return await current.Ready.Task.ConfigureAwait(false);
            }
        }

        internal static bool IsCurrent(GatewayConnection current)
        {
            lock (lifecycleLock) return IsActive && ReferenceEquals(connection, current);
        }

        internal static void Dispatch(GatewayConnection current, Action callback)
            => Sync(() => { if (IsCurrent(current)) callback(); });

        public static void Stop()
        {
            lock (lifecycleLock) StopConnection(connection);
        }

        internal static void StopConnection(GatewayConnection current)
        {
            lock (lifecycleLock)
            {
                if (current == null || !ReferenceEquals(connection, current)) return;
                IsActive = false;
                connection = null;
                Rest = null;
                InteractionRest = null;
                current.Cancel();
                current.Ready.TrySetResult(false);
                Sync(() => interfaces.OnDiscordAPIClosed());
            }
            Logger.Log("DiscordUnity stopped.");
        }

        /// <summary>Deliver queued events and cache changes on the calling (Unity main) thread.</summary>
        public static void Update()
        {
            while (callbacks.TryDequeue(out var callback))
            {
                try { callback(); }
                catch (Exception exception) { Logger.LogError("Error in Discord callback.", exception); }
            }
        }
    }
}
