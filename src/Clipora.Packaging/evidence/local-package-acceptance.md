# Package acceptance

Verified on Windows x64 build 26200 on 2026-08-09.

## Portable

- archive extracted successfully;
- `Clipora.exe` remained running for the acceptance interval;
- `Clipora.pri` and `portable-data/.clipora-portable` were present;
- settings/log routing did not modify `%LOCALAPPDATA%\Clipora`;
- bundled FFmpeg SHA-256: `014876A2FA881DA5493EAB66AE948DF6EFC007B43AC3C6B3AA40E4B567FA7D98`;
- bundled LGPL text was present.

## Setup

- per-user silent installation completed with exit code 0;
- installed application remained running for the acceptance interval;
- bundled FFmpeg and LGPL notices were present;
- portable marker was absent;
- silent uninstall completed with exit code 0;
- installation directory was removed completely;
- all six Clipora-owned Explorer keys were absent after uninstall.

The release workflow repeats build, tests, payload validation and checksum generation on a fresh GitHub-hosted Windows runner.
