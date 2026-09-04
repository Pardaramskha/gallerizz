@echo off
rem Fabrique dist\Gallerizz-Setup.exe (auto-extracteur) + dist\Gallerizz-portable.zip.
rem Prerequis : build.bat deja passe (Gallerizz.exe present).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

if not exist Gallerizz.exe (
  echo Gallerizz.exe manquant : lancez build.bat d'abord.
  exit /b 1
)

if not exist dist mkdir dist
if exist dist\app.zip del dist\app.zip

powershell -NoProfile -Command "Compress-Archive -Path 'Gallerizz.exe','dwebp.exe','register.ps1','unregister.ps1','README.md','LICENSE' -DestinationPath 'dist\app.zip' -Force"
if errorlevel 1 exit /b 1
copy /y dist\app.zip dist\Gallerizz-portable.zip >nul

"%FW%\csc.exe" /nologo /target:winexe /out:dist\Gallerizz-Setup.exe /optimize+ /codepage:65001 ^
  /win32icon:assets\gallerizz.ico ^
  /resource:dist\app.zip,app.zip ^
  /lib:"%FW%\WPF" ^
  /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  tools\setup-stub.cs

if errorlevel 1 (
  echo *** Echec de la compilation de l'installeur ***
  exit /b 1
)
echo Installeur pret : dist\Gallerizz-Setup.exe + dist\Gallerizz-portable.zip
