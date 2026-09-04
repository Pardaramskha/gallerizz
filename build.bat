@echo off
rem Compilation de Gallerizz. Ne demande que le .NET Framework 4.8 (inclus dans Windows 10/11).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

set ICON=
if exist assets\gallerizz.ico set ICON=/win32icon:assets\gallerizz.ico

"%FW%\csc.exe" /nologo /target:winexe /out:Gallerizz.exe /optimize+ /codepage:65001 %ICON% ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Core.dll ^
  /recurse:src\*.cs

if errorlevel 1 (
  echo.
  echo *** Echec de la compilation ***
  exit /b 1
)
echo Compilation reussie : Gallerizz.exe
