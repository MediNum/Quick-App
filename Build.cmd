@echo off
setlocal
rem Quick App build script (keep this file ASCII-only: cmd.exe parses it in the ANSI codepage)
rem Requires only the .NET Framework 4.x that ships with Windows - no SDK needed.
cd /d "%~dp0"

set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
  echo [ERROR] csc.exe not found. Please enable .NET Framework 4.x.
  exit /b 1
)
set WPF=%FW%\WPF
if not exist "%WPF%\PresentationCore.dll" set WPF=%FW%
if not exist KuaiApps.ico (
  echo [ERROR] KuaiApps.ico missing. Run: python BuildIcon.py .
  exit /b 1
)

rem --- Launcher: the app itself ---
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /win32icon:KuaiApps.ico ^
  /lib:"%WPF%" ^
  /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll ^
  /r:PresentationCore.dll /r:PresentationFramework.dll /r:WindowsBase.dll /r:System.Xaml.dll ^
  /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:Microsoft.CSharp.dll ^
  /out:AppShelf.exe Launcher.cs
if errorlevel 1 (
  echo [ERROR] Launcher build failed.
  exit /b 1
)

rem --- Setup: embeds AppShelf.exe as a manifest resource ---
"%FW%\csc.exe" /nologo /target:winexe /optimize+ /win32icon:KuaiApps.ico ^
  /r:System.Windows.Forms.dll /r:Microsoft.CSharp.dll ^
  /res:AppShelf.exe ^
  /out:KuaiApps-Setup.exe Setup.cs
if errorlevel 1 (
  echo [ERROR] Setup build failed.
  exit /b 1
)

echo.
echo Build OK:
echo   AppShelf.exe        - the app (run directly, --self-test to self test)
echo   KuaiApps-Setup.exe  - installer (--self-test / --uninstall)
echo Rename with a version number for release, e.g. QuickApp-Setup-0.1.3.exe.
