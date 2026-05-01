# Koto FFXIV Repo
Custom Dalamud plugin repository.

Included plugins:

- `GlamSpy`: Adds a context menu action to open hovered items from character inspect windows in an external wiki.
- `OofsPlugin` (`OOFs!`): Plays the OOFs! sound on fall and/or death, with configurable behavior and custom audio support.

## Codex build notes

Preferred build command:

```bash
dotnet build OofPlugin.sln
```

If `dotnet` is not installed but Arch/CachyOS package cache contains the .NET 10 packages, Codex can build without changing the system install by extracting them to `/tmp`:

```bash
mkdir -p /tmp/codex-dotnet
bsdtar -xf /var/cache/pacman/pkg/dotnet-host-10.0.4.sdk104-1-x86_64.pkg.tar.zst -C /tmp/codex-dotnet
bsdtar -xf /var/cache/pacman/pkg/dotnet-runtime-10.0.4.sdk104-1-x86_64.pkg.tar.zst -C /tmp/codex-dotnet
bsdtar -xf /var/cache/pacman/pkg/dotnet-targeting-pack-10.0.4.sdk104-1-x86_64.pkg.tar.zst -C /tmp/codex-dotnet
bsdtar -xf /var/cache/pacman/pkg/dotnet-sdk-10.0.4.sdk104-1-x86_64.pkg.tar.zst -C /tmp/codex-dotnet
env DOTNET_ROOT=/tmp/codex-dotnet/usr/share/dotnet /tmp/codex-dotnet/usr/share/dotnet/dotnet build OofPlugin.sln
```

The first restore needs NuGet network access for `Dalamud.NET.Sdk` and NAudio packages. The Dalamud SDK resolves against the local XIVLauncher/Dalamud install at `~/.xlcore/dalamud/Hooks/dev/`.
