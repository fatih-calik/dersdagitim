[Setup]
AppName=Ders Dagitim
AppVersion=1.1.0
AppPublisher=Aras
DefaultDirName={autopf}\DersDagitim
DefaultGroupName=Ders Dagitim
UninstallDisplayIcon={app}\DersDagitim.exe
OutputDir=SetupOutput
OutputBaseFilename=DersDagitimSetup
SetupIconFile=Resources\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
DisableProgramGroupPage=yes
PrivilegesRequired=admin

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Messages]
turkish.WelcomeLabel2=Bu sihirbaz, bilgisayar%C4%B1n%C4%B1za [name] yaz%C4%B1l%C4%B1m%C4%B1n%C4%B1 kuracakt%C4%B1r.%n%nDevam etmek i%C3%A7in '%C4%B0leri' d%C3%BCymesine t%C4%B1klay%C4%B1n%C4%B1z.

[Files]
Source: "bin\Release\net8.0-windows\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Ders Dagitim"; Filename: "{app}\DersDagitim.exe"; IconFilename: "{app}\DersDagitim.exe"
Name: "{group}\Ders Dagitim"; Filename: "{app}\DersDagitim.exe"; IconFilename: "{app}\DersDagitim.exe"
Name: "{group}\Ders Dagitim Kaldir"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\DersDagitim.exe"; Description: "Ders Dagitim programini baslat"; Flags: nowait postinstall skipifsilent
