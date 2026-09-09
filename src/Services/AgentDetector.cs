using System;
using System.IO;
using System.Collections.Generic;
using RecentWorkspaceWidget.Models;

namespace RecentWorkspaceWidget.Services
{
    public static class AgentDetector
    {
        private static readonly Dictionary<AgentType, AgentInfo> CachedAgents = new Dictionary<AgentType, AgentInfo>();
        private static bool isInitialized = false;
        private static readonly object initLock = new object();

        public static void Initialize()
        {
            if (isInitialized) return;
            lock (initLock)
            {
                if (isInitialized) return;

                CachedAgents[AgentType.OpenCode] = DetectOpenCode();
                CachedAgents[AgentType.ClaudeCode] = DetectClaudeCode();
                CachedAgents[AgentType.Codex] = DetectCodex();
                CachedAgents[AgentType.VSCode] = DetectVSCode();
                CachedAgents[AgentType.Explorer] = new AgentInfo
                {
                    Type = AgentType.Explorer,
                    Name = "资源管理器",
                    KeyHint = "Ctrl+E",
                    IsInstalled = true,
                    ExecutablePath = "explorer.exe",
                    CommandTemplate = "explorer.exe \"{path}\""
                };

                isInitialized = true;
            }
        }

        public static AgentInfo GetAgent(AgentType type)
        {
            if (!isInitialized) Initialize();
            AgentInfo info;
            if (CachedAgents.TryGetValue(type, out info)) return info;
            return null;
        }

        public static IEnumerable<AgentInfo> GetAllAgents()
        {
            if (!isInitialized) Initialize();
            return CachedAgents.Values;
        }

        private static AgentInfo DetectOpenCode()
        {
            string exec = FindCommandInPath("opencode");
            bool installed = !string.IsNullOrEmpty(exec);

            return new AgentInfo
            {
                Type = AgentType.OpenCode,
                Name = "OpenCode",
                KeyHint = "Enter",
                IsInstalled = installed,
                ExecutablePath = exec ?? "opencode",
                CommandTemplate = "title OpenCode: {name} && cd /d \"{path}\" && opencode ."
            };
        }

        private static AgentInfo DetectClaudeCode()
        {
            string exec = FindCommandInPath("claude");
            bool installed = !string.IsNullOrEmpty(exec);
            if (!installed)
            {
                // Also check if user has .claude directory configured
                string userClaude = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
                if (Directory.Exists(userClaude))
                {
                    installed = true;
                }
            }

            return new AgentInfo
            {
                Type = AgentType.ClaudeCode,
                Name = "Claude Code",
                KeyHint = "Shift+Enter",
                IsInstalled = installed,
                ExecutablePath = exec ?? "claude",
                CommandTemplate = "title Claude: {name} && cd /d \"{path}\" && claude"
            };
        }

        private static AgentInfo DetectCodex()
        {
            string exec = FindCommandInPath("codex");
            bool installed = !string.IsNullOrEmpty(exec);
            if (!installed)
            {
                string userCodex = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                if (Directory.Exists(userCodex))
                {
                    installed = true;
                }
            }

            return new AgentInfo
            {
                Type = AgentType.Codex,
                Name = "OpenAI Codex",
                KeyHint = "Alt+Enter",
                IsInstalled = installed,
                ExecutablePath = exec ?? "codex",
                CommandTemplate = "title Codex: {name} && cd /d \"{path}\" && codex"
            };
        }

        private static AgentInfo DetectVSCode()
        {
            string exec = FindCommandInPath("code");
            bool installed = !string.IsNullOrEmpty(exec);

            return new AgentInfo
            {
                Type = AgentType.VSCode,
                Name = "VS Code",
                KeyHint = "Ctrl+Enter",
                IsInstalled = installed,
                ExecutablePath = exec ?? "code",
                CommandTemplate = "code \"{path}\""
            };
        }

        private static string FindCommandInPath(string cmdName)
        {
            string[] extensions = new string[] { ".cmd", ".exe", ".bat", ".ps1", "" };
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            
            // Add npm global paths and common locations
            List<string> searchDirs = new List<string>(pathEnv.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries));
            
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            string[] additionalDirs = new string[]
            {
                Path.Combine(appData, "npm"),
                Path.Combine(localAppData, @"Programs\Microsoft VS Code\bin"),
                Path.Combine(progFiles, @"Microsoft VS Code\bin"),
                Path.Combine(progFilesX86, @"Microsoft VS Code\bin")
            };

            foreach (var ad in additionalDirs)
            {
                if (Directory.Exists(ad) && !searchDirs.Contains(ad))
                {
                    searchDirs.Add(ad);
                }
            }

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (string ext in extensions)
                {
                    string candidate = Path.Combine(dir, cmdName + ext);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }
    }
}
