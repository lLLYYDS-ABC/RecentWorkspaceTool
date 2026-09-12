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

        public static void Initialize(bool forceRefresh = false)
        {
            if (isInitialized && !forceRefresh) return;
            lock (initLock)
            {
                if (isInitialized && !forceRefresh) return;

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
                    ExecutablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
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

        public static AgentType GetFirstAvailableCliAgent()
        {
            if (!isInitialized) Initialize();
            AgentType[] cliOrder = new AgentType[]
            {
                AgentType.OpenCode,
                AgentType.Codex,
                AgentType.ClaudeCode,
                AgentType.VSCode
            };

            foreach (var type in cliOrder)
            {
                AgentInfo info;
                if (CachedAgents.TryGetValue(type, out info) && info != null && info.IsInstalled)
                {
                    return type;
                }
            }

            return AgentType.VSCode;
        }

        public static bool HasAnyCliAgentAvailable()
        {
            if (!isInitialized) Initialize();
            AgentType[] cliOrder = new AgentType[]
            {
                AgentType.OpenCode,
                AgentType.Codex,
                AgentType.ClaudeCode,
                AgentType.VSCode
            };

            foreach (var type in cliOrder)
            {
                AgentInfo info;
                if (CachedAgents.TryGetValue(type, out info) && info != null && info.IsInstalled)
                {
                    return true;
                }
            }
            return false;
        }

        private static AgentInfo DetectOpenCode()
        {
            string exec = FindCommandInPath("opencode");
            bool installed = !string.IsNullOrEmpty(exec) && File.Exists(exec);

            return new AgentInfo
            {
                Type = AgentType.OpenCode,
                Name = "OpenCode",
                KeyHint = "Enter",
                IsInstalled = installed,
                ExecutablePath = exec,
                CommandTemplate = "title OpenCode: {name} && cd /d \"{path}\" && opencode ."
            };
        }

        private static AgentInfo DetectClaudeCode()
        {
            string exec = FindCommandInPath("claude");
            bool installed = !string.IsNullOrEmpty(exec) && File.Exists(exec);

            return new AgentInfo
            {
                Type = AgentType.ClaudeCode,
                Name = "Claude Code",
                KeyHint = "Shift+Enter",
                IsInstalled = installed,
                ExecutablePath = exec,
                CommandTemplate = "title Claude: {name} && cd /d \"{path}\" && claude"
            };
        }

        private static AgentInfo DetectCodex()
        {
            string exec = FindCommandInPath("codex");
            bool installed = !string.IsNullOrEmpty(exec) && File.Exists(exec);

            return new AgentInfo
            {
                Type = AgentType.Codex,
                Name = "OpenAI Codex",
                KeyHint = "Alt+Enter",
                IsInstalled = installed,
                ExecutablePath = exec,
                CommandTemplate = "title Codex: {name} && cd /d \"{path}\" && codex"
            };
        }

        private static AgentInfo DetectVSCode()
        {
            string exec = FindCommandInPath("code");
            bool installed = !string.IsNullOrEmpty(exec) && File.Exists(exec);

            return new AgentInfo
            {
                Type = AgentType.VSCode,
                Name = "VS Code",
                KeyHint = "Ctrl+Enter",
                IsInstalled = installed,
                ExecutablePath = exec,
                CommandTemplate = "code \"{path}\""
            };
        }

        public static string FindCommandInPath(string cmdName)
        {
            if (string.IsNullOrEmpty(cmdName)) return null;

            if (Path.IsPathRooted(cmdName) && File.Exists(cmdName))
            {
                return cmdName;
            }

            // Search order: .cmd, .exe, .bat first for native execution; .ps1 as script alternative
            string[] primaryExts = new string[] { ".cmd", ".exe", ".bat", "" };
            string[] scriptExts = new string[] { ".ps1" };

            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            List<string> searchDirs = new List<string>(pathEnv.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries));

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            List<string> additionalDirs = new List<string>
            {
                Path.Combine(appData, "npm"),
                Path.Combine(userProfile, @"AppData\Roaming\npm"),
                Path.Combine(localAppData, @"Programs\Microsoft VS Code\bin"),
                Path.Combine(localAppData, @"Programs\Microsoft VS Code"),
                Path.Combine(progFiles, @"Microsoft VS Code\bin"),
                Path.Combine(progFiles, @"Microsoft VS Code"),
                Path.Combine(progFilesX86, @"Microsoft VS Code\bin")
            };

            // Check npm-global across fixed drives
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        string driveNpm = Path.Combine(drive.RootDirectory.FullName, "npm-global");
                        if (Directory.Exists(driveNpm) && !additionalDirs.Contains(driveNpm))
                        {
                            additionalDirs.Add(driveNpm);
                        }
                    }
                }
            }
            catch { }

            foreach (var ad in additionalDirs)
            {
                if (Directory.Exists(ad) && !searchDirs.Contains(ad))
                {
                    searchDirs.Add(ad);
                }
            }

            // 1. Check primary binary/batch extensions first
            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (string ext in primaryExts)
                {
                    string candidate = Path.Combine(dir, cmdName + ext);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            // 2. Check PowerShell scripts if no native cmd/exe was found
            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (string ext in scriptExts)
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