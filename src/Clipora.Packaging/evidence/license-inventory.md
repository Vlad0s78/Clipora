# Runtime license inventory

Reviewed for Clipora 0.1.0 build 1 on 2026-08-09.

| Component | Version / identity | License | Distribution evidence |
|---|---|---|---|
| Clipora source | 0.1.0 build 1 | MIT | `LICENSE` |
| FFmpeg / FFprobe | n8.1.2-34-g9b6c8969e0, BtbN win64 LGPL | LGPL-3.0-or-later | `ffmpeg/LICENSE`, `ffmpeg/PROVENANCE.json`, `ffmpeg/SOURCE.md` |
| Microsoft Windows App SDK | 2.3.1 | Microsoft license terms | NuGet package metadata and publish payload |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | NuGet package metadata |
| Microsoft.Extensions.DependencyInjection | 10.0.5 | MIT | NuGet package metadata |
| Serilog / sinks | 4.4.0 / 7.0.0 | Apache-2.0 | NuGet package metadata |

FFmpeg is invoked as a separate process. The distributed build has `--disable-libx264`, `--disable-libx265` and no `--enable-gpl`. Exact binary, license, source and build-script hashes are in `ffmpeg/PROVENANCE.json`. Setup and Portable contain this inventory, notices, the LGPL text and provenance.
