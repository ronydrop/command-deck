[Setup]
AppName=DevWorkspaceHub
AppVersion=1.0.0
AppPublisher=Rony Oliveira
DefaultDirName={autopf}\DevWorkspaceHub
DefaultGroupName=DevWorkspaceHub
OutputDir=installer_output
OutputBaseFilename=DevWorkspaceHub-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\DevWorkspaceHub.exe

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\DevWorkspaceHub"; Filename: "{app}\DevWorkspaceHub.exe"
Name: "{autodesktop}\DevWorkspaceHub"; Filename: "{app}\DevWorkspaceHub.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na area de trabalho"; GroupDescription: "Atalhos:"

[Run]
Filename: "{app}\DevWorkspaceHub.exe"; Description: "Iniciar DevWorkspaceHub"; Flags: nowait postinstall skipifsilent
