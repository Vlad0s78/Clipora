# Уведомления о сторонних компонентах

Исходный код Clipora лицензируется отдельно по [MIT License](LICENSE). Эта лицензия не распространяется автоматически на FFmpeg/FFprobe, Windows App SDK и NuGet-зависимости.

## FFmpeg и FFprobe

Clipora проектируется для локального запуска FFmpeg и FFprobe как отдельных дочерних процессов.

- Проект: [FFmpeg](https://ffmpeg.org/)
- Исходный код: [ffmpeg.org/download.html#get-sources](https://ffmpeg.org/download.html#get-sources)
- Лицензионная информация: [ffmpeg.org/legal.html](https://ffmpeg.org/legal.html)
- Bundled-сборка: **FFmpeg n8.1.2-34-g9b6c8969e0, LGPL-3.0-or-later**, BtbN win64 LGPL.
- Конфигурация не включает `--enable-gpl`; `libx264` и `libx265` отключены.

Бинарники `ffmpeg.exe` и `ffprobe.exe` намеренно не хранятся в Git. Каждый Setup/Portable-архив включает:

1. точные версии и происхождение бинарников;
2. полный применимый текст LGPL;
3. сведения о конфигурации сборки;
4. ссылки и SHA-256 соответствующих исходников и build scripts;
5. этот файл и `ffmpeg/PROVENANCE.json`.

Точные immutable archive URL, hashes, FFmpeg commit и build-scripts commit находятся в [`ffmpeg/PROVENANCE.json`](ffmpeg/PROVENANCE.json). Порядок получения соответствующих исходников описан в [`ffmpeg/SOURCE.md`](ffmpeg/SOURCE.md).

FFmpeg является товарным знаком Fabrice Bellard, создателя проекта FFmpeg. Clipora не является частью проекта FFmpeg и не одобрена им.

## Прямые NuGet-зависимости приложения

| Компонент | Версия | Лицензия |
|---|---:|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| Microsoft.Extensions.DependencyInjection | 10.0.5 | MIT |
| Microsoft.WindowsAppSDK | 2.3.1 | Microsoft Windows App SDK License Terms |
| Serilog | 4.4.0 | Apache-2.0 |
| Serilog.Extensions.Logging | 10.0.0 | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |

Точные версии определяются файлами `*.csproj`. При обновлении зависимости это уведомление и применимые license-файлы должны быть проверены до выпуска.

## Тестовые зависимости

| Компонент | Версия | Лицензия |
|---|---:|---|
| xUnit.net | 2.5.3 | Apache-2.0 |
| xunit.runner.visualstudio | 2.5.3 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 18.4.0 | MIT |
| coverlet.collector | 6.0.0 | MIT |

Полные тексты лицензий доступны в исходных пакетах NuGet и репозиториях соответствующих проектов. При распространении собранного приложения применяются также notices транзитивных зависимостей из итоговой publish-папки.
