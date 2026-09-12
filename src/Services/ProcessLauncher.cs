using System;
using System.Diagnostics;
using System.IO;
using RecentWorkspaceWidget.Models;

namespace RecentWorkspaceWidget.Services
{
    public static class ProcessLauncher
    {
        private static string lastLaunchedPath = null;
        private static DateTime lastLaunchTime = DateTime.MinValue;
        private static readonly object launchLock = new object();

        public static Action<string, bool> StatusFeedbackCallback { get; set; }

        public static bool ValidateLaunch(WorkspaceItem item, AgentType type, out string errorMessage)
        {
            errorMessage = null;
            if (item == null)
            {
                errorMessage = "未选择任何工作空间项目。";
                return false;
            }

            if (string.IsNullOrEmpty(item.Path))
            {
                errorMessage = "工作空间路径为空，无法启动。";
                return false;
            }

            if (!Directory.Exists(item.Path))
            {
                errorMessage = string.Format("工作空间目录已不存在或已被移除。\r\n路径: {0}\r\n建议：按 F5 刷新列表移除失效项。", item.Path);
                return false;
            }

            if (type != AgentType.Explorer)
            {
                AgentInfo info = AgentDetector.GetAgent(type);
                if (info == null || !info.IsInstalled || string.IsNullOrEmpty(info.ExecutablePath) || !File.Exists(info.ExecutablePath))
                {
                    string name = (info != null && !string.IsNullOrEmpty(info.Name)) ? info.Name : type.ToString();
                    errorMessage = string.Format("未检测到已安装的 {0} 命令行工具。\r\n建议：通过官方安装程序或 npm 全局安装并配置环境变量后重试。", name);
                    return false;
                }
            }

            return true;
        }

        public static bool Launch(WorkspaceItem item, AgentType type, out string launchError)
        {
            launchError = null;
            if (!ValidateLaunch(item, type, out launchError))
            {
                NotifyStatus(launchError, false);
                return false;
            }

            // Anti-double-click guard: 1.5s window per workspace
            lock (launchLock)
            {
                if (string.Equals(lastLaunchedPath, item.Path, StringComparison.OrdinalIgnoreCase) &&
                    (DateTime.Now - lastLaunchTime).TotalMilliseconds < 1500)
                {
                    return true; // Already being launched, ignore duplicate click
                }
                lastLaunchedPath = item.Path;
                lastLaunchTime = DateTime.Now;
            }

            switch (type)
            {
                case AgentType.OpenCode:
                    return LaunchCliAgent(item, AgentType.OpenCode, "OpenCode", ".");
                case AgentType.ClaudeCode:
                    return LaunchCliAgent(item, AgentType.ClaudeCode, "Claude Code", "");
                case AgentType.Codex:
                    return LaunchCliAgent(item, AgentType.Codex, "OpenAI Codex", "");
                case AgentType.VSCode:
                    return LaunchVSCode(item);
                case AgentType.Explorer:
                    return OpenInExplorer(item);
                default:
                    launchError = "未知或不支持的启动类型。";
                    return false;
            }
        }

        private static bool LaunchVSCode(WorkspaceItem item)
        {
            try
            {
                AgentInfo info = AgentDetector.GetAgent(AgentType.VSCode);
                NotifyStatus(string.Format("正在通过 VS Code 打开: {0}...", item.Name), true);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = info.ExecutablePath;
                psi.Arguments = QuoteWindowsArgument(item.Path);
                psi.WorkingDirectory = item.Path;
                psi.UseShellExecute = true;

                Process proc = Process.Start(psi);
                return (proc != null);
            }
            catch (Exception ex)
            {
                NotifyStatus(string.Format("启动 VS Code 失败: {0}", ex.Message), false);
                return false;
            }
        }

        private static bool OpenInExplorer(WorkspaceItem item)
        {
            try
            {
                NotifyStatus(string.Format("正在打开资源管理器: {0}...", item.Name), true);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "explorer.exe";
                psi.Arguments = QuoteWindowsArgument(item.Path);
                psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                NotifyStatus(string.Format("打开资源管理器失败: {0}", ex.Message), false);
                return false;
            }
        }

        private static bool LaunchCliAgent(WorkspaceItem item, AgentType type, string agentDisplayName, string agentArgs)
        {
            try
            {
                AgentInfo info = AgentDetector.GetAgent(type);
                NotifyStatus(string.Format("正在启动 {0} [{1}]...", agentDisplayName, item.Name), true);

                ProcessStartInfo psi = new ProcessStartInfo();
                bool isPowerShellScript = info.ExecutablePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
                if (isPowerShellScript)
                {
                    psi.FileName = "powershell.exe";
                    psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + QuoteWindowsArgument(info.ExecutablePath);
                    if (!string.IsNullOrEmpty(agentArgs)) psi.Arguments += " " + agentArgs;
                }
                else
                {
                    psi.FileName = info.ExecutablePath;
                    psi.Arguments = agentArgs ?? "";
                }

                psi.WorkingDirectory = item.Path;
                psi.UseShellExecute = true;

                Process proc = Process.Start(psi);
                if (proc == null)
                {
                    NotifyStatus(string.Format("启动 {0} 失败。", agentDisplayName), false);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                NotifyStatus(string.Format("启动 {0} 遇到异常: {1}", agentDisplayName, ex.Message), false);
                return false;
            }
        }

        private static string QuoteWindowsArgument(string value)
        {
            if (value == null) return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void NotifyStatus(string message, bool isSuccess)
        {
            if (StatusFeedbackCallback != null)
            {
                StatusFeedbackCallback(message, isSuccess);
            }
        }
    }
}
