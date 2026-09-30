# DiscordUnity modernization audit

Investigated on September 30, 2026, starting from upstream commit `09cdfb90b2501f7f73e08852d9ced361dc8bd847` dated November 26, 2020. The upstream README states that development has stopped and invites users to maintain a fork. The resulting changes are maintained in [bjorno43/DiscordUnity](https://github.com/bjorno43/DiscordUnity).

## Assessment

The original project required changes before serving as a reliable foundation for a contemporary bot. The library can retain its .NET Standard 2.0 target: that profile provides the networking and threading APIs needed by Unity/Mono.

Discord documents v10 as an available API version. REST now uses `https://discord.com/api/v10`, and the Gateway uses `?v=10&encoding=json`, following the [official API reference](https://docs.discord.com/developers/reference) rather than relying on a default version.

## Findings and changes

| Upstream finding | Change in this fork |
|---|---|
| Gateway v6 with no intents | V10, configurable intents, and current Identify properties |
| Payload `d` always assumed to be `JObject` | `JToken` supports boolean, null, and other payload types |
| Messages above 8 KiB lost; full buffer length used regardless of received bytes | Assemble fragments, process only `Count`, and decode UTF-8 after receiving the full message |
| Closed `ClientWebSocket` reused | Fresh socket for each connection; cancel and observe the previous receive and heartbeat tasks |
| Reconnect used the original URL and normal close | Preserve the READY session and `resume_gateway_url`; abort resumable failures |
| Unmanaged `async void` heartbeat without cancellation | Managed task with jitter, ACK checks, and a recovery/backoff supervisor |
| No recovery policy for protocol close codes | Stop on 4004/4010–4014; reset the session for codes including 4007/4009; interpret invalid-session data |
| Session and READY data processed too late through callbacks | Update protocol state immediately; update game caches through Unity Update |
| Unsafe callback queue | `ConcurrentQueue` and guards against callbacks from stopped connections |
| Stop/start races and startup could wait indefinitely | Connection ownership, cancellable startup timeout, and immediate Stop completion |
| Identify debug logging exposed the bot token | No raw transport payload or token-bearing URL logging |
| No consistent snake-case REST serialization | Shared settings for requests, queries, and response models |
| Errors exposed only the HTTP reason phrase | `DiscordHttpException` with status, Discord code, and response body |
| No 429 handling | Fractional retry-after seconds, bucket headers, global deadlines, bounded retries, and cancellation |
| Incorrect multipart uploads | `payload_json`, `files[n]`, and body reconstruction on retries |
| Misspelled `/mebmers` routes | `/members` and current nickname and ban payloads |
| Position changes sent as a single object | Arrays for channel and role positions |
| `embed` instead of `embeds` | Current array format, including translation through the legacy overload |
| 32-bit/bool permission representations | `ulong`, decimal strings on the wire, and numeric overwrite types |
| Role objects expected where Discord sends ID lists | Snowflake IDs for emoji roles and role mentions |
| Non-nullable member fields and absent caches | Accept optional/null data, initialize dictionaries, and use safe lookups |
| Unknown guild features broke enum deserialization | Preserve all feature names and expose recognized values through the legacy enum |
| Guild updates replaced channel/member caches | Merge partial updates while preserving mutable caches |
| Many existing event interfaces never received callbacks | Complete dispatch for existing member, role, ban, invite, status, voice-state, and message events |
| Obsolete pin routes | Current paginated `/channels/{id}/messages/pins` routes |
| No application commands or interactions | Command registration/management, interaction events, reply/defer, original response, and follow-ups |
| Newtonsoft.Json 12.0.3 | 13.0.4 while retaining .NET Standard 2.0 |

Gateway recovery and intents follow [Discord Gateway](https://docs.discord.com/developers/events/gateway) and its [close codes](https://docs.discord.com/developers/topics/opcodes-and-status-codes). REST handling follows the [rate-limit documentation](https://docs.discord.com/developers/topics/rate-limits). Endpoints and payloads were checked against the [message](https://docs.discord.com/developers/resources/message), [channel](https://docs.discord.com/developers/resources/channel), [guild](https://docs.discord.com/developers/resources/guild), and [user](https://docs.discord.com/developers/resources/user) references.

## Unity/Mono requirement

The production assembly targets only .NET Standard 2.0 and uses C# 7.3 and existing BCL types. No System.Text.Json, ASP.NET, modern .NET runtime components, or native Discord SDK was added. Only the test hosts use .NET 8 or .NET Framework 4.7.2. Newtonsoft.Json 13.0.4 provides a .NET Standard 2.0 assembly; see its [NuGet package information](https://www.nuget.org/packages/Newtonsoft.Json/13.0.4).

Compatibility was tested inside Unity Editor 6000.0.75f1 with Mono, including public live HTTPS and WSS connections to Discord. The Valheim/BepInEx example builds against locally installed game assemblies. Those assemblies require the example to target .NET Standard 2.1, while the Discord library retains its .NET Standard 2.0 target.

## Scope and limitations

- No real bot token was supplied or collected from configuration files. Authenticated Identify/READY, message actions, and reconnects were tested using scripted transports. Public Discord Gateway discovery and HELLO were tested live.
- The mod was not installed into a running Valheim client or server. The example plugin was built but was not tested during gameplay.
- One bot connection and one shard are supported per process. A sharding requirement stops the connection with the relevant close code. IDENTIFY attempts are spaced at least five seconds apart within this connection, and discovery checks the remaining session starts. Multiple processes using one token must coordinate their start budget externally.
- Ordinary REST requests are conservatively serialized. The implementation observes received limits and recovers from 429 responses; it is not a high-throughput scheduler for thousands of guilds. Interaction acknowledgements have a separate transport queue.
- Voice-state/server events are exposed. Audio, codecs, and Discord voice/DAVE handshakes are not implemented; the original voice REST file was already empty.
- Not every modern Discord object has a typed wrapper. Raw dispatch events are available, and unknown properties are accepted. MESSAGE_UPDATE remains partial, and the library does not store a complete message cache.
- The historical upstream `.unitypackage` has been removed to avoid installing obsolete code. The packaging script includes the updated DLLs and source.
- Correcting permission and timestamp sizes changes the public interface and requires existing mods to be rebuilt. See the migration table in the README.

The remaining integration check is a bot session in a test guild within Valheim: startup, receiving text/slash commands, sending replies, recovery after network loss, and game shutdown. See [validation/RESULTS.md](validation/RESULTS.md) for the evidence behind the completed checks.
