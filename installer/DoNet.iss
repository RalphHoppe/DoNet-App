; DoNet — the single setup.exe.
;
; The app ships unpackaged: plain files, installed into Program Files, with a
; Start-menu shortcut and its own entry in Settings > Apps. There is no MSIX,
; no certificate to trust, no developer mode, no Windows policy between the
; recipient and the app - and no internet: the build carries both runtimes
; the app needs, so the one setup.exe is the whole install on any Windows 10
; 1809+ machine.
;
; What it does, in order:
;   1. copies the app's files into Program Files\DoNet (recursively),
;   2. creates the Start-menu shortcut (a desktop one is offered),
;   3. offers to launch DoNet.
;
; Uninstalling removes the app and leaves the vault where it belongs, in
; %LOCALAPPDATA%\DoNet - the data survives the app, unlike an MSIX uninstall.
;
; BUILDING (once per hand-over):
;   - In Visual Studio: Configuration Release, platform x64, then Rebuild.
;     That build is self-contained, and the output folder below is the whole
;     app - you can double-click the exe in it to smoke-test exactly what the
;     setup installs.
;   - Inno Setup 6, free, from https://jrsoftware.org/isinfo.php
;   - open this file in Inno Setup's compiler and press Compile (Ctrl+F9),
;     or from the repository root:  ISCC.exe installer\DoNet.iss
;   - the output is installer\Output\DoNet-Setup.exe — that one file is the
;     hand-over.
;
; Bump AppVersion here whenever the app's version moves.

#define AppName "DoNet"
#define AppVersion "1.0.0.0"

; The Release build output — the self-contained folder the exe was built
; into. The setup installs exactly these files; nothing is added or filtered.
#define BinRoot "..\DoNet\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"

[Setup]
AppId={{8E1A0C5B-9F2E-4C7D-A3B6-1D4E5F6A7B8C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PC

; A plain app with a plain home. Program Files, one UAC prompt, like every
; setup.exe the recipient has ever run.
DefaultDirName={autopf}\{#AppName}
PrivilegesRequired=admin

; This setup owns the app's entry and its uninstall - there is no MSIX
; behind it to own either.
Uninstallable=yes
CreateUninstallRegKey=yes

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=DoNet-Setup
SetupIconFile=..\DoNet\Assets\DoNet.ico

; Close a running copy before replacing its files, so an upgrade over an
; open app is a prompt rather than a half-written install.
CloseApplications=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#BinRoot}\*"; DestDir: "{app}"; Recursesubdirs; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\DoNet"; Filename: "{app}\DoNet.exe"
Name: "{autodesktop}\DoNet"; Filename: "{app}\DoNet.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DoNet.exe"; Description: "Launch DoNet"; Flags: nowait postinstall skipifsilent
