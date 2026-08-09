# Clipora.Packaging

Воспроизводимая инфраструктура выпуска Clipora для Windows 11 x64. Она готовит self-contained publish, детерминированный Portable ZIP и установщик Inno Setup, но **не обходит лицензионные и release-gates**.

## Текущее состояние

Сейчас разрешён только dry-run. Реальная бинарная сборка намеренно блокируется, потому что:

- `ffmpeg/PROVENANCE.json: distributionReview.correspondingSourceComplete` равен `false`;
- инвентаризация лицензий транзитивных компонентов ещё не завершена;
- политика подписи и приёмка на чистой Windows 11 x64 ещё не закрыты.

Это ожидаемое поведение. Изменять проверки или помечать пункты завершёнными без проверяемых evidence-файлов нельзя.

## Команды

Проверка скриптов, детерминированности ZIP и ожидаемой блокировки выпуска:

```powershell
pwsh -NoProfile -File .\src\Clipora.Packaging\tests\Test-Packaging.ps1
```

Безопасный dry-run (создаёт только JSON-отчёт и план, без EXE/ZIP):

```powershell
pwsh -NoProfile -File .\src\Clipora.Packaging\scripts\Build-Packages.ps1 `
  -DryRun
```

Реальная сборка после закрытия всех gates:

```powershell
dotnet restore .\Clipora.sln --locked-mode
pwsh -NoProfile -File .\src\Clipora.Packaging\scripts\Get-VerifiedMediaTools.ps1
pwsh -NoProfile -File .\src\Clipora.Packaging\scripts\Build-Packages.ps1
```

## Артефакты

```text
artifacts/release/
├── clipora-win64-portable.zip
├── clipora-win64-setup.exe
├── SHA256SUMS.txt
├── release-manifest.json
└── reports/
```

Portable ZIP содержит self-contained x64-приложение, `manifest.sha256`, marker `portable-data/.clipora-portable` и каталог `licenses/` с MIT License, notices, LGPL, provenance и точным upstream `README.txt` исходной сборки FFmpeg.

## Детерминированность

- RID фиксирован как `win-x64`;
- версия и build читаются только из `Directory.Build.props` (`VersionPrefix`, `CliporaBuild`);
- restore выполняется по lock-файлам;
- publish использует `Deterministic`, `ContinuousIntegrationBuild` и стабильный `PathMap`;
- содержимое ZIP сортируется по ordinal-пути;
- время каждой ZIP-записи фиксировано через `SOURCE_DATE_EPOCH`;
- `manifest.sha256` и итоговый `SHA256SUMS.txt` формируются в стабильном порядке.

Одинаковый SDK, исходники, lock-файлы, FFmpeg-входы и `SOURCE_DATE_EPOCH` дают одинаковый Portable ZIP. Inno Setup фиксируется по версии компилятора в release-policy; сам установщик проверяется по SHA-256, но не объявляется побитово воспроизводимым.

## Release-gates

`scripts/Test-ReleaseInputs.ps1` проверяет:

1. структуру `PROVENANCE.json` и release-policy;
2. SHA-256 и размер `ffmpeg.exe`/`ffprobe.exe`;
3. SHA-256 LGPL и README сборки FFmpeg;
4. наличие MIT License и `THIRD_PARTY_NOTICES.md`;
5. завершённость Corresponding Source и license inventory;
6. утверждённую политику подписи;
7. evidence приёмки Setup/Portable на чистой Windows 11 x64.
8. отсутствие видео, логов, кэша, settings, временных файлов и PDB в publish payload;
9. подтверждённую изоляцию Portable-данных в `portable-data`.

Установщик ставится в `%ProgramFiles%\Clipora` с elevation и предлагает **неотмеченную по умолчанию** HKCU-интеграцию Проводника для `.mp4`, `.mkv`, `.mov`. Создаются только ветки `Clipora.Open`/`Clipora.Compress`; uninstall удаляет только эти owned keys. Portable ZIP сам registry не изменяет.

## Точные команды упаковки

Dry-run сохраняет полностью развёрнутый массив аргументов в `artifacts/release/reports/release-plan.json`. Для версии `0.1.0`, build `1` выполняемая publish-команда имеет вид:

```powershell
dotnet publish src\Clipora.App\Clipora.App.csproj --configuration Release --runtime win-x64 --self-contained true --no-restore --output artifacts\release\staging\publish -p:Platform=x64 -p:IncludeBundledMediaTools=true -p:WindowsAppSDKSelfContained=true -p:Deterministic=true -p:ContinuousIntegrationBuild=true -p:PathMap=<REPO>=/_/Clipora -p:DebugSymbols=false -p:DebugType=None -p:PublishReadyToRun=false -p:PublishSingleFile=false -p:Version=0.1.0 -p:VersionPrefix=0.1.0 -p:CliporaBuild=1 -p:FileVersion=0.1.0.1 -p:InformationalVersion=0.1.0+build.1
pwsh -NoProfile -File src\Clipora.Packaging\scripts\New-DeterministicZip.ps1 -InputDirectory artifacts\release\staging\publish -OutputPath artifacts\release\clipora-win64-portable.zip -RootDirectoryName Clipora-0.1.0-win-x64 -SourceDateEpoch 946684800
ISCC.exe /DAppVersion=0.1.0 /DBuildNumber=1 /DSourceDir=artifacts\release\staging\publish /DOutputDir=artifacts\release /DOutputBaseName=clipora-win64-setup src\Clipora.Packaging\Clipora.iss
```

Для получения кандидата clean-VM используется `-QaCandidate`. Он убирает только циклическую зависимость от уже завершённой clean-VM acceptance и добавляет `NOT-FOR-DISTRIBUTION.txt`. Corresponding Source, license inventory, подпись, Portable isolation и проверки бинарников остаются обязательными.

Режим `Diagnostic` сообщает все блокеры и возвращает успех для dry-run. Режимы `Policy` и `Distribution` завершаются ошибкой при любом блокере. Параметра отключения gate нет.

## CI

`.github/workflows/release.yml` запускается только вручную. По умолчанию `dry_run=true`: CI компилирует и тестирует проект без bundled FFmpeg, проверяет упаковку и сохраняет только отчёты. Job создания draft-release доступен лишь при `dry_run=false`, выполняет строгий policy gate до загрузки FFmpeg, затем строгий distribution gate до создания каких-либо release-артефактов.
