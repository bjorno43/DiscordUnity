# Valheim Discord Chat 0.2.2 (experimental solo chat)

A chat and moderation plugin for a **dedicated Valheim server with BepInEx 5**. Install it only on the server; players use unmodified clients. The project enables `AllowUnsafeBlocks`, uses C# 7.3 and targets .NET Standard 2.1 to match Valheim. DiscordUnity remains .NET Standard 2.0 and Unity/Mono compatible.

## Channels

| Configuration | Behavior |
|---|---|
| `Discord.ChannelId` | Public bidirectional chat. Only game shouts (`/s`) go to Discord as `[Valheim] Playername: Message`. Human Discord text goes to every connected, spawned player. |
| `Discord.AdminChannelId` | Optional separate one-way log of shouts, normal chat and whispers, with the chat type in the prefix. Also mirrors public Discord messages relayed into the game. Posts in this channel never enter the game. |
| `Discord.CommandsChannelId` | Optional separate channel where the six slash commands below may be executed. Ordinary messages are ignored. |

All channels must be different guild text or announcement channels in the **same Discord server**. Leave optional IDs empty to disable those features. Pings, edits, attachments alone, system messages and other console commands are excluded. Discord names prefer nickname, then global display name, then username. Bot/webhook messages are ignored. Mentions cannot trigger notifications. Markup/control characters are flattened and long text truncated. Each chat queue holds 100 messages; new messages are dropped when full. There is no offline chat replay.

**Admin-log coverage:** chat passing through the server is captured regardless of recipient distance. Vanilla can keep solo chat local, including `/s` shouts. This experimental release enables `Chat.SoloChatRelay` by default to make a spawned solo client send its native chat through the server. Whispers passing through the server are visible to administrators. The client's text-communication privilege must still be granted.

**Vanilla incoming title:** Valheim 1.0.16 resolves names from existing players, ignoring an arbitrary `UserInfo.Name`. Incoming Discord chat appears approximately as `YourPlayername: [Discord] Nickname: Message`. This accepted limitation requires no client plugin. The real player remains first in the client roster so platform-account name lookups continue to resolve that player.

## Experimental solo chat

When exactly one ready, connected player has spawned, the bridge is ready, and game-to-Discord or admin logging is enabled, the outgoing native `PlayerList` packet includes an additional row named **DiscordBot**. It copies the solo player's account metadata but has a different network recipient ID. The native self-account permission check permits this recipient without looking up a fabricated Steam or crossplay account. Native client routing sends the extra recipient's chat through the actual server, where the existing bridge captures it.

DiscordBot has no connection, character object, AI, world position or public map marker. The server's real player list and world history are never extended. `/stats`, `/online`, moderation targets and server slots therefore continue to count real connections. Normal multiplayer packets are unchanged. DiscordBot is removed on the next native player-list update when another ready connection joins, the server becomes empty, or the bridge is no longer ready. An unspawned second ready connection also disables the synthetic recipient. A returning solo player gets it again after spawning.

Accepted side effects: the solo client counts two players and its F2 connection panel shows DiscordBot. Drops configured as one per player may produce one additional item, regardless of distance; this release deliberately leaves that behavior unchanged. The copied account remains one identity in native history. Other client mods that count roster rows can also count DiscordBot. No client plugin or BetterChat update is required; BetterChat 0.1.7 Global/Local/Whisper use this native chat route. Its separate Private/Admin relay is outside this bridge.

Set `Chat.SoloChatRelay = false` and restart to restore the original player-list packets and local-only solo chat. This feature is a gameplay trial: isolated native/Mono checks pass, but live Steam/crossplay sessions and actual F2 rendering still require acceptance testing.

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
2. Extract `ValheimDiscordChat-0.2.2.zip` into the server root, replacing the three DLLs under `BepInEx/plugins/ValheimDiscordChat/`. Remove duplicate older copies elsewhere.
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
SoloChatRelay = true
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

1. Join alone with an unmodified client and wait for `Experimental solo chat active`. Check F2 shows your character plus DiscordBot, while `/stats` and `/online` show only you. Send a shout, normal chat and whisper: public Discord gets only the shout; admin Discord gets all three once. Your text-communication privilege must permit chat.
2. Send public Discord text. Both clients and admin log receive it. Admin/commands-channel posts do not enter game chat.
3. Run `/stats` and `/online`. Check count, names, day, version and advancing uptime/durations.
4. Die once, check `/stats`, restart and verify the count persists. Pre-installation deaths are excluded.
5. Try moderation with an unlisted role and in the wrong channel; no action should occur. Repeat using an allowed role.
6. Test `/alert` on both clients and `/setatspawn` on one. Verify arrival at the sacrifice stones and other players remain in place.
7. Kick a test account, reconnect, then ban it. Confirm the Steam ID in the native ban list and reconnect refusal. Unban through Valheim before normal play.
8. Join with a second client whose platform text-communication permissions allow messages between the two players. DiscordBot disappears after the next native roster update. Repeat the three chat types, then disconnect the second client: DiscordBot returns and solo chat still works. Disconnect/reconnect the solo client and repeat. Check that no DiscordBot account appears in persistent server statistics.
9. Set `SoloChatRelay = false`, restart and check F2 has no DiscordBot. Solo chat may remain local again. Restore `true` and restart to resume the trial.

## No game chat appears in Discord

For solo tests, check `SoloChatRelay = true`, a spawned character, a ready Discord bridge, and `Experimental solo chat active` in the server log. Allow the next native player-list update after startup. F2 should show DiscordBot; `/online` must still show only the real player. Then send `/s Test from Valheim`. If the feature is disabled, vanilla solo chat can remain local and the server receives nothing to relay. Platform text-communication privilege denial is still respected.

BetterChat 0.1.7 uses the same vanilla route for Global, Local and Whisper; its separate relay serves Private/Admin. The Discord plugin does not modify BetterChat or require a client extension. A public Discord message appearing in the admin channel is the intended one-way audit copy and does not establish that the game client has transmitted its own chat.

The native Unity/Mono probe reproduces the original local-only route, then exercises the production roster patch, native roster decoding and native chat permission loop. Solo shouts reach the public relay and all three chat types reach the admin relay. It also checks unchanged server counts/history, command embeds, feature disablement and transitions between zero, one and two real players. See [TESTING.md](TESTING.md). Configuration/permission checks remain relevant if a two-player test also fails.

## Build and validation

From the repository root:

```powershell
dotnet restore Examples/ValheimChatBridge/ValheimDiscordChat.csproj --configfile NuGet.Config
dotnet build Examples/ValheimChatBridge/ValheimDiscordChat.csproj -c Release --no-restore -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
python scripts/package-valheim-chat.py
```

Dedicated-server `ValheimPath` automatically selects `valheim_server_Data/Managed`. Explicit `ManagedPath` / `BepInExPath` overrides are supported. The ZIP includes only the plugin, DiscordUnity, Newtonsoft.Json, English docs, blank example config and license notices. Game/BepInEx assemblies and probe binaries are excluded. See [TESTING.md](TESTING.md).
