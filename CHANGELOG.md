# Changelog

## Valheim Discord Chat example 0.2.2 — 2026-10-01 (experimental)

- Enable configurable solo chat using an outgoing-only DiscordBot player-list row, with no client plugin or actual player connection.
- Reuse the solo player's account metadata and keep the real row first; preserve native text privileges, name lookup, and account-history deduplication.
- Keep actual server rosters, statistics, moderation targets, and slots unchanged; remove the synthetic recipient when a second ready connection joins or the bridge is inactive.
- Accept and document the extra client player count, F2 row, and possible additional one-per-player drop. Set `Chat.SoloChatRelay = false` to disable the trial.
- Extend isolated Unity/Mono validation through native roster serialization/decoding, permissions, real client routing, Discord queues, command counts, and roster transitions.

## Valheim Discord Chat example 0.2.1 — 2026-10-01

- Format `/stats` as an embed with separate statistic fields, a tracking-start field, and a storage warning only when needed.
- Format `/online` as an embed with a player field per connection, session durations, and an empty-server message. Show up to 25 players within Discord embed limits.
- Remove the redundant offline-process notice from bot statistics replies; retain the operational explanation in documentation.
- Keep moderation replies and permission checks unchanged. Document Embed Links permission for the commands channel.

## Valheim Discord Chat example 0.2.0 — 2026-10-01

- Add a separate one-way administrator channel for shouts, normal chat, whispers, and public Discord messages relayed into the game.
- Register guild slash commands `/stats`, `/online`, `/kick`, `/ban`, `/alert`, and `/setatspawn`, restricted to the configured commands channel.
- Require configured Discord RoleIDs for moderation; use authenticated Steam IDs and native Valheim kick/ban functions.
- Send vanilla center-screen announcements and targeted distant teleports to world spawn without a client plugin.
- Persist world-scoped unique player and death totals; seed unique players from Valheim world history. Deaths are observed from installation onward, including while Discord is disconnected.
- Document server-hosted offline limitations, local-only chat, session duration coverage, configuration, upgrade and live testing.
- Extend offline and Unity/Mono integration coverage for moderation authorization, acknowledgements, persistence and vanilla RPC delivery.
- Add an object overload for deferred interaction replies so consumers can suppress mentions without losing the existing string overload.

## Valheim Discord Chat example 0.1.0 — 2026-09-30

- Add a dedicated-server BepInEx 5 chat bridge with unsafe compilation enabled.
- Relay only Valheim shouts to one Discord channel and human Discord text messages to all connected players.
- Include private local token/channel configuration, bounded queues, loop prevention, nickname fallback, native packet and chat policy tests, and an installation ZIP builder.
- Document the accepted vanilla player-name prefix for incoming Discord messages. A separate administrator channel is deferred until after the initial live tests.

## 2.0.0 — 2026-09-30

This version belongs to the bjorno43/DiscordUnity fork.

- Update Discord REST and Gateway to v10 with explicit intents, reconnect/resume supervision, and cancellable heartbeats.
- Correct frame assembly, UTF-8 decoding, protocol payload types, startup/stop behavior, and main-thread callbacks.
- Add consistent REST snake case, successful empty 204 responses, structured errors, rate limits, and multipart uploads.
- Correct member, position, message, ban, and pin endpoints and their current payload formats.
- Use 64-bit permissions/timestamps, accept optional models, update user flags, and preserve partial caches safely.
- Add application commands and Gateway interactions with a separate acknowledgement queue.
- Update Newtonsoft.Json to 13.0.4 while retaining .NET Standard 2.0 and C# 7.3 for the production assembly.
- Add offline regression hosts for .NET 8/Mono, real Unity/Mono network validation, and a buildable BepInEx example.
- Provide English documentation, migration guidance, validation results, and runtime/source packaging.
- Add GitHub Actions builds, offline regression checks, and downloadable build artifacts.
- Remove the obsolete upstream Unity package and unused token configuration template.

**Breaking changes:** permissions/overwrites and activity timestamps use wider numeric types; overwrite `Type` is numeric. Existing mods must be rebuilt. See [README.md](README.md) for migration details.
