# Valheim Discord Chat 0.2.1

A chat and moderation plugin for a **dedicated Valheim server with BepInEx 5**. Install it only on the server; players use unmodified clients. The project enables `AllowUnsafeBlocks`, uses C# 7.3 and targets .NET Standard 2.1 to match Valheim. DiscordUnity remains .NET Standard 2.0 and Unity/Mono compatible.

## Channels

| Configuration | Behavior |
|---|---|
| `Discord.ChannelId` | Public bidirectional chat. Only game shouts (`/s`) go to Discord as `[Valheim] Playername: Message`. Human Discord text goes to every connected, spawned player. |
| `Discord.AdminChannelId` | Optional separate one-way log of shouts, normal chat and whispers, with the chat type in the prefix. Also mirrors public Discord messages relayed into the game. Posts in this channel never enter the game. |
| `Discord.CommandsChannelId` | Optional separate channel where the six slash commands below may be executed. Ordinary messages are ignored. |

All channels must be different guild text or announcement channels in the **same Discord server**. Leave optional IDs empty to disable those features. Pings, edits, attachments alone, system messages and other console commands are excluded. Discord names prefer nickname, then global display name, then username. Bot/webhook messages are ignored. Mentions cannot trigger notifications. Markup/control characters are flattened and long text truncated. Each chat queue holds 100 messages; new messages are dropped when full. There is no offline chat replay.

**Admin-log coverage:** chat passing through the server is captured regardless of recipient distance. Valheim can deliver messages only locally without transmitting them to the server when there are no other permitted recipients. A server plugin cannot observe those local-only messages. Whispers passing through the server are visible to administrators.

**Vanilla incoming title:** Valheim 1.0.16 resolves names from existing players, ignoring an arbitrary `UserInfo.Name`. Incoming Discord chat appears approximately as `YourPlayername: [Discord] Nickname: Message`. This accepted limitation requires no client plugin and does not change the player roster.

## Commands

Use Discord's slash-command picker. Ordinary text containing `/stats` does not invoke the bot. Commands are registered in the configured channel's guild without replacing unrelated application commands.

| Command | Result | Access |
|---|---|---|
| `/stats` | Online count, unique players, recorded deaths, day number, online status, process uptime and Valheim version. | Anyone permitted to use application commands in the commands channel. |
| `/online` | Online names and observed connection durations. | Same as `/stats`. |
| `/kick player:<name or SteamID>` | Resolves the exact online name to an authenticated Steam ID and invokes native kick. | Configured moderator role. |
| `/ban player:<name or SteamID>` | Adds the authenticated Steam ID to native `bannedlist.txt` and requests a kick. | Configured moderator role. |
| `/alert message:<text>` | Global center-screen announcement. | Configured moderator role. |
| `/setatspawn player:<name or SteamID>` | Requests a distant teleport to world spawn, normally the sacrifice stones at `StartTemple`. | Configured moderator role. |

`/stats` uses an embed with individual statistic fields and a full-width death-tracking date. `/online` uses one field per player, showing their session duration. An empty server gets a clear message; servers with more than 25 players show the first 25 alphabetically and indicate how many are omitted. Both embeds include an update timestamp.

Moderation requires at least one role in `Commands.AllowedRoleIds`. Empty or invalid configuration denies all moderation, including Discord administrators. Guild, channel and roles are checked before queueing. Interactions are acknowledged promptly; game operations run on Unity's main thread only after successful acknowledgement. Moderation results and denials are ephemeral; `/stats` and `/online` results are visible in the commands channel. Replayed interactions are ignored. The server log records the requesting Discord user for moderation actions.

Player names match exactly, ignoring case. Ambiguous names are refused; use the Steam ID instead. Targets must be online. Kick/ban refuse connections without an authenticated Steam ID, including non-Steam crossplay players, rather than banning a character name. Use Valheim's usual unban facilities to remove bans.

`/setatspawn` uses the vanilla teleport RPC. It preserves character/inventory and does not replace bed spawn. Loading, missing world spawn or a client's existing teleport state may delay/refuse teleport. The bot reports a request, not confirmed arrival; vanilla provides no arrival acknowledgement to this command.

## Statistics and offline behavior

- Unique players are platform accounts in the current world's stored history merged with accounts observed by the plugin. Legacy world history can be incomplete. Each world has its own ledger.
- Native death statistics live in client profiles. The dedicated server cannot recover historical deaths. This plugin counts authenticated native `OnDeath` broadcasts **from installation onward**, deduplicates them and saves each observed death immediately. `/stats` includes the tracking start time. Tracking continues while Discord is disconnected. Client mods that change death events can affect coverage.
- Connection durations start when this plugin observes a ready peer, with approximately one-second resolution. They reset on disconnect/reconnect or restart. Earlier time in a session cannot be reconstructed if the plugin loads midway.
- Uptime is server-process uptime. Day uses Valheim's world clock/day length. Online counts ready connected players, including those still loading their characters.
- The bot lives inside the server process. It reports online while it can answer. After a stop/crash it cannot answer `/stats` with offline status. Reporting offline requires a separately running monitor outside this plugin.

Statistics live in `BepInEx/config/ValheimDiscordChat/<worldUID>.json`, with a previous `.bak`. Back up that directory with the world. Player-history changes save at most every 30 seconds and on orderly plugin shutdown. A corrupt ledger is preserved and writes disabled until repaired. Read/write failures are logged and `/stats` warns that totals may not survive restart. Deleting the ledger resets deaths/start time; world player history is imported again.

## Install or upgrade

1. Stop the server and back up its configuration.
2. Extract `ValheimDiscordChat-0.2.1.zip` into the server root, replacing the three DLLs under `BepInEx/plugins/ValheimDiscordChat/`. Remove duplicate older copies elsewhere.
3. Keep `BepInEx/config/icecub.ValheimDiscordChat.cfg`. BepInEx adds new settings on startup. First installations generate it automatically; a blank `.cfg.example` is included.
4. Fill in channel IDs and moderator RoleIDs, then restart.

```ini
[General]
Enabled = true

[Discord]
BotToken = YOUR_PRIVATE_BOT_TOKEN
ChannelId = YOUR_PUBLIC_CHAT_CHANNEL_ID
AdminChannelId = YOUR_ADMIN_LOG_CHANNEL_ID
CommandsChannelId = YOUR_COMMANDS_CHANNEL_ID

[Chat]
GameToDiscord = true
DiscordToGame = true
MaxMessageLength = 500

[Commands]
AllowedRoleIds = YOUR_ADMIN_ROLE_ID,YOUR_MODERATOR_ROLE_ID
```

`DISCORD_BOT_TOKEN` overrides the token setting. Guild/application IDs are discovered automatically. Get numeric channel and role IDs through Developer Mode and Copy Channel ID / Copy Role ID. Configuration changes require restart. The ZIP never overwrites your actual token file or statistics.

Enable **Message Content Intent** in the [Developer Portal](https://discord.com/developers/applications) for public Discord-to-game text. Give the bot **View Channel** and **Send Messages** in the configured channels, plus **Embed Links** in the commands channel. Install/invite with `bot` and `applications.commands`; users need **Use Application Commands**. Leave **Interactions Endpoint URL empty**, because commands arrive through the Gateway. See Discord's [application command](https://docs.discord.com/developers/interactions/application-commands) and [interaction delivery](https://docs.discord.com/developers/interactions/receiving-and-responding) documentation.

Administrator permission, Guild Members intent and Presence intent are unnecessary. The plugin enforces RoleIDs itself. Optionally restrict command visibility under Server Settings → Integrations → your application, while retaining server RoleIDs. Run one DiscordUnity connection per process; disable the separate ping example. Avoid conflicting library copies from other plugins.

Look for `Discord chat bridge ready`, optional channel validation and command registration in the log. Invalid optional channel access disables that feature while public chat continues. Invalid/distinctness configuration stops startup. HTTP 403 normally means missing access; Gateway 4014 normally means Message Content Intent is disabled. Correct fatal errors and restart. DiscordUnity handles temporary disconnects and HTTP rate limits.

## Live acceptance test

The owner confirmed the 0.1.0 bridge working in gameplay. The 0.2.x features have offline/native/Unity-Mono validation and still need these checks using your real application:

1. Join with two unmodified clients. Send a shout, normal chat and whisper. Public Discord gets only the shout; admin Discord gets all three once.
2. Send public Discord text. Both clients and admin log receive it. Admin/commands-channel posts do not enter game chat.
3. Run `/stats` and `/online`. Check count, names, day, version and advancing uptime/durations.
4. Die once, check `/stats`, restart and verify the count persists. Pre-installation deaths are excluded.
5. Try moderation with an unlisted role and in the wrong channel; no action should occur. Repeat using an allowed role.
6. Test `/alert` on both clients and `/setatspawn` on one. Verify arrival at the sacrifice stones and other players remain in place.
7. Kick a test account, reconnect, then ban it. Confirm the Steam ID in the native ban list and reconnect refusal. Unban through Valheim before normal play.

## Build and validation

From the repository root:

```powershell
dotnet restore Examples/ValheimChatBridge/ValheimDiscordChat.csproj --configfile NuGet.Config
dotnet build Examples/ValheimChatBridge/ValheimDiscordChat.csproj -c Release --no-restore -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
python scripts/package-valheim-chat.py
```

Dedicated-server `ValheimPath` automatically selects `valheim_server_Data/Managed`. Explicit `ManagedPath` / `BepInExPath` overrides are supported. The ZIP includes only the plugin, DiscordUnity, Newtonsoft.Json, English docs, blank example config and license notices. Game/BepInEx assemblies and probe binaries are excluded. See [TESTING.md](TESTING.md).
