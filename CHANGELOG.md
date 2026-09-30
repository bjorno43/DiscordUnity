# Changelog

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
