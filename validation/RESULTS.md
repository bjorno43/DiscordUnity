# Validation results — September 30, 2026

| Check | Result |
|---|---|
| Production library, Release, .NET Standard 2.0 / C# 7.3 | Passed, 0 warnings / 0 errors |
| Test hosts, Release, .NET 8 and .NET Framework 4.7.2 | Passed, 0 warnings / 0 errors |
| Offline suite on .NET 8 | Passed: 19 scenarios, 928 assertions |
| Same suite on Unity's standalone Mono 6.13.0 runtime, x86 | Passed: 19 scenarios, 928 assertions |
| Same suite inside Unity Editor 6000.0.75f1 / Mono | Passed: 19 scenarios, 928 assertions; exit code 0 |
| Public live Discord REST discovery and Gateway v10 HELLO inside Unity/Mono | Passed over HTTPS and WSS, without a bot token or Identify |
| Public live Discord REST discovery and Gateway v10 HELLO on .NET 8 | Passed; performed before the final three additional regression scenarios |
| BepInEx 5 example against locally installed current Valheim managed assemblies | Passed, 0 warnings / 0 errors; example targets .NET Standard 2.1 |

The Unity run completed with:

```text
PASS: 19 scenarios, 928 assertions.
PASS live TLS REST /api/v10/gateway and WSS Gateway v10 HELLO (no token, no Identify).
DiscordUnity Unity/Mono validation exit code: 0
```

## Covered scenarios

1. V10 REST, snake case, query encoding, bot authorization, error bodies, and empty 204 responses.
2. Fractional retry-after values, global limits, and exhausted route buckets.
3. Cancellation during a rate-limit wait.
4. Member routes, 64-bit permission strings, array bodies, embeds, and ban audit reasons.
5. Rebuilding and resending a multipart upload after a 429.
6. Current pin routes with multiple pages.
7. Optional models, unknown guild features, role IDs, overwrites, and millisecond timestamps.
8. Eight concurrent callback producers; 800 callbacks delivered without loss on Update's thread.
9. READY without private_channels, intents, fragmented UTF-8, and main-thread dispatch.
10. Guild updates preserve channel caches; unavailable guilds are not removed.
11. Reconnect creates a fresh socket and uses resume_gateway_url with the correct session/sequence.
12. Missing heartbeat ACK ends the unresponsive connection attempt and resumes.
13. Invalid Session with boolean false recovers through a new, properly spaced Identify.
14. Fatal close codes stop without a retry loop.
15. Heartbeats before the first dispatch include explicit `d:null`.
16. A queued READY callback cannot reopen a stopped bot.
17. Explicit JSON null fields and bucket sharing with the correct major-channel scope.
18. Startup timeout, discovery failure, duplicate start, and stop/restart.
19. Slash-command payloads and interaction acknowledgements independent of ordinary global REST waits.

## Remaining integration checks

No real bot logged in, no Discord server message was sent, and no gameplay test was performed. The authenticated flow was simulated. Token configuration, guild/channel permissions, privileged intents, and lifecycle inside a real Valheim client or dedicated server must be checked when integrating the mod.

The first public network check using standalone mono.exe reported missing TLS support because that console host does not initialize Unity's TLS provider. The final check inside the actual editor passed with normal certificate validation. Production code contains no certificate bypass or insecure alternative TLS handling.

A subsequent editor run after the English documentation and repository URL updates stopped before executing tests because Unity reported no valid editor license (exit code 198). The successful editor results above refer to the earlier completed run. The final library/test builds, standalone Mono suite, and .NET 8 suite were rerun successfully after those updates. A licensed editor is required to repeat the Unity-hosted network check.
