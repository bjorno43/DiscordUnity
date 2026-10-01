# Chat and moderation validation

Validated on October 1, 2026:

| Check | Result |
|---|---|
| Release build against Valheim client 1.0.16 | Passed, 0 warnings / 0 errors |
| Release build against dedicated server 1.0.12 | Passed, 0 warnings / 0 errors |
| Chat/command policy suite on .NET 8 | Passed, 52 assertions |
| Native packet suite using installed client assemblies on .NET 8 | Passed, 13 assertions |
| Isolated Unity Player / Mono, dedicated server 1.0.12 assemblies and production plugin | Passed, 54 integration checks |
| DiscordUnity regression suite on .NET 8 and .NET Framework 4.7.2 | Passed, 19 scenarios / 928 assertions per host |

The native suite uses the game's `ZRpc.Serialize`, `UserInfo` and `ZRoutedRpc.RoutedRPCData` serialization. It checks chat decoding, unmodified vanilla bytes/read position, authenticated sender/player-object validation, ping exclusion, and malformed/oversized packets.

The policy suite covers nickname/channel/guild filtering, bot/webhook loops, readable mentions, control characters, Unicode and Discord text limits, duplicate tracking and recipient coalescing. Command checks cover role parsing, empty/invalid role denial, malformed options, Steam IDs, durations and slash-command definitions. Discord administrator permissions do not bypass configured roles.

The [Unity/Mono integration probe](MonoSmoke/README.md) loads the production Harmony patches and empty-token BepInEx configuration. Three in-memory game peers exercise native routing. A memory HTTP handler simulates Discord acknowledgements/replies and command registration; it never contacts Discord. Checks include:

- Public shouts only; all three game chat types in the admin log; public Discord mirroring and one-way admin behavior.
- Unchanged vanilla routing, recipient coalescing, intentional repeats, client guards, sender spoofing and incoming-message deduplication.
- Per-recipient vanilla Discord chat packets and unchanged player rosters.
- Native client routing reproduces local-only chat with no server packet, then verifies that a second-recipient chat packet traverses the server and reaches the production Discord relay.
- Persisted death counting, duplicate/spoof rejection, restart reload, and importing offline world-history accounts without double-counting Steam players.
- Wrong-role/wrong-channel denials, ephemeral responses, waiting for successful acknowledgement, failed acknowledgements, disconnects, duplicate commands, ambiguous names and non-Steam moderation refusal.
- Actual native `Kicked` RPC and Valheim `SyncedList` persistence of the resolved Steam ID in an isolated ban file.
- Native global center-screen announcement RPC and one-recipient distant-teleport RPC with vanilla spawn offset.
- Stats/online embed fields and cleared plaintext content, suppressed mentions, six guild command upserts preserving unrelated commands, and shutdown cleanup.

The teleport fixture supplies a deterministic world-spawn lookup result; it validates the production lookup name and RPC payload, not arrival in a fully generated world. Discord readiness and role payloads are simulated. These checks do not prove real command visibility, actual Discord bot permissions, client HUD rendering, teleport arrival, or gameplay reconnect refusal after a ban.

The owner confirmed the previous 0.1.0 bidirectional bridge working in a real authenticated gameplay session. The 0.2.x features still require the live acceptance checks in [README.md](README.md). No real token was used or included in the probe/package.

Run policy checks without game files:

```powershell
dotnet restore Examples/ValheimChatBridge/Tests/ChatPolicyTests.csproj --configfile NuGet.Config
dotnet run --project Examples/ValheimChatBridge/Tests/ChatPolicyTests.csproj -c Release
```

Run native checks using your own game installation:

```powershell
dotnet restore Examples/ValheimChatBridge/NativeTests/NativeTests.csproj --configfile NuGet.Config
dotnet build Examples/ValheimChatBridge/NativeTests/NativeTests.csproj -c Release --no-restore -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
dotnet Examples/ValheimChatBridge/NativeTests/bin/Release/net8.0/NativeTests.dll
```

The policy/native hosts use .NET 8; the integration probe and production plugin use Unity/Mono. Own game/Unity/BepInEx binaries are required for the integration probe and are not distributed.
