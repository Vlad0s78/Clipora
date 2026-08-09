#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef BuildNumber
  #error BuildNumber is required
#endif
#ifndef SourceDir
  #error SourceDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef OutputBaseName
  #error OutputBaseName is required
#endif

#define AppName "Clipora"
#define AppPublisher "Vlad0s"
#define AppExeName "Clipora.exe"

[Setup]
AppId={{A92C969A-8DA1-4FD8-80B1-A7C56EDCB1BA}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion} (build {#BuildNumber})
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseName}
SetupIconFile={#SourceDir}\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
LicenseFile={#SourceDir}\licenses\Clipora-LICENSE.txt
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=force
RestartApplications=no
ChangesAssociations=yes
TimeStampsInUTC=yes
VersionInfoVersion={#AppVersion}.{#BuildNumber}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "explorerintegration"; Description: "{cm:ExplorerIntegrationTask}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[CustomMessages]
english.ExplorerIntegrationTask=Add Clipora commands to File Explorer for supported videos
russian.ExplorerIntegrationTask=Добавить команды Clipora в Проводник для поддерживаемых видео
english.ExplorerOpenLabel=Open in Clipora
russian.ExplorerOpenLabel=Открыть в Clipora
english.ExplorerCompressLabel=Compress with Clipora
russian.ExplorerCompressLabel=Сжать с помощью Clipora

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

; Регистрируются только принадлежащие Clipora HKCU-ключи. uninsdeletekey удаляет только эти ветки.
[Registry]
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Open"; ValueType: string; ValueName: ""; ValueData: "{cm:ExplorerOpenLabel}"; Tasks: explorerintegration; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Compress"; ValueType: string; ValueName: ""; ValueData: "{cm:ExplorerCompressLabel}"; Tasks: explorerintegration; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Compress"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Compress"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Compress\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" --compress ""%1"""; Tasks: explorerintegration

Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Open"; ValueType: string; ValueName: ""; ValueData: "{cm:ExplorerOpenLabel}"; Tasks: explorerintegration; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Compress"; ValueType: string; ValueName: ""; ValueData: "{cm:ExplorerCompressLabel}"; Tasks: explorerintegration; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Compress"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Compress"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Compress\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" --compress ""%1"""; Tasks: explorerintegration

Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Open"; ValueType: string; ValueName: ""; ValueData: "{cm:ExplorerOpenLabel}"; Tasks: explorerintegration; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Open"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Open"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Compress"; ValueType: string; ValueName: ""; ValueData: "{cm:ExplorerCompressLabel}"; Tasks: explorerintegration; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Compress"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExeName}"",0"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Compress"; ValueType: string; ValueName: "MultiSelectModel"; ValueData: "Single"; Tasks: explorerintegration
Root: HKCU; Subkey: "Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Compress\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" --compress ""%1"""; Tasks: explorerintegration

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure DeleteOwnedExplorerKeys;
begin
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Open');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\.mp4\shell\Clipora.Compress');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Open');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\.mkv\shell\Clipora.Compress');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Open');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\SystemFileAssociations\.mov\shell\Clipora.Compress');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    DeleteOwnedExplorerKeys;
end;
