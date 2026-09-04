@echo off
rem Compilation des sondes de Gallerizz (console, meme sources + tests).
setlocal
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319

"%FW%\csc.exe" /nologo /target:exe /out:Gallerizz.Probes.exe /codepage:65001 ^
  /main:Gallerizz.Probes ^
  /lib:"%FW%\WPF" ^
  /r:PresentationFramework.dll /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:System.Xml.dll /r:System.Xml.Linq.dll /r:System.Core.dll ^
  /recurse:src\*.cs /recurse:tests\*.cs

if errorlevel 1 (
  echo.
  echo *** Echec de la compilation des sondes ***
  exit /b 1
)
echo Compilation reussie : Gallerizz.Probes.exe
