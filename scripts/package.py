"""Package only the updated runtime and reviewable source; never include local config."""
from pathlib import Path
from hashlib import sha256
import json
import zipfile

repo = Path(__file__).resolve().parents[1]
artifacts = repo / "artifacts"
artifacts.mkdir(exist_ok=True)
version = "2.0.0"
docs = ["README.md", "AUDIT.md", "CHANGELOG.md", "LICENSE", "THIRD-PARTY-NOTICES.md",
        "validation/README.md", "validation/RESULTS.md", "Examples/ValheimBot/README.md",
        "Examples/ValheimChatBridge/README.md", "Examples/ValheimChatBridge/TESTING.md",
        "Examples/ValheimChatBridge/MonoSmoke/README.md"]
runtime = {
    "lib/netstandard2.0/DiscordUnity.dll": repo / "src/DiscordUnity/bin/Release/netstandard2.0/DiscordUnity.dll",
    "lib/netstandard2.0/Newtonsoft.Json.dll": repo / "src/DiscordUnity/bin/Release/netstandard2.0/Newtonsoft.Json.dll",
}
for path in runtime.values():
    if not path.is_file():
        raise SystemExit("Build Release first: " + str(path))
manifest = {
    "version": version, "kind": "fork", "repository": "https://github.com/bjorno43/DiscordUnity", "targetFramework": "netstandard2.0",
    "upstreamCommit": "09cdfb90b2501f7f73e08852d9ced361dc8bd847",
    "sha256": {name: sha256(path.read_bytes()).hexdigest() for name, path in runtime.items()},
}
with zipfile.ZipFile(artifacts / f"DiscordUnity-{version}-UnityMono.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for name, path in runtime.items():
        archive.write(path, name)
    for name in docs:
        archive.write(repo / name, name)
    archive.writestr("BUILD-MANIFEST.json", json.dumps(manifest, indent=2) + "\n")

sources = set(docs + [".gitignore", ".github/workflows/ci.yml", "NuGet.Config", "src/DiscordUnity.sln", "scripts/package.py",
                     "scripts/package-valheim-chat.py", "Examples/ValheimChatBridge/icecub.ValheimDiscordChat.cfg.example",
                     "validation/Run-UnitySmoke.ps1", "validation/UnitySmoke/Assets/Editor/DiscordUnitySmoke.cs",
                     "validation/UnitySmoke/ProjectSettings/ProjectVersion.txt", "validation/UnitySmoke/Packages/manifest.json"])
for folder in ["src/DiscordUnity", "src/DiscordUnityTests", "Examples/ValheimBot", "Examples/ValheimChatBridge"]:
    for path in (repo / folder).rglob("*"):
        if path.is_file() and path.suffix in {".cs", ".csproj"} and not {"bin", "obj"}.intersection(path.relative_to(repo).parts):
            sources.add(path.relative_to(repo).as_posix())
with zipfile.ZipFile(artifacts / f"DiscordUnity-{version}-source.zip", "w", zipfile.ZIP_DEFLATED) as archive:
    for name in sorted(sources):
        archive.write(repo / name, f"DiscordUnity-{version}/{name}")
    archive.writestr(f"DiscordUnity-{version}/BUILD-MANIFEST.json", json.dumps(manifest, indent=2) + "\n")
for name in [f"DiscordUnity-{version}-UnityMono.zip", f"DiscordUnity-{version}-source.zip"]:
    path = artifacts / name
    print(f"{name}: {path.stat().st_size} bytes; SHA256 {sha256(path.read_bytes()).hexdigest()}")
