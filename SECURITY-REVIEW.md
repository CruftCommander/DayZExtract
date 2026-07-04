# SECURITY-REVIEW.md

Source: `D:\GitHub\DayZExtract` (local clone of github.com/wrdg/DayZExtract)
Review date: 2026-06-20
Reviewer: static read-only analysis - no build or execution performed

---

## 1. Summary Verdict

**SAFE TO BUILD**

Building from source with `dotnet build` or `dotnet publish` is safe. NuGet restore pulls from
nuget.org only (no custom feeds configured), no pre/post-build scripts execute external processes,
and the only binary checked into source is `gear.ico`. The three direct NuGet dependencies are
well-known libraries with no unexpected behavior at build time. Running the compiled binary
introduces two behaviors worth noting: (1) Velopack makes an HTTPS call to the GitHub Releases API
on every interactive startup to check for updates - this can be fully suppressed with
`--unattended`; and (2) if a legacy MSI installation of the tool is detected, it offers to invoke
`msiexec.exe` to uninstall it - this requires explicit user confirmation and is also suppressed by
`--unattended`. Neither behavior is hidden or automatic without user interaction. The source code
contains no backdoors, obfuscated payloads, shell injection vectors, or access to system
directories. All filesystem writes are scoped to the user-specified output directory and user
profile locations.

---

## 2. Dependency Table

| Name | Version | Purpose | Risk Flag |
|---|---|---|---|
| ConsoleAppFramework | 5.7.13 | CLI argument parsing and command routing. Marked `PrivateAssets=all` - used only at build time, trimmed from output. | None |
| Spectre.Console | 0.57.0 | Rich terminal UI: progress bars, breakdown chart, spinners, prompts. No network I/O. | None |
| Velopack | 1.2.0 | Self-updating installer framework. Makes HTTPS calls to GitHub Releases API at runtime. Downloads and stages `.nupkg` update packages. Modifies PATH environment variable on install/uninstall. | Network I/O; binary download on update |

No `packages.lock.json` is present. Transitive dependencies are resolved by NuGet at restore time
and are not pinned. No `NuGet.config` is present; nuget.org is used as the implicit source. No
`global.json` is present; the .NET SDK version is not pinned.

---

## 3. Network and Process Behavior

### 3a. HTTP / Network I/O

**Match 1 - Update URL constant**
- File: `src\KuruExtract\Constants.cs`, line 8
- Content: `public const string UpdateUrl = "https://github.com/wrdg/DayZExtract";`
- This URL is passed to Velopack's `GithubSource` (see Match 2).

**Match 2 - Velopack update check**
- File: `src\KuruExtract\Commands\ExtractDayZCommand.cs`, lines 46-47
```csharp
UpdateInfo? info = null;
var mgr = new UpdateManager(new GithubSource(Constants.UpdateUrl, null, true));
```
- Context: inside `if (!unattended)` block (line 44). The update check is entirely skipped when
  `--unattended` / `-u` is passed. When active, Velopack queries the GitHub Releases API to
  compare the installed version against the latest release. No request is made if the binary is
  not running from a Velopack-managed install (`mgr.IsInstalled` check at line 49).

**Match 3 - Documentation-only URLs (not runtime)**
- `src\KuruExtract\RV\Signatures\Wincrypt\RSAPublicKeyBlob.cs`, line 4: XML doc comment
  referencing `https://github.com/ashelmire/WinCryptoHelp` - code reference, not a runtime call.
- `src\KuruExtract\RV\Signatures\Wincrypt\KeyBlobHeader.cs`, line 4: XML doc comment
  referencing `https://learn.microsoft.com/...` - MSDN reference, not a runtime call.
- `src\KuruExtract\Constants.cs`, line 39: header panel markup string - displayed to the user,
  not fetched.

No direct use of `HttpClient`, `WebClient`, `WebRequest`, `Socket`, `TcpClient`, `UdpClient`,
`NetworkStream`, or `Dns` was found in the source.

### 3b. Process Execution

**Match 1 - msiexec.exe invocation**
- File: `src\KuruExtract\Commands\ExtractDayZCommand.cs`, lines 662-665
```csharp
Process.Start(new ProcessStartInfo("msiexec.exe", $"/x {Constants.LegacyProductCode} /qb")
{
    UseShellExecute = true
})?.WaitForExit();
```
- `LegacyProductCode` is the compile-time constant `{B09BF157-5C17-4087-A2B1-07421300B8C8}`
  (Constants.cs, line 9). It is not user-supplied and cannot be tampered with at runtime.
- This block is inside `PromptLegacyUninstall()`, which is:
  - Windows-only (`[SupportedOSPlatform("windows")]`, line 650)
  - Only called when a registry key for the legacy product is found (line 654)
  - Only executes after `AnsiConsole.ConfirmAsync("Uninstall now?")` returns true (line 660)
  - Entirely skipped when `--unattended` is passed (the calling site is inside `if (!unattended)`)

No other `Process.Start`, `ProcessStartInfo`, shell invocations, or `CreateProcess` equivalents
were found.

### 3c. Registry Access

**Match 1 - Steam install path detection (read-only)**
- File: `src\KuruExtract\Steam\SteamLibrary.cs`, lines 14-15
```csharp
var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
path ??= Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
```
- Read-only. Used to locate the Steam installation directory.

**Match 2 - Legacy product uninstall detection (read-only)**
- File: `src\KuruExtract\Commands\ExtractDayZCommand.cs`, lines 654-655
```csharp
var key = Registry.LocalMachine.OpenSubKey(
    $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{Constants.LegacyProductCode}");
```
- Read-only (`OpenSubKey` without write access). Checks whether the legacy MSI is installed.
  The key name is a compile-time constant; no user input is interpolated.

No registry writes were found in the source.

### 3d. Environment Variables

**Match 1 - PATH modification during install/uninstall**
- File: `src\KuruExtract\Program.cs`, lines 81-102
```csharp
var oldPath = Environment.GetEnvironmentVariable("PATH", target) ?? string.Empty;
// ... rebuild string ...
Environment.SetEnvironmentVariable("PATH", builder.ToString(), target);
```
- Scope: `EnvironmentVariableTarget.User` only (user-level PATH, not system PATH).
- This executes only inside `OnAfterInstallFastCallback` and `OnBeforeUninstallFastCallback`
  (lines 19-20) - Velopack hooks that fire during installer-managed install/uninstall events.
  Running the tool via `dotnet run` does not trigger these callbacks.

### 3e. Named Pipes / IPC

None found.

---

## 4. Filesystem Behavior

### 4a. Write operations

**Extraction output** - `src\KuruExtract\RV\PBO\PBO.cs`, lines 182-210
- `Directory.CreateDirectory(parentDir)` - creates subdirectories inside the destination path
- `File.Create(path)` - creates extracted game files
- `StreamWriter` wrapping `File.Create` - writes decompiled param/config text
- All paths are constructed as `Path.Combine(destination, pbo.Prefix, entry.FileName)` where
  `destination` is the user-supplied argument (or the prompted default).

**Default destination** - `src\KuruExtract\Commands\ExtractDayZCommand.cs`, lines 101-104
- Windows: `%USERPROFILE%\Documents\DayZ Projects`
- Linux/other: `$HOME/dayzprojects`
- Used only when no destination argument is provided and user does not change the prompt value.

**Shortcuts** - `src\KuruExtract\Commands\ExtractDayZCommand.cs`, lines 676-684
- `DesktopDirectory\DayZExtract.lnk`
- `StartMenu\Programs\DayZExtract.lnk`
- Created only inside `RecreateShortcuts()`, which is called only after a confirmed legacy MSI
  uninstall (same `if (!unattended)` path as msiexec). Uses COM `IShellLinkW` / `IPersistFile`
  interop defined in `src\KuruExtract\Interop\ShellShortcut.cs`.

### 4b. Delete operations

**Stale file cleanup** - `src\KuruExtract\Commands\ExtractDayZCommand.cs`, line 284
- `File.Delete(file)` - removes files within the destination that are no longer produced by any
  PBO being extracted. Scope is limited to each PBO's `Prefix` subdirectory inside `destination`.

**Empty directory removal** - `src\KuruExtract\Commands\ExtractDayZCommand.cs`, lines 361-383
- `Directory.Delete(path)` - removes empty directories left after stale file deletion.
- Recursion is bounded to directories within each PBO prefix; the destination root itself is
  not deleted.

### 4c. Special folders accessed

| SpecialFolder | Location | Purpose |
|---|---|---|
| `UserProfile` | `%USERPROFILE%` | Default output path base |
| `DesktopDirectory` | User desktop | Shortcut creation (install flow only) |
| `StartMenu` | User Start Menu | Shortcut creation (install flow only) |

No access to `ApplicationData`, `LocalApplicationData`, `CommonApplicationData`, `Windows`,
`System`, `ProgramFiles`, `ProgramFilesX86`, `CommonProgramFiles`, or any system directories.
No `Path.GetTempPath()` or `Path.GetTempFileName()` calls.

### 4d. Path traversal risk

File names for extracted entries come from the PBO header (`FileEntry.FileName`). The extraction
path is constructed as `Path.Combine(destination, pbo.Prefix, fileName)` with
`Path.GetDirectoryName` and `Directory.CreateDirectory` called on the result. No explicit
path-traversal sanitization (e.g. rejecting `..`) is present. This is a local-only concern:
the PBO files being extracted are the user's own DayZ game installation, not untrusted content.
For the official game PBOs this is a non-issue; for unofficial mod PBOs added via
`--include-unofficial-pbos`, a maliciously constructed PBO could theoretically write outside the
destination. This is not exploitable in normal use against a personal machine.

---

## 5. Build Pipeline Findings

### release.yml

- Trigger: `workflow_dispatch` (manual only; no push or PR trigger)
- Permissions: `contents: write` (minimum required to create GitHub Releases)
- Runner: `windows-latest` (not pinned to a specific image version)
- Steps:
  1. `actions/checkout@v6` - checks out source
  2. `dotnet tool install -g vpk --version 1.2.0` - installs Velopack CLI, version pinned
  3. PowerShell parses `Publish.props` to extract the version number
  4. `dotnet publish -c Release -r win-x64` - AOT single-file compile
  5. `vpk download github ...` - downloads previous release artifacts for delta diffing
     (`continue-on-error: true` so first release works)
  6. `vpk pack ...` - packages into installer + delta
  7. `vpk upload github ...` - publishes to GitHub Releases

No steps compile or execute code downloaded at build time. The NuGet restore implicit in
`dotnet publish` downloads packages from nuget.org. The `vpk download` step downloads the
previous *release artifact* (the published `.nupkg`) for delta computation; this is a known
Velopack workflow, not arbitrary code execution.

### staging.yml

- Trigger: `workflow_dispatch` with a `release_type` input (beta/alpha/rc1)
- Identical to `release.yml` except it modifies `Publish.props` in-place to set `<ReleaseType>`
  before publishing, and passes `--pre` to `vpk upload` to mark the release as a pre-release.

### No shell scripts or external tools

No `.ps1`, `.bat`, `.cmd`, or `.sh` files exist in the repository. Build is pure
`dotnet` CLI + `vpk`.

### Code signing

Binaries are not code-signed. This is acknowledged in `README.md`:
> "The installer and executable are not code signed. Some antivirus software may flag them
> as suspicious."

The source build and the published binary should be reproducible (same toolchain, same flags,
AOT determinism not guaranteed by .NET but possible to verify by hash comparison).

---

## 6. Binary and Encoded Content Findings

### Binaries in source

- `src\KuruExtract\gear.ico` - Windows icon file. The only non-text file tracked in git.
  No `.dll`, `.exe`, `.nupkg`, or compiled assemblies are present.

### Encoded data

**DayZPublicKey** - `src\KuruExtract\Constants.cs`, lines 24-36
- A 148-byte hex literal (the `ReadOnlySpan<byte>` array).
- This is Bohemia Interactive's official `dayz.bikey` RSA public key, used to verify that
  PBO files carry a valid `.dayz.bisign` signature before treating them as official game content.
  The key is publicly available in the DayZ game installation. It is used defensively.

**Wincrypt constants** - `src\KuruExtract\RV\Signatures\Wincrypt\KeyBlobHeader.cs` and
`RSAPublicKeyBlob.cs`
- `CALG_RSA_SIGN = 0x00002400` - Windows CryptoAPI algorithm identifier, MSDN documented.
- `0x31415352` - ASCII encoding of "RSA1", the magic number for a Wincrypt RSA public key blob.
- Both are standard Windows cryptographic structure constants, not payloads.

No `Convert.FromBase64String`, `Convert.FromHexString`, or any runtime decoding of embedded
strings was found. No obfuscated or compressed blobs beyond the above constants.

---

## 7. Provenance Notes

- Git remote: `https://github.com/wrdg/DayZExtract.git` - matches the expected public repository.
- Author: single author, `Wardog <wardog@wrdg.dev>`, across all 212 commits.
- Activity: first commit October 2022; last commit June 18, 2026. Actively maintained.
- Commit signing: none. Neither GPG nor SSH commit signatures are present.
- License: MIT (`LICENSE.md`). Copyright 2022 Wardog.
- `SECURITY.md`: not present. No vulnerability disclosure policy defined.
- `launchSettings.json` (`src\KuruExtract\Properties\launchSettings.json`): contains developer
  test paths (`P:\` and `P:\Mods\@Elevator`). No credentials, tokens, or secrets.
- Recent commits (git log):
  - `a7e4ccc` - Resolve breakdown chart sizing for standard output
  - `b6bbac2` - Removed unnecessary abstraction for PBO format
  - `301a3c3` - Remove unused encryption magic, no EBO extraction
  - `6e73425` - Remove dead rv param code
  - `6e48560` - Remove dead signature code
  - `a01c434` - Update dependencies (vpk 0.0.1589 to 1.2.0, Spectre 0.55-alpha to 0.57.0)

---

## 8. Directory Tree

```
D:\GitHub\DayZExtract\
- .github\
  - assets\
      extraction.gif
  - workflows\
      release.yml
      staging.yml
- src\
  - KuruExtract\
    - Commands\
        ExtractDayZCommand.cs
    - Extensions\
        ExtensionEqualityComparer.cs
        PathExtensions.cs
    - Interop\
        ShellShortcut.cs
    - Properties\
        launchSettings.json
    - RV\
      - Compression\
          LZSS.cs
      - Config\
        - Params\
          - Raw\
              RawArray.cs
              RawValue.cs
          ParamArray.cs
          ParamArraySpec.cs
          ParamClass.cs
          ParamDeleteClass.cs
          ParamEntry.cs
          ParamExternClass.cs
          ParamValue.cs
        - Types\
            EntryType.cs
            ValueType.cs
        ParamFile.cs
      - IO\
          RVBinaryReader.cs
      - PBO\
          FileEntry.cs
          PBO.cs
          PBOFileExisting.cs
      - Signatures\
        - Wincrypt\
            KeyBlobHeader.cs
            RSAPublicKeyBlob.cs
        BiPublicKey.cs
    - Steam\
        GamePath.cs
        SteamGame.cs
        SteamLibrary.cs
        ValveDataFile.cs
    Constants.cs
    GamePath.cs
    KuruExtract.csproj
    Program.cs
    gear.ico
- .gitattributes
- .gitignore
- Directory.Build.props
- KuruExtract.slnx
- LICENSE.md
- Publish.props
- README.md
- Release.props
```
