using DiscordUnity.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DiscordUnity
{
    internal interface IGatewaySocket : IDisposable
    {
        Task ConnectAsync(Uri uri, CancellationToken cancellation);
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellation);
        Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellation);
        void Abort();
    }

    internal sealed class GatewaySocket : IGatewaySocket
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        public Task ConnectAsync(Uri uri, CancellationToken cancellation) => socket.ConnectAsync(uri, cancellation);
        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellation)
            => socket.ReceiveAsync(buffer, cancellation);
        public Task SendAsync(ArraySegment<byte> buffer, CancellationToken cancellation)
            => socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellation);
        public void Abort() => socket.Abort();
        public void Dispose() => socket.Dispose();
    }

    internal sealed class GatewayConnection
    {
        private readonly string token;
        private readonly DiscordBotOptions options;
        private readonly RestClient rest;
        private readonly RestClient interactionRest;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly Random random = new Random();
        private string gatewayUrl;
        private string resumeUrl;
        private string session;
        private int sequence = -1;
        private int heartbeatPending;
        private DateTime lastIdentify = DateTime.MinValue;
        private bool recovered;
        internal readonly TaskCompletionSource<bool> Ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Completion { get; private set; }

        internal GatewayConnection(string token, DiscordBotOptions options, RestClient rest, RestClient interactionRest = null)
        {
            this.token = token;
            this.options = options;
            this.rest = rest;
            this.interactionRest = interactionRest;
        }

        internal void Start() => Completion = Task.Run(RunAsync);
        internal void Cancel()
        {
            cancellation.Cancel();
            rest.Cancel();
            interactionRest?.Cancel();
        }

        private bool CanResume => session != null && Volatile.Read(ref sequence) >= 0 && resumeUrl != null;
        internal static Uri BuildUri(string url)
        {
            var builder = new UriBuilder(url) { Query = "v=10&encoding=json" };
            if (builder.Scheme != "wss") throw new InvalidOperationException("Gateway must use WSS.");
            return builder.Uri;
        }

        internal static bool IsFatalClose(int code)
            => code == 4004 || code == 4010 || code == 4011 || code == 4012 || code == 4013 || code == 4014;

        private void ClearSession()
        {
            session = null;
            resumeUrl = null;
            Interlocked.Exchange(ref sequence, -1);
        }

        private async Task RunAsync()
        {
            int failures = 0;
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    double delay = options.ReconnectDelay.TotalMilliseconds;
                    recovered = false;
                    try
                    {
                        if (!CanResume)
                        {
                            var result = await rest.Http<GatewayModel>(System.Net.Http.HttpMethod.Get, "/gateway/bot").ConfigureAwait(false);
                            if (!result)
                            {
                                var error = result.Exception as DiscordHttpException;
                                if (error != null && (error.StatusCode == 401 || error.StatusCode == 403))
                                    throw new GatewayFailure(error.StatusCode, false, 0, true);
                                throw new InvalidOperationException("Gateway discovery failed.");
                            }
                            gatewayUrl = result.Data.Url;
                            var limit = result.Data.SessionStartLimit;
                            if (limit != null && limit.Remaining <= 0)
                            {
                                DiscordAPI.Logger.LogWarning("Discord session start limit reached; waiting for reset.");
                                await Task.Delay(Math.Max(1000, limit.ResetAfter), cancellation.Token).ConfigureAwait(false);
                                continue;
                            }
                            // One shard uses one concurrency bucket. Space IDENTIFYs by at least 5 seconds.
                            var wait = TimeSpan.FromSeconds(5) - (DateTime.UtcNow - lastIdentify);
                            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancellation.Token).ConfigureAwait(false);
                        }
                        using (var socket = DiscordAPI.SocketFactory())
                        using (var link = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
                        {
                            Task heartbeat = null;
                            Task receive = null;
                            try
                            {
                                using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(link.Token))
                                {
                                    handshake.CancelAfter(TimeSpan.FromSeconds(15));
                                    await socket.ConnectAsync(BuildUri(CanResume ? resumeUrl : gatewayUrl), handshake.Token).ConfigureAwait(false);
                                    var hello = await Receive(socket, handshake.Token).ConfigureAwait(false);
                                    if (hello.Op != 10) throw new InvalidOperationException("Expected Gateway HELLO.");
                                    int interval = hello.As<HeartbeatModel>().Data.HeartbeatInterval;
                                    if (interval <= 0) throw new InvalidOperationException("Invalid heartbeat interval.");
                                    Interlocked.Exchange(ref heartbeatPending, 0);
                                    heartbeat = Heartbeat(socket, interval, link.Token);
                                }
                                if (CanResume)
                                    await Send(socket, new PayloadModel<ResumeModel>
                                    {
                                        Op = 6,
                                        Data = new ResumeModel { Token = token, SessionId = session, Sequence = Volatile.Read(ref sequence) }
                                    }, link.Token).ConfigureAwait(false);
                                else
                                {
                                    lastIdentify = DateTime.UtcNow;
                                    await Send(socket, new PayloadModel<IdentityModel>
                                    {
                                        Op = 2,
                                        Data = new IdentityModel
                                        {
                                            Token = token, Intents = (long)options.Intents,
                                            Properties = new Dictionary<string, string>
                                            {
                                                { "os", Environment.OSVersion.Platform.ToString() },
                                                { "browser", "DiscordUnity" }, { "device", "DiscordUnity" }
                                            }
                                        }
                                    }, link.Token).ConfigureAwait(false);
                                }
                                receive = Listen(socket, link.Token);
                                var ended = await Task.WhenAny(receive, heartbeat).ConfigureAwait(false);
                                await ended.ConfigureAwait(false);
                            }
                            finally
                            {
                                link.Cancel();
                                // Abort keeps a resumable session alive; normal close would invalidate it.
                                socket.Abort();
                                if (receive != null) await Observe(receive).ConfigureAwait(false);
                                if (heartbeat != null) await Observe(heartbeat).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (GatewayFailure failure)
                    {
                        if (DiscordAPI.IsCurrent(this)) DiscordAPI.LastGatewayCloseCode = failure.Code;
                        DiscordAPI.Logger.LogWarning("Discord Gateway closed (" + failure.Code + ").");
                        if (failure.Fatal || IsFatalClose(failure.Code)) return;
                        if (!failure.Resume) ClearSession();
                        delay = failure.DelayMilliseconds;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                    catch (Exception exception)
                    {
                        // Never log raw payloads, tokens, or transport exception URLs.
                        DiscordAPI.Logger.LogWarning("Discord connection interrupted (" + exception.GetType().Name + ").");
                        delay = Math.Min(30000, Math.Max(1000, delay) * Math.Pow(2, Math.Min(failures++, 5)));
                    }
                    if (recovered) failures = 0;
                    await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally
            {
                DiscordAPI.StopConnection(this);
                Ready.TrySetResult(false);
                rest.Dispose();
                interactionRest?.Dispose();
                sendLock.Dispose();
                cancellation.Dispose();
            }
        }

        private static async Task Observe(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception) { /* Failure is handled by the supervisor; cleanup observes both tasks. */ }
        }

        private async Task<PayloadModel> Receive(IGatewaySocket socket, CancellationToken cancel)
        {
            var buffer = new byte[8192];
            using (var message = new MemoryStream())
            {
                WebSocketReceiveResult frame;
                do
                {
                    frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel).ConfigureAwait(false);
                    if (frame.MessageType == WebSocketMessageType.Close)
                    {
                        int code = (int)(frame.CloseStatus ?? WebSocketCloseStatus.Empty);
                        bool resume = code != 1000 && code != 1001 && code != 4003 && code != 4007 && code != 4009;
                        throw new GatewayFailure(code, resume, options.ReconnectDelay.TotalMilliseconds);
                    }
                    if (frame.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("Expected JSON text.");
                    if (message.Length + frame.Count > options.MaxGatewayMessageBytes) throw new InvalidOperationException("Gateway message exceeds configured limit.");
                    message.Write(buffer, 0, frame.Count);
                } while (!frame.EndOfMessage);
                return JsonConvert.DeserializeObject<PayloadModel>(Encoding.UTF8.GetString(message.ToArray()), DiscordAPI.JsonSettings);
            }
        }

        private async Task Listen(IGatewaySocket socket, CancellationToken cancel)
        {
            while (true)
            {
                var payload = await Receive(socket, cancel).ConfigureAwait(false);
                if (payload.Op == 0 && payload.Sequence.HasValue) Interlocked.Exchange(ref sequence, payload.Sequence.Value);
                switch (payload.Op)
                {
                    case 0:
                        if (payload.Event == "READY")
                        {
                            var ready = payload.As<ReadyModel>().Data;
                            session = ready.SessionId;
                            resumeUrl = ready.ResumeGatewayUrl;
                            recovered = true;
                        }
                        else if (payload.Event == "RESUMED") recovered = true;
                        DiscordAPI.Dispatch(this, () => DiscordAPI.ProcessDispatch(this, payload));
                        break;
                    case 1:
                        await SendHeartbeat(socket, cancel).ConfigureAwait(false);
                        break;
                    case 7:
                        throw new GatewayFailure(7, true, 0);
                    case 9:
                        throw new GatewayFailure(9, payload.As<bool>().Data, random.Next(1000, 5001));
                    case 11:
                        Interlocked.Exchange(ref heartbeatPending, 0);
                        break;
                }
            }
        }

        private async Task Heartbeat(IGatewaySocket socket, int interval, CancellationToken cancel)
        {
            await Task.Delay(random.Next(interval), cancel).ConfigureAwait(false);
            while (true)
            {
                if (Volatile.Read(ref heartbeatPending) != 0) throw new TimeoutException("Missing heartbeat ACK.");
                await SendHeartbeat(socket, cancel).ConfigureAwait(false);
                await Task.Delay(interval, cancel).ConfigureAwait(false);
            }
        }

        private Task SendHeartbeat(IGatewaySocket socket, CancellationToken cancel)
        {
            Interlocked.Exchange(ref heartbeatPending, 1);
            var last = Volatile.Read(ref sequence);
            return Send(socket, new PayloadModel<int?> { Op = 1, Data = last < 0 ? (int?)null : last }, cancel);
        }

        private async Task Send<T>(IGatewaySocket socket, PayloadModel<T> payload, CancellationToken cancel)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload, DiscordAPI.JsonSettings));
            await sendLock.WaitAsync(cancel).ConfigureAwait(false);
            try { await socket.SendAsync(new ArraySegment<byte>(bytes), cancel).ConfigureAwait(false); }
            finally { sendLock.Release(); }
        }

        private sealed class GatewayFailure : Exception
        {
            internal readonly int Code;
            internal readonly bool Resume;
            internal readonly bool Fatal;
            internal readonly double DelayMilliseconds;
            internal GatewayFailure(int code, bool resume, double delay, bool fatal = false)
            {
                Code = code; Resume = resume; DelayMilliseconds = delay; Fatal = fatal;
            }
        }
    }
}
