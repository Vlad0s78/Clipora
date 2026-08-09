<p align="center">
  <img src="docs/images/clipora-hero.png" alt="Clipora — video compression and trimming" width="100%">
</p>

<p align="center">
  <a href="https://github.com/Vlad0s78/Clipora/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/Vlad0s78/Clipora/ci.yml?branch=main&style=flat-square&label=build" alt="Build"></a>
  <img src="https://img.shields.io/badge/Windows_11-x64-2DD4E8?style=flat-square" alt="Windows 11 x64">
  <img src="https://img.shields.io/badge/version-0.1.0_alpha-704DE8?style=flat-square" alt="Version 0.1.0 alpha">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-34D399?style=flat-square" alt="MIT License"></a>
</p>

## Русский

**Clipora** — компактное приложение для быстрого локального сжатия и визуальной обрезки видео. Один автоматический профиль выбирает лучший доступный аппаратный кодировщик, уменьшает размер файла и сохраняет визуально близкое качество без ручной настройки параметров.

- **Сжать видео** — обработать файл целиком одним нажатием.
- **Обрезать и сжать** — выбрать фрагмент на таймлайне и сразу получить компактную копию.
- **Только обрезать** — сохранить выбранную часть без перекодирования.
- **Полностью локально** — исходник не изменяется и никуда не загружается.

### Установка

Скачайте файлы из [GitHub Releases](https://github.com/Vlad0s78/Clipora/releases/latest):

- `clipora-win64-setup.exe` — обычная установка.
- `clipora-win64-portable.zip` — распакуйте архив и запустите `Clipora.exe`.

FFmpeg, FFprobe и .NET runtime уже включены. Дополнительные программы и компоненты не требуются.

## English

**Clipora** is a focused Windows app for fast local video compression and visual trimming. Its single automatic profile selects the best available hardware encoder, reduces file size and keeps visually close quality without exposing codec settings.

- **Compress video** in one click.
- **Trim and compress** using the visual timeline.
- **Trim only** without re-encoding.
- **Private by design:** source files stay unchanged and never leave your PC.

### Install

Download from [GitHub Releases](https://github.com/Vlad0s78/Clipora/releases/latest):

- `clipora-win64-setup.exe` — standard installer.
- `clipora-win64-portable.zip` — extract and run `Clipora.exe`.

FFmpeg, FFprobe and the .NET runtime are bundled. No additional downloads are required.

## Исходный код / Source

Требуются Windows 11 x64, .NET SDK 10 и PowerShell 7:

```powershell
dotnet restore Clipora.sln --locked-mode
pwsh -NoProfile -File src/Clipora.Packaging/scripts/Get-VerifiedMediaTools.ps1 -Development
dotnet build Clipora.sln -c Release --no-restore
```

Для сборки Setup и Portable установите Inno Setup 6 и запустите `src/Clipora.Packaging/scripts/Build-Packages.ps1`.

---

Windows 11 x64 · WinUI 3 · .NET 10 · FFmpeg 8.1 LGPL · [MIT License](LICENSE)
