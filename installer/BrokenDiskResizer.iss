; Broken Disk Resizer - kurulum paketi (Inno Setup 6)
; Derleme: paketle.bat  (ya da: ISCC.exe /DAppVersion=1.0 installer\BrokenDiskResizer.iss, önce build.bat)
;
; - Program diskleri ham okuduğu için zaten yönetici izniyle çalışır; bu yüzden Program Files'a kurulur.
; - Program kuruluysa aynı yere üzerine kurar (güncelleme); çalışan kopya önce düzgünce kapatılır
;   (süren tarama durdurulup kaydedilir; bölümleme sürüyorsa beklenir).
; - Windows "Uygulamalar" listesinde sürümüyle görünür. Kaldırırken tarama haritalarının da silinip
;   silinmeyeceği sorulur.

#ifndef AppVersion
  #define AppVersion "1.0"
#endif
#define AppName "Broken Disk Resizer"
#define AppExe "BrokenDiskResizer.exe"
#define AppUrl "https://github.com/HErkanuzun/BrokenDiskResizer"

[Setup]
; AppId hiç değişmemeli: güncellemenin eski kurulumu bulması buna bağlı
AppId={{C500871B-7DB3-47EF-9E79-5F8E29895BEC}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=HErkanuzun
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 HErkanuzun. MIT License.
VersionInfoVersion={#AppVersion}.0.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=BrokenDiskResizer-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
turkish.DeleteData=Tarama haritaları, SMART geçmişi ve ayarlar da silinsin mi?%n%n(%1)%n%nProgramı daha sonra yeniden kuracaksan "Hayır" de; diskin haritası korunur.
english.DeleteData=Also delete the scan maps, SMART history and settings?%n%n(%1)%n%nChoose "No" if you plan to reinstall later; the disk maps are kept.
turkish.Warning=Bu araç diski onarmaz. Bozuk sektörleri artan bir disk genelde ölmektedir; üzerine önemli hiçbir şey koyma.
english.Warning=This tool doesn't repair the disk. A disk with growing bad sectors is usually dying; don't keep anything important on it.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\bin\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\packaging\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Açık kopya varsa önce düzgünce kapatılır (süren tarama kaydedilir)
Filename: "{app}\{#AppExe}"; Parameters: "--exit"; Flags: waituntilterminated runhidden; RunOnceId: "CloseApp"

[Code]
// Dosyalar değiştirilmeden önce çalışan kopya düzgünce kapatılır:
// yeni exe geçici klasörden "--exit" ile çalıştırılır.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  ExtractTemporaryFile('{#AppExe}');
  Exec(ExpandConstant('{tmp}\{#AppExe}'), '--exit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.FinishedLabel.Caption := WizardForm.FinishedLabel.Caption + #13#10#13#10 + CustomMessage('Warning');
end;

// Kaldırma bitince kullanıcı verisi sorulur (sessiz kaldırmada dokunulmaz)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\BrokenDiskResizer');
    if DirExists(DataDir) and not UninstallSilent then
      if MsgBox(FmtMessage(CustomMessage('DeleteData'), [DataDir]), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
