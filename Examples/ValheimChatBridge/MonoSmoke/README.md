# Isolated Unity/Mono integration probe

This is the source of the 54-check integration probe described in [TESTING.md](../TESTING.md). It requires a separate compatible Mono Unity Player test host, your own dedicated-server managed assemblies, and BepInEx 5. No game binaries, Unity Player, or BepInEx binaries are distributed here.

Build with `ManagedPath` pointing to the dedicated server's `valheim_server_Data/Managed` and `BepInExPath` to its `BepInEx/core`:

```powershell
dotnet restore Examples/ValheimChatBridge/MonoSmoke/MonoSmoke.csproj --configfile NuGet.Config
dotnet build Examples/ValheimChatBridge/MonoSmoke/MonoSmoke.csproj -c Release --no-restore -p:ValheimPath="C:\path\to\dedicated-server"
```

Load the probe, plugin, DiscordUnity, Newtonsoft.Json, BepInEx/Harmony dependencies, and your own game assemblies in the isolated Unity Player. Register `ChatBridgeProbe.Run` as its runtime initialization method. Set `VALHEIM_DISCORD_CHAT_PROBE` to a writable result directory. The fixture assumes a test host named `runtime/ValheimLab.exe` beneath that directory; its BepInEx config is isolated under `isolated-bepinex/`. It writes `mono-result.json` and exits with 0 on success.

The fixture intentionally creates inactive game components, scripted in-memory sockets, and simulated Discord readiness and memory HTTP responses for interactions and command registration. **Use it only in an isolated test host**, never in a live server or client. It is excluded from the production plugin and installation package. No real token, bot login, Steam connection, or gameplay session is required.
