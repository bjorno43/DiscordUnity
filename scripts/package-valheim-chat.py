"""Package the dedicated-server chat plugin without credentials or game assemblies."""
from pathlib import Path
from hashlib import sha256
import json
import zipfile

repo = Path(__file__).resolve().parents[1]
example = repo / "Examples/ValheimChatBridge"
plugin = example / "bin/Release/netstandard2.1/ValheimDiscordChat.dll"
library = repo / "src/DiscordUnity/bin/Release/netstandard2.0"
files = {
    "BepInEx/plugins/ValheimDiscordChat/ValheimDiscordChat.dll": plugin,
    "BepInEx/plugins/ValheimDiscordChat/DiscordUnity.dll": library / "DiscordUnity.dll",
    "BepInEx/plugins/ValheimDiscordChat/Newtonsoft.Json.dll": library / "Newtonsoft.Json.dll",
    "README.md": example / "README.md",
    "TESTING.md": example / "TESTING.md",
    "MonoSmoke/README.md": example / "MonoSmoke/README.md",
    "icecub.ValheimDiscordChat.cfg.example": example / "icecub.ValheimDiscordChat.cfg.example",
    "LICENSE": repo / "LICENSE",
    "THIRD-PARTY-NOTICES.md": repo / "THIRD-PARTY-NOTICES.md",
}
for name, path in files.items():
    if not path.is_file():
        raise SystemExit("Build the chat plugin in Release first: " + str(path))
manifest = {
    "version": "0.2.0",
    "pluginGuid": "icecub.ValheimDiscordChat",
    "targetFramework": "netstandard2.1",
    "discordUnityTargetFramework": "netstandard2.0",
    "allowUnsafeBlocks": True,
    "serverOnly": True,
    "sha256": {name: sha256(path.read_bytes()).hexdigest() for name, path in files.items()},
}
artifacts = repo / "artifacts"
artifacts.mkdir(exist_ok=True)
output = artifacts / "ValheimDiscordChat-0.2.0.zip"
with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED) as archive:
    for name, path in files.items():
        archive.write(path, name)
    archive.writestr("BUILD-MANIFEST.json", json.dumps(manifest, indent=2) + "\n")
print(f"{output.name}: {output.stat().st_size} bytes; SHA256 {sha256(output.read_bytes()).hexdigest()}")
