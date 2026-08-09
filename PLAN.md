# PLAN.md — Clipora MVP

## 1. Codex execution rules

1. Read this file and inspect production assets in `src/Clipora.App/Assets/` before coding. The local `Clipora_Assets/` archive is optional design history and is excluded from Git.
2. Implement phases strictly in order; repository must build after every phase.
3. Do not add features outside MVP scope.
4. Use provided assets; do not redraw icons from mockups.
5. Source video is immutable: never overwrite/delete it.
6. All long operations: async + `CancellationToken`.
7. FFmpeg/FFprobe run directly as hidden child processes; never via CMD/PowerShell.
8. UI text must come from RU/EN resources, never hardcoded in Views/ViewModels.

## 2. Product

- Name: **Clipora**
- EN: `Trim. Compress. Done.`
- RU: `Обрезай. Сжимай. Готово.`
- Developer: `by Vlad0s`
- Target: Windows 11 x64
- Release state: pre-1.0 development
- Current version/build: read only from `Directory.Build.props`

MVP flow: `Drop/Open → Analyze → choose Compress whole, Trim + Compress or Trim only → Progress → Result`.

## 3. Stack

- C# / .NET 10
- WinUI 3 + Windows App SDK
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- System.Text.Json
- Serilog
- bundled FFmpeg + FFprobe
- xUnit
- Inno Setup

Do not use Electron, Python runtime, libav bindings, system PATH FFmpeg.

## 4. Design system

Fonts are system fonts; do not bundle font files.

- UI: `Segoe UI Variable`, fallback `Segoe UI`
- Technical log/monospace values: `Cascadia Mono`
- H1 28 Semibold; H2 20 Semibold; Body 14 Regular; Secondary 12 Regular; Button 14 Semibold; Technical 12–13

Colors:

```text
Magenta   #C93CFF
Purple    #7A4DFF
Blue      #2F7BFF
Cyan      #39E6FF
Midnight  #0B1020
White     #F5F7FF
Secondary #9AA3B5
Panel     #10172A
Border    #26314A
Danger    #FF4D67
Success   #38E0A0
```

Accent gradient: `#C93CFF → #7A4DFF → #2F7BFF → #39E6FF`.

Rules: dark-only MVP; radius 10–16 px; glow only for primary/selected/progress states; DPI-safe 100–200%; minimum window width 1100 px.

Production design assets:

```text
src/Clipora.App/Assets/Brand/
src/Clipora.App/Assets/Icons/
src/Clipora.App/Assets/AppIcon.ico
```

Generated mockups are non-normative references. Keep the UI focused and use only production assets tracked under `src/Clipora.App/Assets/`.

## 5. Localization

Locales: `ru-RU`, `en-US`.

```text
src/Clipora.App/Strings/en-US/Resources.resw
src/Clipora.App/Strings/ru-RU/Resources.resw
```

First launch: Windows `ru*` → RU, otherwise EN. Persist manual choice. Localize all UI/errors/dialogs/Explorer labels. Never translate `Clipora`, `Vlad0s`.

## 6. Versioning

Single source: `Directory.Build.props`.

The initial development line starts at `Version=0.1.0`, `Build=1`. Keep the product on `0.x` until the public 1.0 acceptance criteria are complete. Increment `Build` for each distributed build; change the semantic version only for an intentional release milestone.

Expose the current `Version` and `Build` to executable metadata, About, logs, installer and portable names. Do not duplicate literal version/build values in UI or code.

```text
clipora-win64-setup.exe
clipora-win64-portable.zip
```

## 7. Repository structure

```text
Clipora.sln
src/
  Clipora.App/        # WinUI Views/ViewModels/resources
  Clipora.Core/       # models/interfaces/settings
  Clipora.FFmpeg/     # process runner/ffprobe/progress/commands
  Clipora.Media/      # preview/timeline/thumbnails
  Clipora.Shell/      # Explorer integration
  Clipora.Packaging/  # Inno/portable build scripts
tests/
  Clipora.Core.Tests/
  Clipora.FFmpeg.Tests/
ffmpeg/
  ffmpeg.exe
  ffprobe.exe
  LICENSE
```

`ffmpeg.exe` and `ffprobe.exe` are local/release inputs and are excluded from Git; the public repository keeps source code, documentation and license notices.

MVVM only. Views never execute processes or build FFmpeg commands.

## 8. Core API

Models:

```text
VideoFileInfo, VideoStreamInfo, AudioStreamInfo, TrimRange,
EncodeJob, EncodeProgress, EncodeResult,
AppSettings, EncoderCapability
```

Interfaces:

```csharp
IFFprobeService
IFFmpegRunner
IFFmpegCommandBuilder
IEncoderDetector
IThumbnailService
ISettingsService
IOutputFileService
```

`EncodeMode`: `TrimOnly | Compress | TrimAndCompress`.

## 9. FFprobe

Run bundled `ffprobe.exe`; request JSON; map to `VideoFileInfo`.

Required fields: duration, width, height, codec/profile, FPS, bitrate, pixel format, HDR/color metadata, audio codec/bitrate/channels, streams/subtitles, file size.

Never parse human-readable ffprobe output.

## 10. FFmpeg runner

Use `ProcessStartInfo`:

```text
UseShellExecute=false
CreateNoWindow=true
RedirectStandardOutput=true
RedirectStandardError=true
RedirectStandardInput=true
```

Arguments only through `ProcessStartInfo.ArgumentList`.

Progress flags:

```text
-progress pipe:1
-nostats
```

Parse `frame`, `fps`, `bitrate`, `total_size`, `out_time`, `speed`, `progress`; calculate percent, elapsed, ETA, current size, estimated final size. Throttle UI updates to <=10/sec.

Cancel: request graceful stop, then kill process tree if required; remove temp output only.

## 11. Compression profile

MVP exposes one automatic compression profile. There is no user-visible quality selection or advanced encoder options; the UI always has one clear `Compress` action.

Validate encoders with a short real test and choose automatically:

```text
av1_nvenc → av1_qsv → av1_amf → hevc_nvenc → hevc_qsv → hevc_amf → libsvtav1
```

The primary AV1 parameters define the single target profile and prioritize a practical balance of visual quality, target size and encoding performance:

```text
-c:v av1_nvenc -preset p3 -rc vbr -cq 35 -b_ref_mode middle
-c:v av1_qsv -preset medium -global_quality 35
-c:v av1_amf -quality balanced -rc qvbr -qvbr_quality_level 35
-c:v hevc_nvenc -preset p5 -tune hq -rc vbr -cq 30 -b:v 0 -multipass qres -spatial-aq 1 -temporal-aq 1 -b_ref_mode middle
-c:v hevc_qsv -preset medium -global_quality 30
-c:v hevc_amf -quality quality -rc qvbr -qvbr_quality_level 30
-c:v libsvtav1 -preset 10 -crf 40
```

The `medium`, `balanced` and `quality` values above are internal vendor-encoder flags, not quality modes shown in the UI. Keep all parameters centralized in this single profile. `libsvtav1` is intentionally excluded from runtime selection because the completed real benchmark did not meet the application's processing-time/size target. Process, progress, mapping, metadata and safe-output arguments may be added around the profile, but must not create additional quality choices. The resulting size depends on the source and is never presented as a guaranteed compression ratio.

For HEVC output, add `-tag:v hvc1` only to MP4/MOV-family containers. HEVC playback requires a compatible decoder; some Windows systems may need an HEVC extension or a compatible third-party player.

Audio: copy when container-compatible; otherwise AAC at 128 kbit/s.

## 12. Trim / encode pipeline

Expose exactly three actions:

- `Compress whole`: encode the complete source with the single automatic compression profile.
- `Trim + Compress`: encode only the selected interval with the same profile in one FFmpeg process; never create an intermediate re-encoded trimmed file.
- `Trim only`: save the selected interval through stream copy when possible, without re-encoding.

```text
-ss START -i INPUT -t DURATION -map 0 -c copy OUTPUT
```

Show a localized warning that frame-perfect stream-copy trim depends on keyframes. Every action writes a new output file; the source remains immutable.

## 13. Output safety

Never write over source.

```text
source.mp4
→ target.tmp.mp4
→ success
→ target.mp4
```

Delete temp on cancel/error. Default suffixes: `_compressed`, `_trimmed`, `_trimmed_compressed`; resolve collisions with `_2`, `_3`, etc.

Preserve metadata/rotation/chapters/audio/subtitles/HDR/color metadata where compatible.

## 14. Preview and timeline

Preview MVP: `MediaPlayerElement`.

Custom `TimelineControl` must support thumbnail strip, playhead, StartHandle, EndHandle, selected-range overlay, time ruler, click/drag seek.

Generate 20–40 thumbnails through FFmpeg; cache in `%LOCALAPPDATA%\Clipora\Cache\`.

## 15. Screens

Implement these states using the design system above and production assets in `src/Clipora.App/Assets/`:

```text
Home
LoadedVideo
TrimEditor
Encoding
Result
Settings
About
Error
```

Encoding screen shows: percent, encoder, FPS, speed, elapsed, ETA, current output, estimated final size, original size, Cancel.

About shows: Clipora, Version, Build, `by Vlad0s`, FFmpeg attribution/licenses.

## 16. Settings

Persist JSON at `%LOCALAPPDATA%\Clipora\settings.json`.

Fields: Language, OutputMode, ExplorerIntegration, ShowTechnicalLog, TrimShortcuts. Trim shortcuts persist typed Play/Pause, SetStart and SetEnd gestures; gestures must be supported, unique and must not use system-reserved combinations. Compression quality, encoder selection, compatible-audio copy/AAC fallback and metadata/chapters preservation are automatic policies, not settings.

## 17. Explorer integration

MVP uses safe HKCU registry entries only:

```text
Compress with Clipora
Open in Clipora
```

CLI:

```text
Clipora.exe "D:\video.mp4"
Clipora.exe --compress "D:\video.mp4"
```

Delete only Clipora-owned keys. Never delete complete `SystemFileAssociations` branches. Modern `IExplorerCommand` is post-MVP.

## 18. Packaging

Installer: Inno Setup → `%ProgramFiles%\Clipora`; include app, FFmpeg/FFprobe, licenses, shortcuts, uninstall, optional Explorer integration. Store the final installer artwork under `src/Clipora.Packaging/Assets/`.

Portable ZIP: app + ffmpeg + assets + licenses + `portable-data/`; no automatic Registry writes.

## 19. Tests

Required tests:

- `FFmpegCommandBuilder`: spaces, Cyrillic paths, TrimOnly, Compress, TrimAndCompress, audio copy, single automatic hardware AV1 → hardware HEVC → software H.264 profile.
- `FFmpegProgressParser`: frame/fps/speed/out_time/total_size/progress.
- `OutputFileService`: collision names, temp naming, safe finalize.

## 20. Implementation phases

### P1 — Bootstrap
Create solution/projects, DI, logging, localization, versioning, bundled-tool resolver, WinUI shell.  
**Done:** build succeeds; app launches.

### P2 — Load + Analyze
Browse, Drag&Drop, FFprobe JSON, metadata model, LoadedVideo UI.  
**Done:** MP4/MKV/MOV load and metadata displays.

### P3 — Encoding Engine
CommandBuilder, Runner, progress parser, cancellation, temp output, single automatic hardware AV1 → hardware HEVC → software H.264 compression profile.  
**Done:** file compresses without console and shows real progress.

### P4 — Compression UX
One `Compress` action without quality selection, plus Encoding, Result and Error/Retry screens.  
**Done:** `Drop → Compress → Result` works end-to-end.

### P5 — Preview + Trim
MediaPlayerElement, thumbnail cache, TimelineControl, TrimOnly, TrimAndCompress.  
**Done:** visual range selection and both trim modes work.

### P6 — Settings + Localization
RU/EN switching, persistence, About, version/build/developer and configurable Trim shortcuts.  
**Done:** all user-facing UI is available in RU and EN; validated shortcuts persist and apply without restarting.

### P7 — Explorer + Release
Context menu, Installer, Portable, licenses, installer/splash assets.  
**Done:** Setup EXE + Portable ZIP work on clean Windows 11 x64.

## 21. MVP acceptance

MVP is complete only when:

- bundled FFmpeg/FFprobe require no external install;
- Drag&Drop/Browse/FFprobe/preview work;
- TrimOnly and one-pass TrimAndCompress work;
- the single automatic hardware AV1 → hardware HEVC → software H.264 profile works;
- progress/FPS/speed/elapsed/ETA/sizes are live;
- Cancel and temp cleanup are safe;
- source is never modified;
- Cyrillic/space paths work;
- UI follows supplied Clipora assets;
- full RU/EN localization works;
- Version/Build/`by Vlad0s` display correctly;
- Explorer commands work;
- Installer and Portable build successfully;
- required tests pass.

## 22. Out of scope

No multi-track editor, transitions/effects, subtitle editor, watermark editor, cloud/accounts, AI, batch queue, updater, online rendering.

After P7 stop and output a short implementation/test/known-issues report.
