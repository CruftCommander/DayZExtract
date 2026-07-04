# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

**DayZExtract** is a .NET 10 CLI tool (internal project name `KuruExtract`, assembly name `DayZExtract`) for extracting game content from DayZ PBO archives. It is a faster alternative to DayZ Tools Extract / DayZ2P by Mikero, with parallel extraction, Steam auto-detection, Velopack self-updating, and a Spectre.Console rich UI.

## Build & Run Commands

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download). Targets `x64` only.

```bash
# debug build
dotnet build KuruExtract.slnx

# release build
dotnet build KuruExtract.slnx -c Release

# run directly (pass -- before any app options)
dotnet run --project src/KuruExtract/KuruExtract.csproj -- [options]

# publish as AOT single-file exe (output: publish/)
dotnet publish KuruExtract.slnx -c Release -r win-x64
```

There are no tests in this project.

### Packaging (Velopack)

Version is defined in `Publish.props` under `<Version>`. The `<ReleaseType>` property appends a pre-release suffix if non-empty.

```bash
# install vpk CLI (version must match release.yml — currently 1.2.0)
dotnet tool install -g vpk --version 1.2.0

dotnet publish KuruExtract.slnx -c Release -r win-x64
vpk pack -u DayZExtract -v <version> -o releases -p publish --mainExe DayZExtract.exe --packAuthors Wardog --msi --instLocation PerUser
```

CI release (`release.yml`) is triggered manually via `workflow_dispatch`. It reads the version from `Publish.props`, publishes, downloads the previous release for delta diffing, packs, and uploads to GitHub Releases.

## Architecture

Single project: `src/KuruExtract/KuruExtract.csproj`. All subsystems are under the `KuruExtract` namespace.

### Entry flow

`Program.cs` bootstraps Velopack (handles install/uninstall hooks and PATH management), then delegates everything to `ConsoleApp.Run(args, ExtractDayZCommand.Execute)`.

`ExtractDayZCommand.Execute` in `Commands/ExtractDayZCommand.cs` is the single command and orchestrates the full extraction pipeline:
1. Velopack self-update check (skipped in `--unattended` mode)
2. Steam game auto-detection via `SteamLibrary` → `GamePath`
3. PBO enumeration; official PBOs are verified against the hardcoded Bohemia public key via `.dayz.bisign` files
4. Smart cleanup: builds a complete set of expected output paths, diffs against existing files on disk, deletes stale files and empty directories
5. Parallel extraction via `Partitioner.Create` + PLINQ with configurable degree of parallelism
6. Extension filter evaluation (include XOR exclude — both cannot be set simultaneously)
7. Post-extraction stats: `BreakdownChart` by file type and byte size

### Subsystems

| Path | Purpose |
|------|---------|
| `RV/PBO/PBO.cs` | PBO parser and extractor. Reads header on construction (file stream is closed after header read and reopened lazily during extraction). Lock-per-PBO synchronizes concurrent reads from PLINQ workers. |
| `RV/PBO/PBOFileExisting.cs` | Wraps a `FileEntry`; transparently renames `config.bin` → `.cpp` and flags it as a rapified param file for decompilation. |
| `RV/PBO/FileEntry.cs` | Raw PBO header entry (filename, offsets, packing method, sizes). |
| `RV/Config/ParamFile.cs` | Decompiles rapified (binary `\0raP`-magic) configs (config.bin, .rvmat) into human-readable text. Non-rapified files that pass the extension check are copied through untouched. |
| `RV/Compression/LZSS.cs` | LZSS decompressor for compressed PBO entries. Uses `ArrayPool<byte>` to avoid heap allocations on the hot path. |
| `RV/IO/RVBinaryReader.cs` | Stream wrapper with PBO-specific helpers (null-terminated ASCII strings, LZSS reads). |
| `RV/Signatures/BiPublicKey.cs` | Reads Wincrypt-format `.bikey` / `.bisign` files. Used to verify official PBOs: only PBOs whose `.dayz.bisign` matches the embedded hardcoded key (`Constants.DayZPublicKey`) are treated as official. |
| `Steam/SteamLibrary.cs` | Locates Steam via registry (Windows) or filesystem candidates (Linux). Parses `libraryfolders.vdf` (VDF format) and `.acf` manifest files to build the installed game list. |
| `Steam/ValveDataFile.cs` | VDF/KeyValues text parser used by `SteamLibrary`. |
| `Steam/GamePath.cs` | Resolves DayZ stable (AppId 221100) and DayZ Experimental (AppId 1024020) install paths from the `SteamLibrary` game list. |

### Key behaviors

- **Official vs. unofficial PBOs**: When no `--include-unofficial-pbos` mods are specified, only PBOs with a valid `.dayz.bisign` signature are included. When mods are specified, official PBO scanning is skipped entirely and only the provided mod paths are used. Obfuscated unofficial PBOs (`obfuscated` property in header) are silently skipped.
- **Scripts PBO sub-directory injection**: The `scripts` PBO (prefix = "scripts") gets a `DayZ` folder injected between the first path segment and the rest, unless `--flat-scripts` is set. Editor paths (`editor/...`) skip the `editor` segment before injection.
- **Smart cleanup scope**: Cleanup is scoped to each PBO's `Prefix` directory rather than the whole destination, so unrelated mods sharing a root prefix segment are left untouched.

### Build configuration

- `Directory.Build.props`: Shared properties — `net10.0`, `x64`, `nullable enable`, `IsAotCompatible=true`, platform `#define` constants (`WINDOWS`/`LINUX`/`MACOS`).
- `Release.props`: Imported on Release configuration; sets `RELEASE` define and `DebugType=none`.
- `Publish.props`: Imported on publish; sets version, AOT/trim/single-file publish options, and `PUBLISH` define. Controls the output directory (`publish/`).
