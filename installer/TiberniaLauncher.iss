#ifndef SourceDirectory
  #error SourceDirectory must point to the published launcher files.
#endif
#ifndef OutputDirectory
  #error OutputDirectory must point to the installer output directory.
#endif
#ifndef SetupIcon
  #error SetupIcon must point to the Tibernia icon.
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{EBC487F4-D81C-4D2D-8886-3F760B520731}
AppName=Tibernia Launcher
AppVersion={#AppVersion}
AppVerName=Tibernia Launcher {#AppVersion}
AppPublisher=Tibernia
AppPublisherURL=https://tibernia.com/
AppSupportURL=https://tibernia.com/
DefaultDirName={localappdata}\Tibernia
DefaultGroupName=Tibernia
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=Tibernia-Setup
SetupIconFile={#SetupIcon}
UninstallDisplayIcon={app}\OtsLauncher.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.17763
VersionInfoCompany=Tibernia
VersionInfoDescription=Instalator Tibernia Launcher
VersionInfoProductName=Tibernia Launcher
VersionInfoProductVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
Source: "{#SourceDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Tibernia Launcher"; Filename: "{app}\OtsLauncher.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Tibernia"; Filename: "{app}\OtsLauncher.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\OtsLauncher.exe"; Description: "Uruchom Tibernia Launcher"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
