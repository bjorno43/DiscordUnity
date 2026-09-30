# Running validation

```powershell
dotnet restore src/DiscordUnity.sln --configfile NuGet.Config
dotnet build src/DiscordUnity.sln -c Release --no-restore
dotnet src/DiscordUnityTests/bin/Release/net8.0/DiscordUnityTests.dll
```

The Mono test executable is `src/DiscordUnityTests/bin/Release/net472/DiscordUnityTests.exe`. Building that target requires .NET Framework 4.7.2 reference assemblies. All regular tests use scripted HTTP/WebSocket transports and a recognizable fake token. They require no real bot access or configuration.

To build only the .NET 8 host on a system without .NET Framework reference assemblies:

```powershell
dotnet build src/DiscordUnityTests/DiscordUnityTests.csproj -c Release -f net8.0
```

Add `--network-smoke` for a public REST/WSS check. It fetches `/api/v10/gateway` and reads Gateway HELLO without sending Identify or a bot token. Normal certificate validation remains enabled.

A standalone `mono.exe` from a Unity installation is not a complete Unity host and may lack initialized Unity TLS support. Run the network check through the actual editor:

```powershell
.\validation\Run-UnitySmoke.ps1 -UnityEditor "C:\path\to\Unity\Editor\Unity.exe"
```

The script copies the built library, Newtonsoft.Json, and net472 test host into the included separate UnitySmoke project, then starts Unity hidden in batch mode. The project was created for Unity 6000.0.75f1. A usable Unity installation and editor license are required.

The script returns after starting the process. Wait for that process to exit, then inspect `validation/unity-smoke.log` for `DiscordUnity Unity/Mono validation exit code: 0`. Both the offline suite and public live network check must report PASS. The smoke project does not exercise Valheim gameplay.
