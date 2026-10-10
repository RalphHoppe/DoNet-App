; DoNet — the single setup.exe.
;
; This wraps the MSIX kit in the classic internet-download experience: one
; executable, everything inside it, one UAC prompt, a progress bar, done. The
; recipient double-clicks DoNet-Setup.exe and DoNet lands on their Start menu.
;
; What it does, in order:
;   1. unpacks the .msix and .cer into a temp folder (they never land anywhere
;      the user has to see),
;   2. trusts the certificate — certutil into the machine's Trusted People
;      store, the same store Add-AppDevPackage.ps1 uses,
;   3. installs the app — Add-AppxPackage with -ForceUpdateFromAnyVersion, so
;      re-running the setup over an existing install is a reinstall, not an
;      error,
;   4. offers to launch DoNet, straight from the Start menu's own entry.
;
; The setup is a bootstrapper only: Windows owns the app's Start menu entry,
; its icon and its uninstall (Settings > Apps > DoNet). No second
; Programs-and-Features entry, no second uninstaller — one app, one way out.
;
; BUILDING (once per hand-over):
;   - Inno Setup 6, free, from https://jrsoftware.org/isinfo.php
;   - run Visual Studio's packaging wizard first (see README, "Handing it
;     over"), so the AppPackages folder below exists,
;   - open this file in Inno Setup's compiler and press Compile (Ctrl+F9),
;     or from the repository root:  ISCC.exe installer\DoNet.iss
;   - the output is installer\Output\DoNet-Setup.exe — that one file is the
;     hand-over.
;
; Bump AppVersion here whenever Package.appxmanifest's version moves, so the
; paths below point at the new kit.

#define AppName "DoNet"
#define AppVersion "1.0.0.0"

[Setup]
AppId={{8E1A0C5B-9F2E-4C7D-A3B6-1D4E5F6A7B8C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PC

; A bootstrapper, not a second app record: the MSIX is the install, and
; Windows already gives it an entry and an uninstall.
Uninstallable=no
CreateUninstallRegKey=no
CreateAppDir=no

; Trusting a machine-wide certificate store needs the one elevation, exactly
; like every setup.exe from the internet.
PrivilegesRequired=admin

WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
OutputDir=Output
OutputBaseFilename=DoNet-Setup
SetupIconFile=..\DoNet\Assets\DoNet.ico

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"

[Files]
; The packaging wizard's output, from the repository root. Fixed destination
; names, so the versioned wizard names below never reach the run steps.
Source: "..\DoNet\AppPackages\DoNet_{#AppVersion}_x64_Test\DoNet_{#AppVersion}_x64.msix"; DestDir: "{tmp}"; DestName: "DoNet.msix"; Flags: ignoreversion
Source: "..\DoNet\AppPackages\DoNet_{#AppVersion}_x64_Test\DoNet_{#AppVersion}_x64.cer"; DestDir: "{tmp}"; DestName: "DoNet.cer"; Flags: ignoreversion

[Run]
; 1. Trust the publisher on this machine.
Filename: "certutil.exe"; Parameters: "-addstore TrustedPeople ""{tmp}\DoNet.cer"""; StatusMsg: "Trusting the publisher..."; Flags: runhidden

; 2. Install the app. ForceUpdateFromAnyVersion makes re-running the setup a
;    reinstall rather than an error, for the same version or an older one.
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Add-AppxPackage -Path '{tmp}\DoNet.msix' -ForceUpdateFromAnyVersion"""; StatusMsg: "Installing DoNet..."; Flags: runhidden

; 3. Offer to launch it, through the Start menu's own entry.
Filename: "powershell.exe"; Parameters: "-NoProfile -Command ""explorer.exe shell:AppsFolder\$((Get-AppxPackage DoNet).PackageFamilyName)!App"""; Description: "Launch DoNet"; Flags: nowait postinstall skipifsilent runhidden
