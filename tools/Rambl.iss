; Build after publishing the Windows x64 app. Pass /DAppVersion=0.4.0 to ISCC.
#ifndef AppVersion
  #error Pass /DAppVersion=<version> to ISCC.
#endif

#define PublishDir "..\src\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId=jam.rambl
AppName=Rambl
AppVersion={#AppVersion}
AppPublisher=Rambl
AppPublisherURL=https://github.com/smreki/rambl
AppSupportURL=https://github.com/smreki/rambl/issues
DefaultDirName={localappdata}\Programs\Rambl
DefaultGroupName=Rambl
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Rambl-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Rambl.exe
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,runtime-pack.json"
Source: "..\artifacts\vc_redist.x64.exe"; Flags: dontcopy

[Icons]
Name: "{group}\Rambl"; Filename: "{app}\Rambl.exe"
Name: "{autodesktop}\Rambl"; Filename: "{app}\Rambl.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Rambl.exe"; Description: "Launch Rambl"; Flags: nowait postinstall skipifsilent

[Code]
function HasVCRuntime: Boolean;
var
  Installed: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Installed', Installed) and (Installed = 1);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  RedistPath: String;
  ExitCode: Integer;
begin
  Result := True;
  if (CurPageID <> wpReady) or HasVCRuntime then
    Exit;

  if WizardSilent then
  begin
    Log('The x64 Visual C++ Redistributable is missing; run Setup interactively to install it.');
    Result := False;
    Exit;
  end;

  if MsgBox('Rambl needs the Microsoft Visual C++ Redistributable (x64), which is not installed.' + #13#10#13#10 +
    'Install the bundled Microsoft prerequisite now? Windows will ask for administrator permission for this prerequisite only.',
    mbConfirmation, MB_YESNO) <> IDYES then
  begin
    Result := False;
    Exit;
  end;

  try
    ExtractTemporaryFile('vc_redist.x64.exe');
  except
    MsgBox('The bundled Microsoft Visual C++ Redistributable could not be extracted: ' + GetExceptionMessage,
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  RedistPath := ExpandConstant('{tmp}\vc_redist.x64.exe');
  if not ShellExec('runas', RedistPath, '/install /passive /norestart', '',
    SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then
  begin
    MsgBox('The Visual C++ installation was cancelled or could not start: ' + SysErrorMessage(ExitCode),
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if ExitCode = 3010 then
  begin
    MsgBox('Windows must restart to finish installing the Visual C++ Redistributable. Restart and run Rambl Setup again.',
      mbInformation, MB_OK);
    Result := False;
    Exit;
  end;

  if (ExitCode <> 0) or not HasVCRuntime then
  begin
    MsgBox(Format('The Visual C++ Redistributable did not install successfully (exit code %d). Try again or install it from Microsoft before continuing.', [ExitCode]),
      mbError, MB_OK);
    Result := False;
  end;
end;
