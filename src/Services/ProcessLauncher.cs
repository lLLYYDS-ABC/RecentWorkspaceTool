using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using RecentWorkspaceWidget.Models;

namespace RecentWorkspaceWidget.Services
{
    public static class ProcessLauncher
    {
        public static void Launch(WorkspaceItem item, AgentType type)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;

            switch (type)
            {
                case AgentType.OpenCode:
                    LaunchOpenCode(item);
                    break;
                case AgentType.ClaudeCode:
                    LaunchClaudeCode(item);
                    break;
                case AgentType.Codex:
                    LaunchCodex(item);
                    break;
                case AgentType.VSCode:
                    LaunchVSCode(item);
                    break;
                case AgentType.Explorer:
                    OpenInExplorer(item);
                    break;
            }
        }

        public static void LaunchOpenCode(WorkspaceItem item)
        {
            LaunchCliCommand(item, "OpenCode", "title OpenCode: {0} && cd /d \"{1}\" && opencode .");
        }

        public static void LaunchClaudeCode(WorkspaceItem item)
        {
            LaunchCliCommand(item, "Claude Code", "title Claude: {0} && cd /d \"{1}\" && claude");
        }

        public static void LaunchCodex(WorkspaceItem item)
        {
            LaunchCliCommand(item, "OpenAI Codex", "title Codex: {0} && cd /d \"{1}\" && codex");
        }

        public static void LaunchVSCode(WorkspaceItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;

            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    AgentInfo info = AgentDetector.GetAgent(AgentType.VSCode);
                    string codePath = (info != null && !string.IsNullOrEmpty(info.ExecutablePath)) ? info.ExecutablePath : "code";

                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = codePath;
                    psi.Arguments = string.Format("\"{0}\"", item.Path);
                    psi.UseShellExecute = true;
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;

                    Process.Start(psi);
                }
                catch
                {
                    // Fallback to cmd
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo();
                        psi.FileName = "cmd.exe";
                        psi.Arguments = string.Format("/c \"code \"{0}\"\"", item.Path);
                        psi.CreateNoWindow = true;
                        psi.WindowStyle = ProcessWindowStyle.Hidden;
                        psi.UseShellExecute = true;
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            MessageBox.Show("启动 VS Code 失败: " + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        }));
                    }
                }
            });
        }

        public static void OpenInExplorer(WorkspaceItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;

            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "explorer.exe";
                    psi.Arguments = string.Format("\"{0}\"", item.Path);
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch { }
            });
        }

        private static void LaunchCliCommand(WorkspaceItem item, string agentDisplayName, string cmdFormat)
        {
            if (item == null || string.IsNullOrEmpty(item.Path)) return;

            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = "cmd.exe";
                    psi.Arguments = string.Format("/k \"" + cmdFormat + "\"", item.Name, item.Path);
                    psi.WorkingDirectory = item.Path;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception ex)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show(string.Format("启动 {0} 失败: {1}", agentDisplayName, ex.Message), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }));
                }
            });
        }
    }
}
