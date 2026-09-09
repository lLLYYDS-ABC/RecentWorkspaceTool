@echo off
chcp 65001 >nul
echo [INFO] 正在编译 AI Workspace Launcher...

set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC_PATH%" (
    echo [ERROR] 未找到 C# 编译器: %CSC_PATH%
    pause
    exit /b 1
)

"%CSC_PATH%" /nologo /target:winexe /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.CSharp.dll" /win32icon:app.ico /out:RecentWorkspaceWidget.exe /recurse:src\*.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCCESS] 编译成功: RecentWorkspaceWidget.exe
) else (
    echo [ERROR] 编译失败，请检查错误提示。
    pause
)
