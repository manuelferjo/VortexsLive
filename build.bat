@echo off
rem Crea VortexsLive.exe con el compilador de C# que ya trae Windows (.NET Framework 4). No instala nada.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo No se encuentra el compilador de .NET Framework 4 en este Windows.
  pause
  exit /b 1
)
if not exist "res\logo.png" (
  echo Falta la carpeta "res" junto a build.bat. Descomprime el ZIP completo y vuelve a probar.
  pause
  exit /b 1
)
"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe /out:VortexsLive.exe /win32icon:res\app.ico /r:System.Security.dll /r:System.Numerics.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.VisualBasic.dll /resource:res\logo.png,StreamDelay.logo.png /resource:res\chat.html,StreamDelay.chat.html /resource:res\delay.html,StreamDelay.delay.html /resource:res\viewers.html,StreamDelay.viewers.html /resource:res\mando.html,StreamDelay.mando.html /resource:res\overlay-style.js,StreamDelay.overlay-style.js /resource:res\widgets.html,StreamDelay.widgets.html /resource:res\Outfit-Bold.ttf,StreamDelay.Outfit-Bold.ttf /resource:res\PlusJakartaSans-Regular.ttf,StreamDelay.PlusJakartaSans-Regular.ttf /resource:res\PlusJakartaSans-SemiBold.ttf,StreamDelay.PlusJakartaSans-SemiBold.ttf /resource:res\PlusJakartaSans-Bold.ttf,StreamDelay.PlusJakartaSans-Bold.ttf /resource:res\JetBrainsMono-Medium.ttf,StreamDelay.JetBrainsMono-Medium.ttf src\*.cs
if errorlevel 1 (
  echo.
  echo No se pudo crear VortexsLive.exe. Copia el texto de arriba si pides ayuda.
  pause
  exit /b 1
)
rem Firma digital: solo si ya configuraste tu certificado (firmar.ps1 -Elegir, ver FIRMA.txt).
if exist "firma.cfg" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0firmar.ps1"
  if errorlevel 1 echo AVISO: VortexsLive.exe se ha creado pero NO se ha podido firmar.
)
echo.
echo Listo: VortexsLive.exe creado en esta carpeta. Ya puedes abrirlo.
pause
