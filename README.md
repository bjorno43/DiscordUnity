# DiscordUnity 2.0.0

A Discord bot library for Unity/Mono applications and Valheim mods. This fork of [DiscordUnity/DiscordUnity](https://github.com/DiscordUnity/DiscordUnity) updates the original library for Discord REST and Gateway v10 while retaining its Unity runtime requirements.

The **library targets .NET Standard 2.0 and C# 7.3**, using `HttpClient`, `ClientWebSocket`, and Newtonsoft.Json 13.0.4. It does not require a modern .NET runtime in the game. The original MIT license and attribution are preserved. Version 2.0.0 belongs to [this fork](https://github.com/bjorno43/DiscordUnity), rather than the original upstream project.

See [AUDIT.md](AUDIT.md) for the findings, changes, and limitations, and [validation/RESULTS.md](validation/RESULTS.md) for the completed validation.

## Build and install

```powershell
dotnet restore src/DiscordUnity/DiscordUnity.csproj --configfile NuGet.Config
dotnet build src/DiscordUnity/DiscordUnity.csproj -c Release --no-restore
```

Use `DiscordUnity.dll` and `Newtonsoft.Json.dll` from `src/DiscordUnity/bin/Release/netstandard2.0`. In a Unity project, place them in `Assets/Plugins` and use the Mono scripting backend with an API compatibility profile that supports .NET Standard 2.0. In a BepInEx mod, distribute the library alongside your plugin.

Check the Newtonsoft.Json version already loaded by your application. The old 12.0.3 dependency has been replaced with 13.0.4; avoid conflicting copies across mods. The outdated upstream `DiscordUnity.unitypackage` has been removed from this fork. Install the updated DLLs or build from source.

To generate runtime and source ZIP packages after a Release build:

```powershell
python scripts/package.py
```

The archives are written to `artifacts/`, with SHA-256 checksums in `BUILD-MANIFEST.json`. Local configuration, tokens, Unity caches, and the historical upstream package are excluded.

The GitHub Actions workflow builds the library and both test hosts, runs the offline suite on .NET 8 and .NET Framework, and uploads these packages as build artifacts. Unity/Mono and Valheim validation require the local tools described below.

## Unity lifecycle

```csharp
// Start on the Unity main thread, for example in Awake or Start.
var options = new DiscordBotOptions
{
    Intents = GatewayIntents.Guilds | GatewayIntents.GuildMessages
};
Task<bool> startup = DiscordAPI.StartWithBot(botToken, options);

// Call every frame from MonoBehaviour.Update, including during startup.
DiscordAPI.Update();

// Stop when the owner of the connection shuts down.
DiscordAPI.Stop();
DiscordAPI.Update();
```

`StartWithBot` returns `true` once READY has been processed on the main thread. Keep that thread running; calling `.Wait()` or `.Result` on an unfinished task will prevent queued work from completing. Events, cache changes, and result transformations in the existing REST wrappers are processed through `Update`. When using `await`, the caller's synchronization context determines where its continuation runs. Transport work runs on background tasks.

There is one static bot connection per process. Register handlers, call `Update`, and read caches on the same Unity main thread. REST calls are asynchronous. Stopping completes pending startup and REST results as well.

## Discord configuration

Create a bot in the [Discord Developer Portal](https://discord.com/developers/applications) and invite it to your server with the `bot` scope and the necessary channel permissions. Application commands use `applications.commands`, which is also included with the bot scope.

The default intents are `Guilds`, `GuildMessages`, and `DirectMessages`. Privileged intents are opt-in. For text commands such as `!ping` in guild channels, add `GatewayIntents.MessageContent` and enable **Message Content Intent** on the application's Bot page. Similarly, enable `GuildMembers` or `GuildPresences` when you need that data. Missing intents can cause empty message content or Gateway close code 4014. See the [Discord Gateway documentation](https://docs.discord.com/developers/events/gateway).

Keep the bot token in local configuration or a process environment variable. Exclude it from distributed plugins and source code. For a Valheim mod, the token belongs to the administrator of the process running the bot.

## Messages and files

```csharp
var result = await DiscordAPI.CreateMessage(channelId, new DiscordMessageOptions
{
    Content = "Valheim server started!",
    Embeds = new object[] { new { description = "Welcome, Vikings." } },
    AllowedMentions = new { parse = new string[0] }
});

if (!result)
{
    var error = result.Exception as DiscordHttpException;
    // StatusCode, DiscordCode, and ResponseBody contain error details.
}
```

Upload files through `DiscordMessageOptions.Files` using `DiscordFile`, with a `Filename`, byte-array `Content`, and optional `ContentType`. Multipart bodies are rebuilt when a request is retried after a 429. The original extended `CreateMessage` overload remains available: `embed` is translated to `embeds`, and `file` must now be a `DiscordFile` or `DiscordFile[]`. Discord determines the allowed file size.

## Slash commands

```csharp
await DiscordAPI.CreateApplicationCommand(applicationId,
    new { name = "ping", description = "Check the Valheim bot" }, guildId);

DiscordAPI.InteractionCreated += interaction =>
{
    if (interaction.Type == 2 && (string)interaction.Data?["name"] == "ping")
        _ = Reply(interaction);
};

async Task Reply(DiscordInteraction interaction)
{
    var reply = await interaction.Respond("Pong!", ephemeral: true);
    // Check reply.Success and handle failures.
}
```

Respond to an interaction within three seconds. For longer work, first call `await interaction.Defer()`, then `EditOriginalResponse`. Acknowledgements use a separate REST client so ordinary requests waiting for a global rate limit do not block the initial response. Keep calling `Update`. Original responses and follow-ups use the interaction token; their endpoints do not receive a bot authorization header. See [Discord interactions](https://docs.discord.com/developers/interactions/receiving-and-responding).

For a mod that performs game actions, implement authorization checks for the guild, channel, user, and roles. The included example only responds to ping commands.

## Migration from upstream

| Area | Change |
|---|---|
| REST and Gateway | Explicit v10 instead of an implicit API default or Gateway v6 |
| Permissions | `ulong`/`ulong?`, serialized as decimal strings |
| Channel overwrites | Integer `Type` (0 = role, 1 = member); `ulong` `Allow` and `Deny` |
| Activity timestamps | `long` milliseconds since the Unix epoch |
| Guild features | `FeatureNames` preserves all strings; `Features` exposes recognized legacy enum values |
| Emoji roles and message role mentions | Snowflake IDs instead of role objects |
| Partial caches | `Server`, `Channel`, and `Owner` may be absent; a complete member list is not guaranteed |
| Pins | Paginated `/messages/pins` through `GetChannelPins`; `GetPinnedMessages` retrieves all pages |
| Usernames | `GlobalName`/`DisplayName`; the discriminator may be `"0"` |
| Errors | HTTP status and Discord error code available; empty 204 responses succeed |
| User/OAuth2 methods | Methods unsuitable for bot tokens fail locally with `NotSupportedException` |

Rebuild existing mods to accommodate these changes. `StartWithBot(string)` and the existing event interfaces remain available. Some modern Discord features have no typed wrapper; `GatewayEventReceived` exposes every dispatch event on the main thread. Avoid logging entire raw payloads because some events contain tokens. MESSAGE_UPDATE and MESSAGE_DELETE provide partial message objects, and the library does not maintain a complete message history.

Use a `JObject` containing `JValue.CreateNull()` to explicitly clear a field; ordinary optional null properties are omitted. `ModifyServerMember(guildId, userId, object edits)` supports this for actions such as disconnecting a member from voice or removing a timeout.

## Valheim example and validation

[Examples/ValheimBot](Examples/ValheimBot/README.md) contains a minimal BepInEx 5 plugin, disabled by default. The example targets .NET Standard 2.1 to match the current Valheim assemblies; the DiscordUnity library remains .NET Standard 2.0.

[Valheim Discord Chat](Examples/ValheimChatBridge/README.md) is a dedicated-server BepInEx plugin with public shout chat, a one-way administrator chat log, and guild slash commands for statistics, online players, role-controlled kick/ban, global alerts, and teleport to world spawn. It requires no client mod and includes persistent world statistics, offline/native/Unity-Mono tests, and an installation package builder. See its README for the accepted vanilla player-name prefix on incoming Discord messages.

```powershell
dotnet restore src/DiscordUnity.sln --configfile NuGet.Config
dotnet build src/DiscordUnity.sln -c Release --no-restore
dotnet run --project src/DiscordUnityTests -f net8.0 -c Release --no-build
```

Tests require no bot token. Building the `net472` test host requires .NET Framework 4.7.2 reference assemblies; the resulting executable can run under Mono. On systems without those reference assemblies, build only the .NET 8 host:

```powershell
dotnet build src/DiscordUnityTests/DiscordUnityTests.csproj -c Release -f net8.0
```

The optional `--network-smoke` argument retrieves only the public Gateway URL and HELLO. See [validation/README.md](validation/README.md) for Unity validation instructions and [validation/RESULTS.md](validation/RESULTS.md) for the scope of the results.

Voice audio, sharding, and user login are not implemented. The server-only chat bridge has been confirmed working in an authenticated gameplay session by its server owner. The new moderation commands and administrator channel still need live verification with your bot, Discord roles, and connected clients.
