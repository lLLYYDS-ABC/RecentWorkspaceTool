@echo off
setlocal
chcp 65001 >nul
echo [INFO] Compiling Recent Workspace Widget...

set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC_PATH%" (
    echo [ERROR] C# Compiler not found: %CSC_PATH%
    pause
    exit /b 1
)

"%CSC_PATH%" /nologo /target:winexe /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.CSharp.dll" /win32icon:app.ico /out:RecentWorkspaceWidget.exe /recurse:src\*.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] Build succeeded: RecentWorkspaceWidget.exe
) else (
    echo [ERROR] Build failed. If the file is locked, please exit the running application and try again.
)
endlocal
