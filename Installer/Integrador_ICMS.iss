; Inno Setup Script - Integrador_ICMS
#define AppName "Integrador_ICMS"
#define AppVersion "1.0"
#define AppPublisher "PEREIRA E SOUZA TECNOLOGIA LTDA"
#define AppExeName "CsvIntegratorApp.exe"
#define AppFolderName "Integrador_ICMS"

[Setup]
AppId={{B0C7E3B2-2B54-4B1A-9A1B-2F2C6D7B8E9F}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppFolderName}
DefaultGroupName={#AppFolderName}
OutputBaseFilename=integrador_icms
OutputDir=bin\Release
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin
WizardStyle=modern
SetupLogging=yes
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion=1.0.0.0
VersionInfoDescription=Integrador_ICMS Setup
VersionInfoProductName=Integrador_ICMS
VersionInfoCompany={#AppPublisher}

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppFolderName}"; Filename: "{app}\{#AppExeName}"
Name: "{commondesktop}\{#AppFolderName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Executar {#AppName}"; Flags: nowait postinstall skipifsilent
