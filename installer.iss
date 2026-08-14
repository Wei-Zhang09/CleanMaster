[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName=CleanMaster
AppVersion=2.3.0
AppPublisher=AWe-SoftWare
AppPublisherURL=https://awe-software-production.up.railway.app
AppSupportURL=https://awe-software-production.up.railway.app
DefaultDirName={autopf}\CleanMaster
DefaultGroupName=CleanMaster
AllowNoIcons=yes
OutputDir=installer
OutputBaseFilename=CleanMaster-Setup-v2.3.0
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "bin\Release\net9.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}"; Permissions: users-modify

[Icons]
Name: "{group}\CleanMaster"; Filename: "{app}\CleanMaster.exe"
Name: "{group}\Uninstall CleanMaster"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CleanMaster"; Filename: "{app}\CleanMaster.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CleanMaster.exe"; Description: "Launch CleanMaster"; Flags: nowait postinstall skipifsilent shellexec

