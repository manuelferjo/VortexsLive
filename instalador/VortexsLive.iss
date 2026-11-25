; Instalador de Vortexs Live (Inno Setup 6). Lo compila GitHub (Compilar) en cada versión:
;   ISCC.exe /DAppVersion=4.25 instalador\VortexsLive.iss   →   dist\VortexsLive-Instalar.exe
; Se instala solo para el usuario (sin pedir permisos de administrador) en
; %LOCALAPPDATA%\Programs\Vortexs Live: es una carpeta donde se puede escribir, así que las
; actualizaciones automáticas funcionan y VortexsLive.ini (ajustes y claves) se queda ahí.
; Desinstalar no borra VortexsLive.ini, los clips ni los sonidos/imágenes de las alertas.

#ifndef AppVersion
  #define AppVersion "0.0"
#endif

[Setup]
AppId={{6B0E7A52-3C1F-4F7B-9D2A-5E8C1B4A7F30}
AppName=Vortexs Live
AppVersion={#AppVersion}
AppVerName=Vortexs Live {#AppVersion}
AppPublisher=Vortexs
AppPublisherURL=https://www.vortexs.es/vortexs-live
AppSupportURL=https://www.vortexs.es/vortexs-live
AppUpdatesURL=https://github.com/manuelferjo/VortexsLive/releases/latest
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\Vortexs Live
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=VortexsLive-Instalar
SetupIconFile=..\res\app.ico
UninstallDisplayIcon={app}\VortexsLive.exe
UninstallDisplayName=Vortexs Live
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
es.StartWithWindows=Abrir Vortexs Live al iniciar Windows
en.StartWithWindows=Open Vortexs Live when Windows starts
es.Extra=Más:
en.Extra=More:

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Extra}"; Flags: unchecked

[Files]
Source: "..\VortexsLive.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\GUIA.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LEEME.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\res\OFL-*.txt"; DestDir: "{app}\licencias-fuentes"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Vortexs Live"; Filename: "{app}\VortexsLive.exe"
Name: "{autoprograms}\Guía de Vortexs Live"; Filename: "{app}\GUIA.html"
Name: "{autodesktop}\Vortexs Live"; Filename: "{app}\VortexsLive.exe"; Tasks: desktopicon
Name: "{userstartup}\Vortexs Live"; Filename: "{app}\VortexsLive.exe"; Tasks: startup

[Run]
Filename: "{app}\VortexsLive.exe"; Description: "{cm:LaunchProgram,Vortexs Live}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Lo que deja la propia actualización (el .exe anterior).
Type: files; Name: "{app}\VortexsLive.exe.old"
