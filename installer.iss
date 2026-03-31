[Setup]
AppName=DevWorkspaceHub
AppVersion=1.0.0
AppId={{B3F7A2D1-8E4C-4F9B-A6D2-1C5E8F3A7B9D}
AppPublisher=Rony Oliveira
AppPublisherURL=https://github.com/ronyo
AppSupportURL=https://github.com/ronyo
AppUpdatesURL=https://github.com/ronyo
DefaultDirName={autopf}\DevWorkspaceHub
DefaultGroupName=DevWorkspaceHub
OutputDir=installer_output
OutputBaseFilename=DevWorkspaceHub-Setup-v1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\DevWorkspaceHub.exe
LicenseFile=
PrivilegesRequired=lowest
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\DevWorkspaceHub"; Filename: "{app}\DevWorkspaceHub.exe"
Name: "{group}\Desinstalar DevWorkspaceHub"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DevWorkspaceHub"; Filename: "{app}\DevWorkspaceHub.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"
Name: "startupicon"; Description: "Iniciar com o Windows"; GroupDescription: "Atalhos:"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DevWorkspaceHub"; ValueData: """{app}\DevWorkspaceHub.exe"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\DevWorkspaceHub.exe"; Description: "Iniciar DevWorkspaceHub agora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\DevWorkspaceHub"
