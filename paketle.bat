@echo off
rem Broken Disk Resizer - tek tikla kurulum paketi
rem 1) Programi derler (build.bat)  2) Inno Setup yoksa winget ile kurar  3) dist\ klasorune Setup.exe + Portable.zip uretir
cd /d "%~dp0"
setlocal

echo [1/3] Program derleniyor...
call build.bat ci
if errorlevel 1 goto :fail

rem Surum numarasi AssemblyInfo.cs'ten okunur
for /f "tokens=2 delims=()" %%a in ('findstr /c:"AssemblyInformationalVersion" src\AssemblyInfo.cs') do set VER=%%~a
if "%VER%"=="" goto :fail
echo        Surum: %VER%

echo [2/3] Inno Setup araniyor...
set ISCC=
for %%p in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do if exist %%p set ISCC=%%~p
if "%ISCC%"=="" (
  echo        Inno Setup 6 kurulu degil. Kurulum paketi icin gerekli ^(ucretsiz, jrsoftware.org^).
  choice /c EH /m "        winget ile simdi kurulsun mu? (E=Evet, H=Hayir)"
  if errorlevel 2 goto :noinno
  winget install --id JRSoftware.InnoSetup -e --accept-source-agreements --accept-package-agreements
  for %%p in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do if exist %%p set ISCC=%%~p
)
if "%ISCC%"=="" goto :noinno

echo [3/3] Paketler hazirlaniyor...
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 -Version %VER% -Iscc "%ISCC%"
if errorlevel 1 goto :fail
echo.
echo TAMAM: dist\BrokenDiskResizer-%VER%-Setup.exe
explorer dist
pause
exit /b 0

:noinno
echo.
echo Inno Setup olmadan kurulum paketi yapilamaz. Elle kurmak icin: https://jrsoftware.org/isdl.php
echo Program yine de hazir: bin\BrokenDiskResizer.exe
pause
exit /b 1

:fail
echo.
echo HATA: paketleme durdu. Yukaridaki mesaja bak.
pause
exit /b 1
