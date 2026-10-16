#define MyAppName "Auction Print Relay"
#define MyAppVersion "1.1.3"
#define MyAppExeName "AuctionPrintRelay.Wpf.exe"
[Setup]
AppId={{DC2286AA-F05A-4E68-96B2-32C18F712B2D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Auction Print Relay
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=AuctionPrintRelay-Setup-{#MyAppVersion}-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "Start Auction Print Relay when Windows starts"; Flags: checkedonce
[Files]
Source: "staging\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\AuctionProductMonitor-BarTender.btin"; DestDir: "{app}\BarTender"; Flags: onlyifdoesntexist
Source: "..\bidproductlabel.btw"; DestDir: "{app}\BarTender"; Flags: onlyifdoesntexist
Source: "..\auction-bartender-print-sample.csv"; DestDir: "{app}\BarTender"; Flags: onlyifdoesntexist
[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{commonstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: autostart
[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Auction Print Relay"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Auction Print Relay"" dir=in action=allow protocol=TCP localport=17892 profile=private"; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Auction Print Relay"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveRelayFirewall"
