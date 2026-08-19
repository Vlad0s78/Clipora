# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build and test

Requires .NET SDK 10 (pinned in `global.json`) and PowerShell 7. Windows 11 x64 only.

```powershell
dotnet restore Clipora.sln --locked-mode                 # packages.lock.json files are committed
dotnet build Clipora.sln -c Release --no-restore -p:IncludeBundledMediaTools=false
dotnet test Clipora.sln -c Release --no-build --no-restore
```

`IncludeBundledMediaTools=false` is what CI uses: `ffmpeg.exe`/`ffprobe.exe` are **not** in git (only `ffmpeg/PROVENANCE.json` and license files are), and `Clipora.App.csproj` fails the build with a clear error if they are missing. To build/run the app for real, fetch the pinned, SHA-256-verified binaries first:

```powershell
pwsh -NoProfile -File src/Clipora.Packaging/scripts/Get-VerifiedMediaTools.ps1 -Development
```

Single project / single test:

```powershell
dotnet test tests/Clipora.Core.Tests/Clipora.Core.Tests.csproj
dotnet test tests/Clipora.Core.Tests/Clipora.Core.Tests.csproj --filter "FullyQualifiedName~TrimRangeTests"
```

`TreatWarningsAsErrors` is on solution-wide (`Directory.Build.props`), so any new warning breaks the build. Version and build number live only in `Directory.Build.props` (`VersionPrefix`, `CliporaBuild`) — packaging scripts read them from there and reject anything but three numeric components.

Packaging (Inno Setup 6 required for a real build):

```powershell
pwsh -NoProfile -File src/Clipora.Packaging/tests/Test-Packaging.ps1        # lints scripts + asserts gates block
pwsh -NoProfile -File src/Clipora.Packaging/scripts/Build-Packages.ps1 -DryRun
```

## Architecture

Layered by dependency direction: `Core` ← `FFmpeg` ← `Media`, plus `Shell`, all consumed by the WinUI `App`.

- **Clipora.Core** (`net10.0`, no dependencies) — interfaces (`IVideoProcessingService`, `IFFmpegCommandBuilder`, `IEncoderDetector`, …), records/models, and pure policy classes that hold the testable decisions: `CompressionProfile` (encoder names, presets, quality constants), `TrimSelectionMath`, `LanguagePolicy`, `OutputDirectoryPolicy`, `AppDataPathResolver`, `TrimShortcutPolicy`. Most unit tests target this project.
- **Clipora.FFmpeg** — process-level implementations. `VideoProcessingService` orchestrates one job: detect encoder → build command → run → finalize output, always writing to a temp file and deleting it in `finally`. `EncoderDetector` probes names in `PreferredEncoderNames` order (NVENC/QSV/AMF AV1, then HEVC, then `libsvtav1`) by encoding one black frame, stops at the first that works, and caches that result for the process lifetime (a result with no available encoder is deliberately not cached, so a transient driver failure is retried). `App` warms the probe up in the background at startup. `FFmpegCommandBuilder` emits an argument *list* (never a shell string) per `EncodeMode`; `FFmpegProgressParser` reads `-progress pipe:1` output.
- **Clipora.Media** — `ThumbnailService`, timeline frames cached under the app data cache directory.
- **Clipora.Shell** — `CliporaCommandLine` (`<path>` = open, `--compress <path>`) and HKCU-only Explorer context-menu registration for `.mp4/.mkv/.mov`. Portable installs get `DisabledExplorerIntegrationService` instead — portable mode never touches the registry.
- **Clipora.App** — WinUI 3, unpackaged and self-contained. `App.ConfigureServices()` is the single composition root (`Microsoft.Extensions.DependencyInjection`); MVVM via `CommunityToolkit.Mvvm` `[ObservableProperty]`/`[RelayCommand]`; Serilog rolling file logs. The six state panels in `MainPage.xaml` (empty/loading/error/loaded/processing/success) are gated by `x:Load`, so their children do not exist until that state is active — do not reference their named elements from code-behind; the trim editor panel stays on `Visibility` precisely because code-behind drives its player and layout. UI strings live in `Strings/{en-US,ru-RU}/Resources.resw` and are read through `ResourceLoader` — never hard-code user-facing text.

Three encode modes drive nearly everything: `Compress` (whole file, re-encode), `TrimAndCompress` (`-ss`/`-t` + re-encode), `TrimOnly` (`-c copy`, no encoder detection). `VideoProcessingService.ValidateRequest` enforces which arguments each mode allows.

**Portable vs installed** is decided at startup by `AppDataPathResolver`: a `portable-data/.clipora-portable` marker next to the executable redirects settings, logs, and thumbnail cache into `portable-data/`; otherwise `%LOCALAPPDATA%\Clipora`. This split affects Explorer integration and the packaging gates, so keep new state paths going through `AppDataPaths`.

## Testing conventions

xunit, one test project per source project. `Clipora.FFmpeg` and `Clipora.Media` expose internals via `InternalsVisibleTo` so process-construction helpers (`CreateStartInfo`) can be asserted directly. Process-execution tests do not invoke FFmpeg: they run `Clipora.FFmpeg.TestHost.exe`, a small stub copied into the test output by an MSBuild target that emits scripted progress/failure/hang behaviour. Tests deliberately use paths with spaces and Cyrillic characters — keep quoting/encoding behaviour intact.

## Release gates (do not "fix" by disabling)

`src/Clipora.Packaging/scripts/Test-ReleaseInputs.ps1` blocks real binary releases while FFmpeg Corresponding Source, license inventory, signing policy, or clean-VM acceptance are unconfirmed in `release-policy.json`. This blocking is intended. Never mark a gate complete, relax a check, or add a bypass flag without the evidence file the policy points at. `Get-VerifiedMediaTools.ps1` similarly refuses binaries whose SHA-256 does not match `ffmpeg/PROVENANCE.json`.

## Conventions

`.editorconfig` rules: LF endings, 4-space indent for C#/XAML, file-scoped namespaces, `System` usings first, brace on a new line. Nullable and implicit usings are enabled everywhere. Log messages, PowerShell error text, and in-repo docs are written in Russian; identifiers, XML/summary-level API text, and exception messages thrown from library code are English — match whichever the surrounding file uses.
