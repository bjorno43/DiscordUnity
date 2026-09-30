# Valheim Discord Chat 0.1.0

A minimal bidirectional chat bridge for a **dedicated Valheim server with BepInEx 5**. Install it only on the server; players can use unmodified clients. The project explicitly enables `AllowUnsafeBlocks` and targets .NET Standard 2.1, matching Valheim's assemblies. DiscordUnity itself remains .NET Standard 2.0.

## Behavior

- Valheim to Discord: only shouts (`/s message`) are forwarded, as `[Valheim] Playername: Message`.
- Normal chat, whispers, map pings, and commands are not sent to Discord.
- Discord to Valheim: human text messages from the configured channel are sent to every connected, spawned player, with `[Discord] Nickname: Message` in the body.
- The Discord server nickname is preferred, followed by the global display name and then the username.
- Bot, webhook, and system messages are ignored. Attachments alone, edits, and message history are not forwarded.
- Mentions do not trigger Discord notifications. Line breaks and game markup are flattened, and long messages are truncated.
- Delivery is queued in order, with a maximum of 100 pending messages per direction. New messages are dropped if a queue is full. Queues are cleared on shutdown; there is no offline history replay.

**Vanilla chat limitation in Valheim 1.0.16:** the chat window resolves the title through an existing platform player and ignores an arbitrary `UserInfo.Name`. A server-only bridge therefore sends each recipient a message using that recipient's known identity. The visible line is approximately:

```text
YourPlayername: [Discord] Nickname: Message
```

The extra player name is a vanilla presentation limitation; the Discord author's name remains in the message body. The mod does not change the player list or require a client plugin. The same transport is used in the inspected dedicated server 1.0.12 assemblies.

A second administrator channel capturing all chat is planned for a later iteration and is not implemented in this version.

## Install and configure

1. Stop the dedicated server.
2. Extract `ValheimDiscordChat-0.1.0.zip` into the server directory. It places `ValheimDiscordChat.dll`, `DiscordUnity.dll`, and `Newtonsoft.Json.dll` in `BepInEx/plugins/ValheimDiscordChat/`.
3. Start the server once to generate `BepInEx/config/icecub.ValheimDiscordChat.cfg`. Alternatively, copy the included `.cfg.example` to that path, removing `.example`.
4. Stop the server, fill in `BotToken` and `ChannelId`, and restart it.

```ini
[General]
Enabled = true

[Discord]
BotToken = YOUR_PRIVATE_BOT_TOKEN
ChannelId = YOUR_DISCORD_TEXT_CHANNEL_ID

[Chat]
GameToDiscord = true
DiscordToGame = true
MaxMessageLength = 500
```

`DISCORD_BOT_TOKEN`, when set in the server process environment, overrides `BotToken`. The channel's guild is resolved automatically; no separate guild or application ID is needed. Use Discord Developer Mode and **Copy Channel ID** to obtain the numeric channel ID. This version accepts a guild text or announcement channel, not a forum, thread, voice channel, or DM.

Enable **Message Content Intent** on the application's Bot page in the [Discord Developer Portal](https://discord.com/developers/applications). Discord requires this intent to deliver ordinary guild message content; see the [official Gateway documentation](https://docs.discord.com/developers/events/gateway#message-content-intent). Give the bot **View Channel** and **Send Messages** in the selected channel. It does not need Administrator, Guild Members, or Presence intents.

The plugin starts once a dedicated server's network is available and logs `Discord chat bridge ready` after login and channel validation. It stays inactive if loaded in a client or a hosted non-dedicated game. Configuration changes require a server restart. Run one DiscordUnity connection per process; disable the separate ping example if it is installed. Check for existing compatible Newtonsoft.Json 13 and DiscordUnity assemblies when combining plugins, to avoid conflicting copies.

## First live test

1. Wait for `Discord chat bridge ready` in `BepInEx/LogOutput.log`.
2. Join with an unmodified client and send `/s Hello from Valheim`. Verify one `[Valheim] ...` message in Discord.
3. Send a normal message and a `/w` whisper. Verify neither reaches Discord.
4. Post `Hello from Discord` in the configured Discord channel. Verify it appears in every connected player's game chat with the Discord nickname in the body and the accepted extra player-name prefix.
5. Verify messages in other channels and the bot's own replies are ignored.
6. Shut down and restart the server to check cleanup and reconnect. A real authenticated session and gameplay delivery are the remaining integration checks; no real token is included in the tests or package.

If startup reports close code 4014, check Message Content Intent. HTTP 403 during channel lookup or sending usually indicates missing channel access/permissions. An invalid token or fatal Gateway error stops the bridge; correct the setting and restart the server. HTTP rate limits and temporary Gateway disconnects are handled by DiscordUnity.

## Build and test

From the repository root:

```powershell
dotnet restore Examples/ValheimChatBridge/ValheimDiscordChat.csproj --configfile NuGet.Config
dotnet build Examples/ValheimChatBridge/ValheimDiscordChat.csproj -c Release --no-restore -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
python scripts/package-valheim-chat.py
```

For a dedicated-server installation, set `ManagedPath` to `valheim_server_Data/Managed` and `BepInExPath` to `BepInEx/core`. The package builder uses the normal Release output from the project. It includes only the plugin, DiscordUnity, Newtonsoft.Json, documentation, a blank example configuration, and license notices; game/BepInEx assemblies are excluded.

See [TESTING.md](TESTING.md) for the completed offline and native packet checks. The library's network/Mono validation is documented separately in [DiscordUnity's validation results](https://github.com/bjorno43/DiscordUnity/blob/master/validation/RESULTS.md).
