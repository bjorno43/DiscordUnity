# Chat bridge validation

Validated on September 30, 2026:

| Check | Result |
|---|---|
| Release plugin build against installed Valheim client 1.0.16 assemblies | Passed, 0 warnings / 0 errors |
| Release plugin build against installed dedicated server 1.0.12 assemblies | Passed, 0 warnings / 0 errors |
| Chat policy suite on .NET 8 | Passed, 32 assertions |
| Native packet suite using installed client game assemblies on .NET 8 | Passed, 13 assertions |
| Isolated Unity Player / Mono using actual dedicated server 1.0.12 assemblies and the production plugin | Passed, 23 integration checks |

The native suite uses the game's `ZRpc.Serialize`, `UserInfo`, and `ZRoutedRpc.RoutedRPCData` serialization. It checks shout decoding, untouched vanilla bytes/read position, rejection of spoofed sender IDs or other players' objects, exclusion of normal chat/whispers/pings, and tolerance of malformed/oversized packets. The policy suite covers nickname fallback, channel/guild filtering, bot/webhook loops, readable mentions, control characters, Unicode truncation, Discord length/markdown constraints, bounded duplicate tracking, and per-recipient coalescing that preserves intentional repeats.

The [Unity/Mono integration probe](MonoSmoke/README.md) installs the actual production Harmony patches, generates the BepInEx configuration with an empty token, and routes native packets through three in-memory peers. It verifies authenticated shout capture, untouched vanilla delivery, recipient coalescing, intentional repeats, client guards, exclusion of normal/whisper/ping traffic, Discord event deduplication, per-recipient vanilla response packets with complete Discord text, unchanged player rosters, and queue cleanup. Discord connection readiness is simulated; no HTTP connection or bot login occurs in this probe.

Run the policy suite without game files:

```powershell
dotnet restore Examples/ValheimChatBridge/Tests/ChatPolicyTests.csproj --configfile NuGet.Config
dotnet run --project Examples/ValheimChatBridge/Tests/ChatPolicyTests.csproj -c Release
```

Run native packet checks using your own installed game assemblies:

```powershell
dotnet restore Examples/ValheimChatBridge/NativeTests/NativeTests.csproj --configfile NuGet.Config
dotnet build Examples/ValheimChatBridge/NativeTests/NativeTests.csproj -c Release --no-restore -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
dotnet Examples/ValheimChatBridge/NativeTests/bin/Release/net8.0/NativeTests.dll
```

The policy/packet hosts use .NET 8; the integration probe and plugin run on Unity/Mono. These checks do not prove real bot login, platform-specific chat permission behavior, or rendering on connected players. No real bot token or Discord message was used. Follow the live test steps in [README.md](README.md) on your test server. An administrator channel is outside this first test version.
