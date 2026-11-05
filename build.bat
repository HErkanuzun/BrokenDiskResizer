@echo off
rem Broken Disk Resizer - derleme betigi. Windows'un icindeki .NET Framework derleyicisini kullanir, ekstra kurulum gerekmez.
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo .NET Framework 4 derleyicisi bulunamadi.
  if "%1"=="" pause
  exit /b 1
)

if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu ^
  /win32icon:src\app.ico /win32manifest:src\app.manifest ^
  /resource:src\app.ico,BrokenDiskResizer.app.ico /resource:src\logo.png,BrokenDiskResizer.logo.png ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Core.dll ^
  /out:bin\BrokenDiskResizer.exe src\*.cs
if errorlevel 1 (
  echo.
  echo DERLEME HATASI
  if "%1"=="" pause
  exit /b 1
)
echo.
echo TAMAM: bin\BrokenDiskResizer.exe  (acarken yonetici izni ister)
if "%1"=="" pause
