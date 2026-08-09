# Portable data isolation evidence

Status: **verified** for the application and packaging source. Clean-Windows Setup/Portable acceptance remains a separate release gate.

## Contract

- Portable marker: `portable-data/.clipora-portable` beside `Clipora.exe`.
- Installed mode: settings, logs and thumbnail cache remain under `%LOCALAPPDATA%/Clipora`.
- Portable mode: `settings.json`, `Logs/` and `Cache/Thumbnails/` resolve below `portable-data/`.
- Portable mode registers `DisabledExplorerIntegrationService`; the Settings checkbox is disabled and no HKCU-backed service is created.
- Setup staging does not contain the marker. Portable staging creates the marker before its manifest and ZIP are generated.

## Source enforcement

- `src/Clipora.Core/Services/AppDataPathResolver.cs`
- `src/Clipora.App/App.xaml.cs`
- `src/Clipora.App/ViewModels/SettingsPageViewModel.cs`
- `src/Clipora.Shell/DisabledExplorerIntegrationService.cs`
- `src/Clipora.Packaging/scripts/Build-Packages.ps1`

## Verification

```text
dotnet build Clipora.sln -c Release -p:Platform=x64 --no-restore --nologo
exit=0; warnings=0; errors=0

Core tests: 110 passed
FFmpeg tests: 97 passed
Media tests: 8 passed
Shell tests: 15 passed
Total: 230 passed, 0 failed
```

A real Release `Clipora.exe` was started with the marker present. It created its startup log under `portable-data/Logs`; a before/after snapshot of `%LOCALAPPDATA%/Clipora` stayed byte-for-byte unchanged.

The focused acceptance then saved and reloaded settings and generated 20 real JPEG timeline thumbnails from the neutral test video. `settings.json`, every thumbnail and the cache metadata were below `portable-data`; the non-portable sentinel directory stayed empty. The video SHA-256 remained:

```text
5176131F6187198AE45A3F06C60048D54321D1ABC343813E0B3C59A777A984A9
```

Registry guard tests verify `IsEnabled=false` and reject every `SetEnabled` attempt in Portable mode before any registry API can be reached.
