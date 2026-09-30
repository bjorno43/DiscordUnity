# Minimal Valheim/BepInEx example

This example demonstrates an embedded Discord bot's lifecycle. It is disabled by default and only responds to `!ping` in a configured channel and a previously registered `/ping` interaction.

```powershell
dotnet restore Examples/ValheimBot/ValheimBot.csproj --configfile NuGet.Config
dotnet build Examples/ValheimBot/ValheimBot.csproj -c Release --no-restore -p:ValheimPath="C:\Program Files (x86)\Steam\steamapps\common\Valheim"
```

Alternatively, provide `ManagedPath` and `BepInExPath`. The example targets .NET Standard 2.1 to match the current Valheim assemblies and references DiscordUnity targeting .NET Standard 2.0. BepInEx 5 and the game assemblies must be available.

Place the built example plugin and both library DLLs in a dedicated subfolder of `BepInEx/plugins`. Check whether a compatible Newtonsoft.Json 13 assembly is already loaded before adding another copy.

On first launch, BepInEx creates `BepInEx/config/example.discordunity.valheim.cfg`. Set `GuildId`, `ChannelId`, and `Enabled`. Supply the bot token locally through `BotToken` or the `DISCORD_BOT_TOKEN` environment variable of the Valheim process.

For `!ping`, set `TextCommands=true` and enable Message Content Intent in Discord. For `/ping`, register the command once through `DiscordAPI.CreateApplicationCommand(applicationId, new { name = "ping", description = "Check the bot" }, guildId)`, for example in your own startup task after `StartWithBot` completes. This example does not register commands automatically.

`Update` keeps running during startup, and `OnDestroy` stops the connection. Perform Unity game actions on that main thread. Add explicit user/role authorization before implementing game administration commands. Assign one owner to the static DiscordUnity connection per process.

The example has been built against Valheim assemblies, but a real authenticated bot session and gameplay integration remain to be tested. See [validation results](../../validation/RESULTS.md).
