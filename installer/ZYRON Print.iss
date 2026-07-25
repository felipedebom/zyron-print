#define MyAppName "ZYRON Print"
#define MyAppVersion "0.1.5"
#define MyAppPublisher "ZYRON"
#define MyAppExeName "ZYRON Print.exe"

[Setup]
AppId={{2C6387D1-902B-4AF9-B69B-9DBD1146398F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ZYRON Print
DefaultGroupName=ZYRON Print
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=ZYRON-Print-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\src\Zyron.Print\Assets\zyron-print.ico

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ZYRON Print"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\ZYRON Print"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o ZYRON Print"; Flags: nowait postinstall skipifsilent
