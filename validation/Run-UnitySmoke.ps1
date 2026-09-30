param([Parameter(Mandatory = $true)][string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $PSScriptRoot 'UnitySmoke'
$plugins = Join-Path $project 'Assets\Plugins'
New-Item -ItemType Directory -Force -Path $plugins | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'src\DiscordUnity\bin\Release\netstandard2.0\DiscordUnity.dll') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $repo 'src\DiscordUnity\bin\Release\netstandard2.0\Newtonsoft.Json.dll') -Destination $plugins
Copy-Item -LiteralPath (Join-Path $repo 'src\DiscordUnityTests\bin\Release\net472\DiscordUnityTests.exe') -Destination (Join-Path $plugins 'DiscordUnityTests.dll')
$log = Join-Path $PSScriptRoot 'unity-smoke.log'
$arguments = '-batchmode -nographics -disable-assembly-updater -projectPath "' + $project + '" -executeMethod DiscordUnitySmoke.Run -logFile "' + $log + '"'
$process = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
Write-Output "Unity smoke process: $($process.Id)"
Write-Output "Log: $log"
