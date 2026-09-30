using DiscordUnity;
using DiscordUnity.Models;
using DiscordUnity.State;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnityTests
{
    internal static class Program
    {
        private const string Token = "test-token-never-use-as-real-credential";
        private static int assertions;
        private static int tests;
        private static readonly CapturingLogger logger = new CapturingLogger();

        private static int Main(string[] args)
        {
            DiscordAPI.Logger = logger;
            try
            {
                Run("v10 request serialization, snake case, errors and 204", RestProtocol);
                Run("fractional 429 delays, global limits and shared buckets", RateLimits);
                Run("cancel requests waiting for a rate limit", RestCancellation);
                Run("member routes, permission strings, array bodies and embeds", PublicRest);
                Run("multipart uploads survive rate limit retries", FileUploads);
                Run("modern pin endpoint and pagination", Pins);
                Run("optional models, 64-bit permissions and unknown guild features", Models);
                Run("thread-safe callback delivery", ConcurrentCallbacks);
                Run("READY, intents, fragmented UTF-8 and main-thread dispatch", GatewayMessages);
                Run("server changes preserve caches; unavailable is not a guild leave", GuildCache);
                Run("reconnect uses a fresh socket and the resume URL", Reconnect);
                Run("missing heartbeat ACK reconnects and resumes", MissingAck);
                Run("invalid session boolean and re-identify", InvalidSession);
                Run("fatal close codes stop without retry", FatalCloses);
                Run("heartbeat before any dispatch uses d:null", NullHeartbeat);
                Run("READY data queued before stop cannot reopen the bot", StopBeforeUpdate);
                Run("explicit JSON nulls and shared rate-limit bucket scope", ExplicitNullAndSharedBuckets);
                Run("startup timeout, discovery failure and stop/restart", Lifecycle);
                Run("slash commands and interaction acknowledgements bypass global REST delay", Interactions);
                Reset();
                Console.WriteLine("PASS: " + tests + " scenarios, " + assertions + " assertions.");
                if (args.Contains("--network-smoke")) NetworkSmoke().GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("FAIL: " + exception);
                return 1;
            }
            finally { Reset(); }
        }

        private static void Run(string name, Action test)
        {
            Reset();
            while (logger.Lines.TryTake(out _)) { }
            test();
            Check(!logger.Lines.Any(line => line.Contains(Token)), "Logs must not contain the bot token.");
            Check(!logger.Lines.Any(line => line.StartsWith("ERROR:Error in Discord callback")), "No callback processing failures.");
            tests++;
            Console.WriteLine("PASS " + name);
        }

        private static void Check(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new Exception(message);
        }

        private static T Pump<T>(Task<T> task, int timeout = 10000)
        {
            Until(() => task.IsCompleted, timeout);
            return task.GetAwaiter().GetResult();
        }

        private static void Until(Func<bool> condition, int timeout = 10000)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                DiscordAPI.Update();
                if (clock.ElapsedMilliseconds > timeout) throw new TimeoutException("Test condition was not reached.");
                Thread.Sleep(1);
            }
            DiscordAPI.Update();
        }

        private static void Reset()
        {
            var active = DiscordAPI.CurrentConnection;
            var rest = DiscordAPI.Rest;
            var interactions = DiscordAPI.InteractionRest;
            DiscordAPI.Stop();
            if (active?.Completion != null) active.Completion.GetAwaiter().GetResult();
            rest?.Cancel(); rest?.Dispose();
            interactions?.Cancel(); interactions?.Dispose();
            DiscordAPI.Rest = null;
            DiscordAPI.InteractionRest = null;
            DiscordAPI.Update();
            DiscordAPI.InitializeState();
        }

        private static HttpResponseMessage Response(int status, string json = "")
            => new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        private static FakeHttp SetRest(Func<Request, int, HttpResponseMessage> responder)
        {
            var http = new FakeHttp(responder);
            DiscordAPI.Rest = new RestClient(new HttpClient(http), Token);
            return http;
        }

        private static void RestProtocol()
        {
            var http = SetRest((request, index) => index == 0 ? Response(200, "{\"id\":\"1\",\"global_name\":\"Bjorn\"}")
                : index == 1 ? Response(204) : Response(400, "{\"code\":50035,\"message\":\"Invalid Form Body\",\"errors\":{\"name\":{}}}"));
            var user = Pump(DiscordAPI.Rest.Http<UserModel>(HttpMethod.Post, "/users/@me", new { recipientId = "1", skip = (string)null }, new { withCounts = true, text = "a&b c" }));
            Check(user && user.Data.GlobalName == "Bjorn", "Response model must use snake case settings.");
            var request0 = http.Requests[0];
            Check(request0.Url.StartsWith("https://discord.com/api/v10/users/@me?"), "REST version must be pinned.");
            Check(request0.Url.Contains("with_counts=true") && request0.Url.Contains("a%26b%20c"), "Query must be snake case and encoded.");
            Check(request0.Body.Contains("recipient_id") && !request0.Body.Contains("skip"), "Body must use snake case and omit null optional fields.");
            Check(request0.Authorization == "Bot " + Token && request0.UserAgent.Contains("DiscordBot"), "Authentication and user agent.");
            Check(Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Delete, "/channels/1")), "Empty 204 is success.");
            var error = Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/bad"));
            var exception = error.Exception as DiscordHttpException;
            Check(!error && exception?.StatusCode == 400 && exception.DiscordCode == 50035 && exception.ResponseBody.Contains("errors"), "Preserve structured Discord errors.");
        }

        private static void RateLimits()
        {
            var timer = Stopwatch.StartNew();
            var http = SetRest((request, index) =>
            {
                if (index == 0) return Response(429, "{\"retry_after\":0.08,\"global\":true}");
                var response = Response(200, "{}");
                if (index == 1)
                {
                    response.Headers.Add("X-RateLimit-Bucket", "shared");
                    response.Headers.Add("X-RateLimit-Remaining", "0");
                    response.Headers.Add("X-RateLimit-Reset-After", "0.08");
                }
                return response;
            });
            Check(Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/1/messages/2")), "429 should retry.");
            Check(http.Requests.Count == 2 && timer.ElapsedMilliseconds >= 65, "retry_after is seconds, with decimals.");
            timer.Restart();
            Check(Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/1/messages/3")), "Subsequent request succeeds.");
            Check(timer.ElapsedMilliseconds >= 65, "Normalized route honors bucket exhaustion.");
        }

        private static void RestCancellation()
        {
            var http = SetRest((request, index) => Response(429, "{\"retry_after\":60,\"global\":true}"));
            var task = DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/1/messages");
            Until(() => http.Requests.Count > 0);
            DiscordAPI.Rest.Cancel();
            var result = Pump(task);
            Check(!result && result.Exception is OperationCanceledException, "Cancellation must stop rate-limit waits.");
            Check(http.Requests.Count == 1, "Cancellation must not retry requests.");
        }

        private static void PublicRest()
        {
            var http = SetRest((request, index) =>
            {
                if (request.Url.Contains("/members/2")) return Response(200, "{\"user\":{\"id\":\"2\"},\"premium_since\":null}");
                if (request.Url.EndsWith("/roles") && request.Method == "POST") return Response(200, "{\"id\":\"3\",\"permissions\":\"1099511627776\"}");
                if (request.Url.Contains("/messages")) return Response(200, "{\"id\":\"4\",\"channel_id\":\"1\",\"content\":\"ok\"}");
                return Response(204);
            });
            Check(Pump(DiscordAPI.GetServerMember("1", "2")), "Get member.");
            Check(http.Requests[0].Url.EndsWith("/guilds/1/members/2"), "Correct members route.");
            Check(Pump(DiscordAPI.ModifyServerChannelPositions("1", "2", 1)), "Modify position.");
            Check(JToken.Parse(http.Requests[1].Body) is JArray, "Positions must be an array.");
            var role = Pump(DiscordAPI.CreateServerRole("1", "role", 1UL << 40, null, null, null));
            Check(role && role.Data.Permissions == (1UL << 40), "High permission bits preserved.");
            Check(JObject.Parse(http.Requests[2].Body)["permissions"].Type == JTokenType.String, "Permission bitfield must be a decimal string.");
            Check(Pump(DiscordAPI.CreateMessage("1", "hello", null, null, null, new { description = "test" }, null, null)), "Legacy message overload.");
            var message = JObject.Parse(http.Requests[3].Body);
            Check(message["embeds"] is JArray && message["embed"] == null, "v10 embeds are an array.");
            Check(Pump(DiscordAPI.CreateServerBan("1", "2", 2, "test reason")), "Ban endpoint.");
            Check((int)JObject.Parse(http.Requests[4].Body)["delete_message_seconds"] == 172800 && http.Requests[4].Reason == "test%20reason", "Ban payload and audit reason.");
            Check(!Pump(DiscordAPI.GetUserConnections()) && http.Requests.Count == 5, "Bot-only client rejects OAuth2-only calls locally.");
        }

        private static void FileUploads()
        {
            var http = SetRest((request, index) => index == 0 ? Response(429, "{\"retry_after\":0.001}") : Response(200, "{\"id\":\"4\",\"channel_id\":\"1\"}"));
            Check(Pump(DiscordAPI.CreateMessage("1", new DiscordMessageOptions
            {
                Content = "attachment", Files = new[] { new DiscordFile { Filename = "valheim.txt", Content = Encoding.UTF8.GetBytes("Viking") } }
            })), "Upload succeeds after retry.");
            Check(http.Requests.Count == 2, "Upload retried once.");
            foreach (var request in http.Requests)
                Check(request.ContentType.StartsWith("multipart/form-data") && request.Body.Contains("payload_json") && request.Body.Contains("files[0]") && request.Body.Contains("Viking"), "Rebuild multipart body for each attempt.");
        }

        private static void Pins()
        {
            var http = SetRest((request, index) => Response(200, "{\"items\":[{\"pinned_at\":\"2026-09-" + (index == 0 ? "29" : "28") + "T00:00:00Z\",\"message\":{\"id\":\"" + index + "\",\"channel_id\":\"1\"}}],\"has_more\":" + (index == 0 ? "true" : "false") + "}"));
            var result = Pump(DiscordAPI.GetPinnedMessages("1"));
            Check(result && result.Data.Length == 2, "Legacy convenience method gets every pin page.");
            Check(http.Requests.All(r => r.Url.Contains("/channels/1/messages/pins")), "Current pin endpoint.");
            Check(http.Requests[1].Url.Contains("before="), "Pins paginate by timestamp.");
        }

        private static void Models()
        {
            var json = "{\"id\":\"1\",\"owner_id\":\"2\",\"features\":[\"COMMUNITY\",\"NEW_UNKNOWN_FEATURE\"],\"roles\":[{\"id\":\"3\",\"permissions\":\"1099511627776\"}],\"emojis\":[{\"id\":\"4\",\"roles\":[\"3\"]}],\"channels\":[{\"id\":\"5\",\"name\":\"general\",\"permission_overwrites\":[{\"id\":\"3\",\"type\":0,\"allow\":\"1099511627776\",\"deny\":\"0\"}]}],\"members\":[{\"user\":{\"id\":\"2\",\"discriminator\":\"0\",\"global_name\":\"Bjorn\"},\"premium_since\":null,\"joined_at\":null}],\"voice_states\":[{\"user_id\":\"2\",\"channel_id\":\"5\"}]}";
            var guild = new DiscordServer(JsonConvert.DeserializeObject<GuildModel>(json, DiscordAPI.JsonSettings));
            DiscordAPI.Servers[guild.Id] = guild;
            Check(guild.FeatureNames.Contains("NEW_UNKNOWN_FEATURE"), "Unknown features do not break deserialization.");
            Check(guild.Owner.User.DisplayName == "Bjorn" && guild.Members["2"].PremiumSince == null, "Missing/null member data.");
            Check(guild.Channels["5"].Name == "general" && guild.Channels["5"].Server == guild, "Channel names and inherited guild ID.");
            Check(guild.Channels["5"].PermissionOverwrites[0].Allow == 1UL << 40, "Overwrite permission bitfield.");
            Check(guild.Emojis["4"].RoleIds.Single() == "3" && guild.VoiceStates["2"] != null, "Emoji role IDs and partial voice state.");
            Check(new DiscordServer(new GuildModel { Id = "empty" }).Channels.Count == 0, "Unavailable/minimal guilds have usable empty caches.");
            var message = JsonConvert.DeserializeObject<MessageModel>("{\"id\":\"1\",\"mention_roles\":[\"3\"],\"embeds\":[{\"type\":\"rich\"}]}", DiscordAPI.JsonSettings);
            Check(message.MentionRoles[0] == "3", "Role mentions are IDs.");
            var presence = JsonConvert.DeserializeObject<PresenceModel>("{\"user\":{\"id\":\"2\"},\"activities\":[{\"created_at\":1780000000000,\"party\":{\"id\":\"p\"}}]}", DiscordAPI.JsonSettings);
            Check(new DiscordPresence(presence).Activities[0].CreatedAt == 1780000000000L, "Millisecond timestamps need 64 bits and optional party size.");
        }

        private static void ConcurrentCallbacks()
        {
            int count = 0;
            int callingThread = Thread.CurrentThread.ManagedThreadId;
            var producers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 100; i++) DiscordAPI.Sync(() => { Check(Thread.CurrentThread.ManagedThreadId == callingThread, "Callbacks must run on Update's thread."); count++; });
            })).ToArray();
            Task.WaitAll(producers);
            DiscordAPI.Update();
            Check(count == 800, "Concurrent producers must not lose callbacks.");
        }

        private static void ConfigureGateway(params FakeSocket[] sockets)
        {
            DiscordAPI.HttpClientFactory = () => new HttpClient(new FakeHttp((request, index) => Response(200,
                "{\"url\":\"wss://gateway.example\",\"session_start_limit\":{\"remaining\":100,\"reset_after\":60000}}")));
            int next = 0;
            DiscordAPI.SocketFactory = () => sockets[Interlocked.Increment(ref next) - 1];
        }

        private static DiscordBotOptions Options(int heartbeat = 1000) => new DiscordBotOptions
        {
            Intents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            StartupTimeout = TimeSpan.FromSeconds(4), ReconnectDelay = TimeSpan.FromMilliseconds(1)
        };

        private static string ReadyJson => "{\"op\":0,\"t\":\"READY\",\"s\":1,\"d\":{\"v\":10,\"session_id\":\"session\",\"resume_gateway_url\":\"wss://resume.example\",\"user\":{\"id\":\"9\",\"username\":\"bot\",\"bot\":true},\"guilds\":[{\"id\":\"1\",\"unavailable\":true}]}}";

        private static FakeSocket ReadySocket(int interval = 1000)
        {
            var socket = new FakeSocket(interval);
            socket.OnSend = payload => { if ((int)payload["op"] == 2) socket.Push(ReadyJson); };
            return socket;
        }

        private static void GatewayMessages()
        {
            var socket = ReadySocket();
            ConfigureGateway(socket);
            string text = string.Concat(Enumerable.Repeat("Viking 🪓 é", 3000));
            int thread = Thread.CurrentThread.ManagedThreadId;
            DiscordMessage received = null;
            Action<string, JToken> handler = (name, payload) =>
            {
                Check(Thread.CurrentThread.ManagedThreadId == thread, "Dispatches must run on the Unity thread.");
                if (name == "MESSAGE_CREATE") received = new DiscordMessage(payload.ToObject<MessageModel>(DiscordAPI.JsonSerializer));
            };
            DiscordAPI.GatewayEventReceived += handler;
            try
            {
                Check(Pump(DiscordAPI.StartWithBot(Token, Options())), "READY should finish startup without private_channels.");
                Check(socket.Uri.Query == "?v=10&encoding=json", "Gateway version and encoding.");
                var identify = socket.Sent.First(p => (int)p["op"] == 2);
                Check((long)identify["d"]["intents"] == (long)Options().Intents, "IDENTIFY includes requested intents.");
                Check(identify["d"]["properties"]["os"] != null && identify["d"]["properties"]["$os"] == null, "Modern Identify properties.");
                socket.Push(new JObject { ["op"] = 0, ["t"] = "MESSAGE_CREATE", ["s"] = 2,
                    ["d"] = new JObject { ["id"] = "20", ["channel_id"] = "30", ["content"] = text, ["mention_roles"] = new JArray("4"), ["author"] = new JObject { ["id"] = "2" } } }.ToString(), 113);
                Until(() => received != null);
                Check(received.Content == text && received.Channel != null && received.ChannelId == "30", "Reassemble UTF-8 fragments and initialize observed DMs.");
                socket.Push("{\"op\":1,\"d\":null}");
                Until(() => socket.Sent.Any(p => (int)p["op"] == 1 && (int?)p["d"] == 2));
            }
            finally { DiscordAPI.GatewayEventReceived -= handler; }
        }

        private static void GuildCache()
        {
            var socket = ReadySocket(); ConfigureGateway(socket);
            Check(Pump(DiscordAPI.StartWithBot(Token, Options())), "Start.");
            socket.Push("{\"op\":0,\"t\":\"GUILD_CREATE\",\"s\":2,\"d\":{\"id\":\"1\",\"name\":\"Valheim\",\"channels\":[{\"id\":\"3\",\"name\":\"general\"}]}}");
            Until(() => DiscordAPI.Servers["1"].Channels.Count == 1);
            var channels = DiscordAPI.Servers["1"].Channels;
            socket.Push("{\"op\":0,\"t\":\"GUILD_UPDATE\",\"s\":3,\"d\":{\"id\":\"1\",\"name\":\"Updated\"}}");
            Until(() => DiscordAPI.Servers["1"].Name == "Updated");
            Check(ReferenceEquals(channels, DiscordAPI.Servers["1"].Channels), "Partial guild update must preserve mutable channel cache.");
            socket.Push("{\"op\":0,\"t\":\"GUILD_DELETE\",\"s\":4,\"d\":{\"id\":\"1\",\"unavailable\":true}}");
            Until(() => DiscordAPI.Servers["1"].Unavailable == true);
            Check(DiscordAPI.Servers.ContainsKey("1"), "Unavailable guild remains cached.");
            socket.Push("{\"op\":0,\"t\":\"GUILD_DELETE\",\"s\":5,\"d\":{\"id\":\"1\"}}");
            Until(() => !DiscordAPI.Servers.ContainsKey("1"));
        }

        private static void Reconnect()
        {
            var first = ReadySocket(); var second = new FakeSocket();
            second.OnSend = p => { if ((int)p["op"] == 6) second.Push("{\"op\":0,\"t\":\"RESUMED\",\"s\":2,\"d\":{}}"); };
            ConfigureGateway(first, second);
            Check(Pump(DiscordAPI.StartWithBot(Token, Options())), "Start.");
            first.Push("{\"op\":7,\"d\":null}");
            Until(() => second.Sent.Any(p => (int)p["op"] == 6));
            Check(first.Aborted && second.Uri.Host == "resume.example", "Use new socket and resume gateway.");
            var resume = second.Sent.First(p => (int)p["op"] == 6);
            Check((string)resume["d"]["session_id"] == "session" && (int)resume["d"]["seq"] == 1, "Resume uses session and sequence.");
            Check(!second.Sent.Any(p => (int)p["op"] == 2), "Resume must not Identify.");
        }

        private static void MissingAck()
        {
            var first = ReadySocket(25); first.AutoAck = false;
            var second = new FakeSocket(); ConfigureGateway(first, second);
            Check(Pump(DiscordAPI.StartWithBot(Token, Options())), "Start.");
            Until(() => second.Sent.Any(p => (int)p["op"] == 6));
            Check(first.Aborted, "Missing ACK aborts zombie connection.");
        }

        private static void InvalidSession()
        {
            Check(!JsonConvert.DeserializeObject<PayloadModel>("{\"op\":9,\"d\":false}", DiscordAPI.JsonSettings).As<bool>().Data, "Boolean Gateway payload must deserialize.");
            var first = ReadySocket(); var second = ReadySocket(); ConfigureGateway(first, second);
            Check(Pump(DiscordAPI.StartWithBot(Token, Options())), "Start.");
            var timer = Stopwatch.StartNew(); first.Push("{\"op\":9,\"d\":false}");
            Until(() => second.Sent.Any(p => (int)p["op"] == 2), 9000);
            Check(second.Uri.Host == "gateway.example" && !second.Sent.Any(p => (int)p["op"] == 6), "Non-resumable session creates a new Identify.");
            Check(timer.ElapsedMilliseconds >= 4700, "IDENTIFY concurrency window is respected.");
        }

        private static void FatalCloses()
        {
            foreach (int code in new[] { 4004, 4010, 4011, 4012, 4013, 4014 }) Check(GatewayConnection.IsFatalClose(code), "Fatal close classification.");
            Check(!GatewayConnection.IsFatalClose(4007), "Invalid sequence may re-identify.");
            var socket = new FakeSocket();
            socket.OnSend = p => { if ((int)p["op"] == 2) socket.Close(4014); };
            ConfigureGateway(socket);
            Check(!Pump(DiscordAPI.StartWithBot(Token, Options())), "Fatal close finishes startup with false.");
            Check(!DiscordAPI.IsActive && DiscordAPI.LastGatewayCloseCode == 4014 && socket.Connects == 1, "No reconnect loop for disallowed intents.");
        }

        private static void Lifecycle()
        {
            var socket = new FakeSocket(); ConfigureGateway(socket);
            var options = Options(); options.StartupTimeout = TimeSpan.FromMilliseconds(150);
            Check(!Pump(DiscordAPI.StartWithBot(Token, options)), "No READY must time out.");
            Check(!DiscordAPI.IsActive, "Timeout stops client.");
            Reset();
            DiscordAPI.HttpClientFactory = () => new HttpClient(new FakeHttp((request, index) => Response(401, "{\"code\":0}")));
            Check(!Pump(DiscordAPI.StartWithBot(Token, Options())), "Invalid token fails gateway discovery.");
            Check(!DiscordAPI.IsActive, "Failed discovery cleans up.");
            Reset();
            var first = ReadySocket(); var second = ReadySocket(); ConfigureGateway(first, second);
            var start = DiscordAPI.StartWithBot(Token, Options());
            Check(!Pump(DiscordAPI.StartWithBot(Token, Options())), "Duplicate startup rejected.");
            Check(Pump(start), "Original startup succeeds.");
            var active = DiscordAPI.CurrentConnection;
            first.Push("{\"op\":0,\"t\":\"MESSAGE_CREATE\",\"s\":2,\"d\":{\"id\":\"1\",\"channel_id\":\"stale\"}}");
            DiscordAPI.Stop(); active.Completion.GetAwaiter().GetResult();
            Check(Pump(DiscordAPI.StartWithBot(Token, Options())), "Immediate restart succeeds.");
            Check(!DiscordAPI.PrivateChannels.ContainsKey("stale"), "Old callbacks must not alter a restarted bot.");
            var pending = DiscordAPI.CreateMessage("1", "test");
            DiscordAPI.Stop();
            Check(!Pump(pending), "Stop completes pending Unity-queued REST results.");
        }

        private static void NullHeartbeat()
        {
            var socket = new FakeSocket(25); ConfigureGateway(socket);
            var start = DiscordAPI.StartWithBot(Token, Options());
            Until(() => socket.Sent.Any(p => (int)p["op"] == 1));
            var heartbeat = socket.Sent.First(p => (int)p["op"] == 1);
            Check(heartbeat["d"] != null && heartbeat["d"].Type == JTokenType.Null, "Before a dispatch, heartbeat d must explicitly be null.");
            DiscordAPI.Stop();
            Check(!Pump(start), "Stop resolves pending startup without needing a READY callback.");
        }

        private static void StopBeforeUpdate()
        {
            var socket = ReadySocket(); ConfigureGateway(socket);
            var start = DiscordAPI.StartWithBot(Token, Options());
            // Deliberately do not pump Unity Update while READY is received.
            var timer = Stopwatch.StartNew();
            while (!socket.Sent.Any(p => (int)p["op"] == 2))
            {
                if (timer.ElapsedMilliseconds > 2000) throw new TimeoutException();
                Thread.Sleep(1);
            }
            Thread.Sleep(20);
            var active = DiscordAPI.CurrentConnection;
            DiscordAPI.Stop(); active.Completion.GetAwaiter().GetResult();
            Check(!Pump(start) && DiscordAPI.User == null, "A queued READY must not revive a stopped bot.");
        }

        private static void ExplicitNullAndSharedBuckets()
        {
            var http = SetRest((request, index) =>
            {
                var response = Response(200, "{}");
                response.Headers.Add("X-RateLimit-Bucket", "shared-bucket");
                if (index == 2)
                {
                    response.Headers.Add("X-RateLimit-Remaining", "0");
                    response.Headers.Add("X-RateLimit-Reset-After", "0.08");
                }
                return response;
            });
            Check(Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Post, "/channels/1/messages", new JObject { ["nick"] = JValue.CreateNull() })), "Explicit null payload.");
            Check(JObject.Parse(http.Requests[0].Body)["nick"].Type == JTokenType.Null, "JObject supports clearing a nullable field.");
            Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/1/pins"));
            Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Post, "/channels/1/messages"));
            var timer = Stopwatch.StartNew();
            Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/2/pins"));
            Check(timer.ElapsedMilliseconds < 65, "Different major channel IDs must not share a bucket deadline.");
            Pump(DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/1/pins"));
            Check(timer.ElapsedMilliseconds >= 65, "Different routes on the same known bucket and channel respect its deadline.");
        }

        private static void Interactions()
        {
            var normal = SetRest((request, index) => Response(429, "{\"retry_after\":60,\"global\":true}"));
            var blocked = DiscordAPI.Rest.Http<object>(HttpMethod.Get, "/channels/1/messages");
            Until(() => normal.Requests.Count == 1);
            var http = new FakeHttp((request, index) => Response(204));
            DiscordAPI.InteractionRest = new RestClient(new HttpClient(http), Token);
            var interaction = new DiscordInteraction(JObject.Parse("{\"id\":\"1\",\"application_id\":\"2\",\"type\":2,\"token\":\"interaction-secret\",\"member\":{\"user\":{\"id\":\"3\"}},\"data\":{\"name\":\"status\"}}"));
            var timer = Stopwatch.StartNew();
            Check(Pump(interaction.Defer(true)) && timer.ElapsedMilliseconds < 1000, "Interaction acknowledgement bypasses a global wait.");
            Check(http.Requests[0].Authorization == null && http.Requests[0].Url.Contains("/interactions/1/interaction-secret/callback"), "Callback token authentication.");
            var body = JObject.Parse(http.Requests[0].Body);
            Check((int)body["type"] == 5 && (int)body["data"]["flags"] == 64 && interaction.UserId == "3", "Defer ephemeral response and parse author.");
            DiscordAPI.Rest.Cancel(); Pump(blocked);
            Reset();
            var commands = SetRest((request, index) => Response(200, "{\"id\":\"1\"}"));
            Check(Pump(DiscordAPI.CreateApplicationCommand("2", new { name = "status", description = "Server status", defaultMemberPermissions = "0" }, "3")), "Register guild command.");
            Check(commands.Requests[0].Url.EndsWith("/applications/2/guilds/3/commands") && commands.Requests[0].Body.Contains("default_member_permissions"), "Command route and payload.");
        }

        private static async Task NetworkSmoke()
        {
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            using (var http = new HttpClient())
            using (var socket = new ClientWebSocket())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/bjorno43/DiscordUnity, 2.0.0)");
                var response = await http.GetAsync("https://discord.com/api/v10/gateway", cancel.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var json = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                await socket.ConnectAsync(GatewayConnection.BuildUri((string)json["url"]), cancel.Token).ConfigureAwait(false);
                var buffer = new byte[8192];
                var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel.Token).ConfigureAwait(false);
                var hello = JObject.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
                Check((int)hello["op"] == 10 && (int)hello["d"]["heartbeat_interval"] > 0, "Live unauthenticated gateway HELLO.");
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Compatibility smoke test", cancel.Token).ConfigureAwait(false);
                Console.WriteLine("PASS live TLS REST /api/v10/gateway and WSS Gateway v10 HELLO (no token, no Identify).");
            }
        }

        private sealed class CapturingLogger : ILogger
        {
            internal readonly ConcurrentBag<string> Lines = new ConcurrentBag<string>();
            public void Log(string message) => Lines.Add(message);
            public void LogWarning(string message) => Lines.Add(message);
            public void LogError(string message, Exception exception = null) => Lines.Add("ERROR:" + message);
        }

        private sealed class Request
        {
            internal string Url, Method, Body, Authorization, UserAgent, ContentType, Reason;
        }

        private sealed class FakeHttp : HttpMessageHandler
        {
            private readonly Func<Request, int, HttpResponseMessage> respond;
            internal readonly List<Request> Requests = new List<Request>();
            internal FakeHttp(Func<Request, int, HttpResponseMessage> respond) => this.respond = respond;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellation)
            {
                cancellation.ThrowIfCancellationRequested();
                var request = new Request
                {
                    Url = message.RequestUri.AbsoluteUri, Method = message.Method.Method,
                    Body = message.Content == null ? "" : await message.Content.ReadAsStringAsync().ConfigureAwait(false),
                    ContentType = message.Content?.Headers.ContentType?.ToString(),
                    Authorization = message.Headers.Authorization?.ToString(), UserAgent = message.Headers.UserAgent.ToString(),
                    Reason = message.Headers.TryGetValues("X-Audit-Log-Reason", out var reason) ? reason.Single() : null
                };
                lock (Requests) { int index = Requests.Count; Requests.Add(request); return respond(request, index); }
            }
        }

        private sealed class FakeSocket : IGatewaySocket
        {
            private sealed class Frame { internal byte[] Bytes; internal bool End; internal int? Close; }
            private readonly ConcurrentQueue<Frame> frames = new ConcurrentQueue<Frame>();
            private readonly SemaphoreSlim available = new SemaphoreSlim(0);
            private readonly int interval;
            internal readonly ConcurrentQueue<JObject> Sent = new ConcurrentQueue<JObject>();
            internal Action<JObject> OnSend;
            internal bool AutoAck = true;
            internal bool Aborted;
            internal Uri Uri;
            internal int Connects;
            internal FakeSocket(int interval = 1000) => this.interval = interval;
            public Task ConnectAsync(Uri uri, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                Uri = uri; Connects++;
                Push("{\"op\":10,\"d\":{\"heartbeat_interval\":" + interval + "}}");
                return Task.CompletedTask;
            }
            internal void Push(string json, int fragmentSize = 8192)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                for (int offset = 0; offset < bytes.Length; offset += fragmentSize)
                {
                    int count = Math.Min(fragmentSize, bytes.Length - offset);
                    var part = new byte[count]; Array.Copy(bytes, offset, part, 0, count);
                    frames.Enqueue(new Frame { Bytes = part, End = offset + count == bytes.Length }); available.Release();
                }
            }
            internal void Close(int code) { frames.Enqueue(new Frame { Close = code }); available.Release(); }
            public async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancel)
            {
                await available.WaitAsync(cancel).ConfigureAwait(false);
                if (!frames.TryDequeue(out var frame)) throw new Exception("Missing frame.");
                if (frame.Close.HasValue) return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, (WebSocketCloseStatus)frame.Close.Value, "Test close");
                Array.Copy(frame.Bytes, 0, buffer.Array, buffer.Offset, frame.Bytes.Length);
                return new WebSocketReceiveResult(frame.Bytes.Length, WebSocketMessageType.Text, frame.End);
            }
            public Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                var payload = JObject.Parse(Encoding.UTF8.GetString(buffer.Array, buffer.Offset, buffer.Count));
                Sent.Enqueue(payload);
                if ((int)payload["op"] == 1 && AutoAck) Push("{\"op\":11,\"d\":null}");
                OnSend?.Invoke(payload);
                return Task.CompletedTask;
            }
            public void Abort() => Aborted = true;
            public void Dispose() { }
        }
    }
}
