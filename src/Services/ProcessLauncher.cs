using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
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
                psi.Arguments = "\"" + item.Path + "\"";
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
                psi.Arguments = "\"" + item.Path + "\"";
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
            string tempBatPath = null;
            try
            {
                AgentInfo info = AgentDetector.GetAgent(type);
                NotifyStatus(string.Format("正在启动 {0} [{1}]...", agentDisplayName, item.Name), true);

                // Clean and escape window title to prevent cmd syntax disruption
                string safeName = Regex.Replace(item.Name ?? "Workspace", @"[&|<>^""()\\%]", " ").Trim();
                string safeTitle = string.Format("{0}: {1}", agentDisplayName, safeName);

                tempBatPath = Path.Combine(Path.GetTempPath(), string.Format("rw_launch_{0}_{1}.bat", type, Guid.NewGuid().ToString("N").Substring(0, 8)));

                StringBuilder batContent = new StringBuilder();
                batContent.AppendLine("@echo off");
                batContent.AppendLine("chcp 65001 >nul");
                batContent.AppendLine(string.Format("title {0}", safeTitle));
                batContent.AppendLine("cd /d \"" + item.Path + "\"");

                // Check if target executable is a PowerShell script (.ps1)
                bool isPowerShellScript = info.ExecutablePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);

                if (isPowerShellScript)
                {
                    string psCmd = string.Format("powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{0}\"", info.ExecutablePath);
                    if (!string.IsNullOrEmpty(agentArgs)) psCmd += " " + agentArgs;
                    batContent.AppendLine(psCmd);
                }
                else
                {
                    if (string.IsNullOrEmpty(agentArgs))
                    {
                        batContent.AppendLine("\"" + info.ExecutablePath + "\"");
                    }
                    else
                    {
                        batContent.AppendLine("\"" + info.ExecutablePath + "\" " + agentArgs);
                    }
                }

                File.WriteAllText(tempBatPath, batContent.ToString(), Encoding.GetEncoding("GBK"));

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "cmd.exe";
                psi.Arguments = "/k \"\"" + tempBatPath + "\"\"";
                psi.WorkingDirectory = item.Path;
                psi.UseShellExecute = true;

                Process proc = Process.Start(psi);
                if (proc == null)
                {
                    NotifyStatus(string.Format("启动 cmd.exe 运行 {0} 失败。", agentDisplayName), false);
                    try { if (File.Exists(tempBatPath)) File.Delete(tempBatPath); } catch { }
                    return false;
                }
                else
                {
                    // Success: schedule cleanup of temporary batch wrapper
                    string fileToClean = tempBatPath;
                    ThreadPool.QueueUserWorkItem((s) =>
                    {
                        try
                        {
                            Thread.Sleep(8000);
                            if (File.Exists(fileToClean)) File.Delete(fileToClean);
                        }
                        catch { }
                    });
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrEmpty(tempBatPath))
                {
                    try { if (File.Exists(tempBatPath)) File.Delete(tempBatPath); } catch { }
                }
                NotifyStatus(string.Format("启动 {0} 遇到异常: {1}", agentDisplayName, ex.Message), false);
                return false;
            }
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