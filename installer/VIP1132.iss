#define AppName "Will's VIP 1132 User Manager"
#define AppVersion "3.0.10"
#define AppPublisher "WILL"
#ifndef PublishDir
  #define PublishDir "..\build\publish"
#endif
#ifndef DistDir
  #define DistDir "..\dist"
#endif
#ifndef VideoHost
  #define VideoHost "..\build\VIP1132.InstallerVisual.exe"
#endif
#ifndef SetupVideo
  #define SetupVideo "..\assets\setup-loop.mp4"
#endif

[Setup]
AppId={{50EAB146-4745-4A53-A9EE-F4F1132A3000}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\WILL\VIP 1132
DefaultGroupName=VIP 1132
DisableProgramGroupPage=yes
OutputDir={#DistDir}
OutputBaseFilename=VIP1132-Setup-{#AppVersion}
SetupIconFile=..\assets\vip1132.ico
UninstallDisplayIcon={app}\VIP1132.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#VideoHost}"; Flags: dontcopy
Source: "{#VideoHost}.config"; Flags: dontcopy
Source: "{#SetupVideo}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\VIP 1132"; Filename: "{app}\VIP1132.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\VIP 1132"; Filename: "{app}\VIP1132.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\VIP1132.exe"; Description: "Launch VIP 1132"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  VideoSentinel: string;

procedure StopInstallerVideo;
begin
  if VideoSentinel <> '' then
    SaveStringToFile(VideoSentinel, 'complete', False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  HostPath: string;
  VideoPath: string;
begin
  if CurStep = ssInstall then
  begin
    ExtractTemporaryFile('VIP1132.InstallerVisual.exe');
    ExtractTemporaryFile('VIP1132.InstallerVisual.exe.config');
    ExtractTemporaryFile('setup-loop.mp4');

    HostPath := ExpandConstant('{tmp}\VIP1132.InstallerVisual.exe');
    VideoPath := ExpandConstant('{tmp}\setup-loop.mp4');
    VideoSentinel := ExpandConstant('{tmp}\vip1132-install.complete');
    DeleteFile(VideoSentinel);

    Exec(HostPath, '"' + VideoPath + '" "' + VideoSentinel + '"',
      ExpandConstant('{tmp}'), SW_SHOWNORMAL, ewNoWait, ResultCode);
  end
  else if CurStep = ssPostInstall then
    StopInstallerVideo;
end;

procedure DeinitializeSetup;
begin
  StopInstallerVideo;
end;
