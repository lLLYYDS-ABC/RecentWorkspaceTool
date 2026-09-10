using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using RecentWorkspaceWidget.Models;

namespace RecentWorkspaceWidget.Services
{
    public static class WorkspaceScanner
    {
        public static List<WorkspaceItem> ScanDirectories()
        {
            Dictionary<string, DateTime> folderTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. Current Working Directory (actively in use right now)
            try
            {
                string curDir = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(curDir))
                {
                    AddOrUpdateWorkspace(folderTimes, curDir, DateTime.Now, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: false);
                }
            }
            catch { }

            // 2. JetBrains IDEs (PyCharm, IntelliJ IDEA, WebStorm, CLion, GoLand, Rider, etc.)
            try
            {
                string[] jbRoots = new string[]
                {
                    Path.Combine(appData, "JetBrains"),
                    Path.Combine(localAppData, "JetBrains")
                };

                foreach (var jbRoot in jbRoots)
                {
                    if (Directory.Exists(jbRoot))
                    {
                        foreach (var ideDir in Directory.GetDirectories(jbRoot))
                        {
                            try
                            {
                                string xmlFile = Path.Combine(ideDir, @"options\recentProjects.xml");
                                if (File.Exists(xmlFile))
                                {
                                    string xmlContent = File.ReadAllText(xmlFile);
                                    MatchCollection entries = Regex.Matches(xmlContent, @"<entry\s+key=""([^""]+)""[^>]*>(.*?)</entry>", RegexOptions.Singleline);
                                    foreach (Match entry in entries)
                                    {
                                        string rawKey = entry.Groups[1].Value.Replace("$USER_HOME$", userProfile).Replace('/', '\\');
                                        string body = entry.Groups[2].Value;

                                        long ts = 0;
                                        Match tsMatch = Regex.Match(body, @"name=""(?:activationTimestamp|projectOpenTimestamp)""\s+value=""(\d+)""");
                                        if (tsMatch.Success)
                                        {
                                            long.TryParse(tsMatch.Groups[1].Value, out ts);
                                        }

                                        DateTime itemTime = DateTime.MinValue;
                                        if (ts > 0)
                                        {
                                            try
                                            {
                                                itemTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ts).ToLocalTime();
                                            }
                                            catch { }
                                        }

                                        if (itemTime > DateTime.MinValue)
                                        {
                                            AddOrUpdateWorkspace(folderTimes, rawKey, itemTime, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: false);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // 3. VS Code, Cursor, Windsurf, Trae, VSCodium workspaceStorage
            try
            {
                string[] wsDirs = new string[]
                {
                    Path.Combine(appData, @"Code\User\workspaceStorage"),
                    Path.Combine(appData, @"Cursor\User\workspaceStorage"),
                    Path.Combine(appData, @"Windsurf\User\workspaceStorage"),
                    Path.Combine(appData, @"Trae\User\workspaceStorage"),
                    Path.Combine(appData, @"VSCodium\User\workspaceStorage")
                };

                foreach (var wsDir in wsDirs)
                {
                    if (Directory.Exists(wsDir))
                    {
                        var dirs = new DirectoryInfo(wsDir).GetDirectories();
                        Array.Sort(dirs, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                        int limit = Math.Min(dirs.Length, 45);
                        for (int i = 0; i < limit; i++)
                        {
                            try
                            {
                                string jsonFile = Path.Combine(dirs[i].FullName, "workspace.json");
                                if (File.Exists(jsonFile))
                                {
                                    string json = File.ReadAllText(jsonFile);
                                    var m = Regex.Match(json, @"""folder""\s*:\s*""(?:file:///|file://)?([^""]+)""");
                                    if (m.Success)
                                    {
                                        string raw = Uri.UnescapeDataString(m.Groups[1].Value).Replace('/', '\\');
                                        if (raw.StartsWith("\\") && raw.Length > 2 && raw[2] == ':') raw = raw.Substring(1);

                                        DateTime itemTime = dirs[i].LastWriteTime;
                                        string dbFile = Path.Combine(dirs[i].FullName, "state.vscdb");
                                        if (File.Exists(dbFile))
                                        {
                                            DateTime dbTime = File.GetLastWriteTime(dbFile);
                                            if (dbTime > itemTime) itemTime = dbTime;
                                        }

                                        AddOrUpdateWorkspace(folderTimes, raw, itemTime, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: false);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // 4. Claude Code sessions
            try
            {
                string claudeSessions = Path.Combine(userProfile, @".claude\sessions");
                if (Directory.Exists(claudeSessions))
                {
                    var files = new DirectoryInfo(claudeSessions).GetFiles("*.json");
                    Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    int limit = Math.Min(files.Length, 25);
                    for (int i = 0; i < limit; i++)
                    {
                        try
                        {
                            string text = File.ReadAllText(files[i].FullName);
                            var m = Regex.Match(text, @"""cwd""\s*:\s*""([^""]+)""");
                            if (m.Success)
                            {
                                string cwd = m.Groups[1].Value.Replace(@"\\", @"\");
                                AddOrUpdateWorkspace(folderTimes, cwd, files[i].LastWriteTime, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: false);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 5. Active Git Repositories (Desktop, Drives, Common Projects)
            try
            {
                List<string> candidateRoots = new List<string>();
                if (Directory.Exists(desktop)) candidateRoots.Add(desktop);

                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            string altDesktop = Path.Combine(drive.RootDirectory.FullName, "Users", Environment.UserName, "Desktop");
                            if (Directory.Exists(altDesktop) && !candidateRoots.Contains(altDesktop))
                            {
                                candidateRoots.Add(altDesktop);
                            }

                            string root = drive.RootDirectory.FullName;
                            if (Directory.Exists(root) && !candidateRoots.Contains(root))
                            {
                                candidateRoots.Add(root);
                            }
                        }
                    }
                    catch { }
                }

                foreach (var baseDir in candidateRoots)
                {
                    try
                    {
                        foreach (var sub in Directory.GetDirectories(baseDir))
                        {
                            try
                            {
                                string gitDir = Path.Combine(sub, ".git");
                                if (Directory.Exists(gitDir))
                                {
                                    DateTime gitTime = GetGitRepositoryLastActiveTime(gitDir);
                                    if (gitTime > DateTime.MinValue)
                                    {
                                        AddOrUpdateWorkspace(folderTimes, sub, gitTime, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: true);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // 6. Windows Recent .lnk (filtered strictly for genuine workspaces and code files)
            try
            {
                string recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (Directory.Exists(recentDir))
                {
                    var files = new DirectoryInfo(recentDir).GetFiles("*.lnk");
                    Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

                    int maxLnk = Math.Min(files.Length, 50);
                    if (maxLnk > 0)
                    {
                        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                        if (shellType != null)
                        {
                            dynamic shell = Activator.CreateInstance(shellType);
                            for (int i = 0; i < maxLnk; i++)
                            {
                                try
                                {
                                    var file = files[i];
                                    dynamic shortcut = shell.CreateShortcut(file.FullName);
                                    string target = shortcut.TargetPath;
                                    if (string.IsNullOrEmpty(target)) continue;

                                    if (Directory.Exists(target))
                                    {
                                        AddOrUpdateWorkspace(folderTimes, target, file.LastWriteTime, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: true);
                                    }
                                    else if (File.Exists(target))
                                    {
                                        string ext = Path.GetExtension(target).ToLowerInvariant();
                                        if (IsCodeExtension(ext))
                                        {
                                            string parent = Path.GetDirectoryName(target);
                                            string root = FindWorkspaceRoot(parent);
                                            AddOrUpdateWorkspace(folderTimes, root, file.LastWriteTime, appData, localAppData, desktop, userProfile, requireWorkspaceStructure: true);
                                        }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 7. Running processes & Active IDE windows -> bump matched project to DateTime.Now
            try
            {
                Process[] processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        string pName = proc.ProcessName.ToLowerInvariant();
                        if (pName.Contains("pycharm") || pName.Contains("idea") || pName.Contains("webstorm") ||
                            pName.Contains("code") || pName.Contains("cursor") || pName.Contains("windsurf") ||
                            pName.Contains("trae") || pName.Contains("devenv") || pName.Contains("opencode") ||
                            pName.Contains("deepseek") || pName.Contains("windowsterminal") || pName.Contains("terminal"))
                        {
                            string title = proc.MainWindowTitle;
                            if (!string.IsNullOrEmpty(title))
                            {
                                foreach (var key in new List<string>(folderTimes.Keys))
                                {
                                    string wsName = Path.GetFileName(key);
                                    if (!string.IsNullOrEmpty(wsName) && wsName.Length >= 3)
                                    {
                                        // Match project name as a distinct token or title prefix/suffix
                                        string pattern = @"(?:^|[\[\(\s–—\-:·|/\\\]])" + Regex.Escape(wsName) + @"(?:$|[\]\)\s–—\-:·|/\\\[])";
                                        if (Regex.IsMatch(title, pattern, RegexOptions.IgnoreCase))
                                        {
                                            folderTimes[key] = DateTime.Now;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            // Sort by LastActive descending
            List<KeyValuePair<string, DateTime>> sorted = new List<KeyValuePair<string, DateTime>>(folderTimes);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            List<WorkspaceItem> results = new List<WorkspaceItem>();
            foreach (var kvp in sorted)
            {
                string path = kvp.Key;
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;

                string parentDir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    string parentName = Path.GetFileName(parentDir);
                    if (!string.IsNullOrEmpty(parentName) && !parentName.Equals("Desktop", StringComparison.OrdinalIgnoreCase))
                    {
                        if (name.Equals("web", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("src", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("client", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("server", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("api", StringComparison.OrdinalIgnoreCase))
                        {
                            name = parentName + " / " + name;
                        }
                    }
                }

                string drive = "";
                try { drive = Path.GetPathRoot(path).Replace("\\", "").ToUpper(); } catch { }

                string pinyin = PinyinHelper.GetPinyinInitials(name);

                results.Add(new WorkspaceItem
                {
                    Path = path,
                    Name = name,
                    Drive = drive,
                    PinyinInitials = pinyin,
                    LastActive = kvp.Value
                });

                if (results.Count >= 40) break;
            }

            return results;
        }

        private static void AddOrUpdateWorkspace(
            Dictionary<string, DateTime> folderTimes,
            string dir,
            DateTime time,
            string appData,
            string localAppData,
            string desktop,
            string userProfile,
            bool requireWorkspaceStructure)
        {
            if (string.IsNullOrEmpty(dir)) return;
            dir = NormalizeWorkspace(dir);
            if (!IsValidWorkspacePath(dir, appData, localAppData, desktop, userProfile)) return;
            if (requireWorkspaceStructure && !IsWorkspaceDirectory(dir)) return;

            if (!folderTimes.ContainsKey(dir))
            {
                folderTimes[dir] = time;
            }
            else if (time > folderTimes[dir])
            {
                folderTimes[dir] = time;
            }
        }

        private static DateTime GetGitRepositoryLastActiveTime(string gitDir)
        {
            DateTime best = DateTime.MinValue;
            try
            {
                string[] checkFiles = new string[]
                {
                    Path.Combine(gitDir, "index"),
                    Path.Combine(gitDir, "HEAD"),
                    Path.Combine(gitDir, @"logs\HEAD"),
                    Path.Combine(gitDir, "FETCH_HEAD"),
                    Path.Combine(gitDir, "COMMIT_EDITMSG")
                };

                foreach (var cf in checkFiles)
                {
                    if (File.Exists(cf))
                    {
                        DateTime wt = File.GetLastWriteTime(cf);
                        if (wt > best && wt <= DateTime.Now) best = wt;
                    }
                }

                if (best == DateTime.MinValue && Directory.Exists(gitDir))
                {
                    DateTime dt = Directory.GetLastWriteTime(gitDir);
                    if (dt <= DateTime.Now) best = dt;
                }
            }
            catch { }
            return best;
        }

        public static bool IsWorkspaceDirectory(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            try
            {
                // 1. Version control & IDE config
                if (Directory.Exists(Path.Combine(dir, ".git"))) return true;
                if (Directory.Exists(Path.Combine(dir, ".vscode"))) return true;
                if (Directory.Exists(Path.Combine(dir, ".idea"))) return true;

                // 2. Project descriptor files
                string[] projectFiles = new string[]
                {
                    "package.json", "pom.xml", "build.gradle", "build.gradle.kts",
                    "requirements.txt", "pyproject.toml", "Pipfile", "setup.py", "environment.yml",
                    "Cargo.toml", "go.mod", "composer.json", "CMakeLists.txt", "Makefile",
                    "Dockerfile", "docker-compose.yml"
                };
                foreach (var pf in projectFiles)
                {
                    if (File.Exists(Path.Combine(dir, pf))) return true;
                }

                // 3. Solution / project files
                if (Directory.GetFiles(dir, "*.sln").Length > 0) return true;
                if (Directory.GetFiles(dir, "*.csproj").Length > 0) return true;
                if (Directory.GetFiles(dir, "*.fsproj").Length > 0) return true;

                // 4. Source code directories
                if (Directory.Exists(Path.Combine(dir, "src")) ||
                    Directory.Exists(Path.Combine(dir, "app")) ||
                    Directory.Exists(Path.Combine(dir, "lib")) ||
                    Directory.Exists(Path.Combine(dir, "components")) ||
                    Directory.Exists(Path.Combine(dir, "packages")))
                {
                    return true;
                }

                // 5. Check if direct files contain source code
                foreach (var file in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (IsCodeExtension(ext)) return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsCodeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            string[] codeExts = new string[]
            {
                ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".vue",
                ".rs", ".go", ".cpp", ".c", ".cs", ".php", ".ipynb",
                ".sln", ".csproj", ".fsproj", ".json", ".yml", ".yaml", ".sql"
            };
            foreach (var ce in codeExts)
            {
                if (ext.Equals(ce, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string FindWorkspaceRoot(string startDir)
        {
            string curr = startDir;
            int depth = 0;
            while (!string.IsNullOrEmpty(curr) && depth < 4)
            {
                if (Directory.Exists(Path.Combine(curr, ".git")) ||
                    File.Exists(Path.Combine(curr, "package.json")) ||
                    File.Exists(Path.Combine(curr, "pom.xml")) ||
                    File.Exists(Path.Combine(curr, "requirements.txt")) ||
                    File.Exists(Path.Combine(curr, "pyproject.toml")) ||
                    File.Exists(Path.Combine(curr, "Cargo.toml")) ||
                    File.Exists(Path.Combine(curr, "go.mod")))
                {
                    return curr;
                }
                string parent = Path.GetDirectoryName(curr);
                if (string.IsNullOrEmpty(parent) || parent.Equals(curr, StringComparison.OrdinalIgnoreCase)) break;
                curr = parent;
                depth++;
            }
            return startDir;
        }

        private static string NormalizeWorkspace(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return dir;
            try
            {
                dir = Path.GetFullPath(dir);
            }
            catch { }
            dir = dir.TrimEnd('\\');

            if (dir.Length >= 2 && dir[1] == ':')
            {
                dir = char.ToUpperInvariant(dir[0]) + dir.Substring(1);
            }

            string[] stripSuffixes = new string[]
            {
                "\\src\\test", "\\src\\main", "\\src", "\\test", "\\tests",
                "\\docs\\", "\\doc\\", "\\bin\\", "\\obj\\", "\\dist\\",
                "\\build\\", "\\target\\", "\\.idea\\", "\\.vscode\\"
            };

            foreach (var s in stripSuffixes)
            {
                if (s.EndsWith("\\"))
                {
                    int idx = dir.IndexOf(s, StringComparison.OrdinalIgnoreCase);
                    if (idx > 0)
                    {
                        string parent = dir.Substring(0, idx);
                        if (Directory.Exists(parent)) { dir = parent; break; }
                    }
                }
                else
                {
                    if (dir.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        string parent = dir.Substring(0, dir.Length - s.Length);
                        if (Directory.Exists(parent)) { dir = parent; break; }
                    }
                }
            }
            return dir;
        }

        private static bool IsValidWorkspacePath(string dir, string appData, string localAppData, string desktop, string userProfile)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            string root = Path.GetPathRoot(dir);
            if (dir.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            if (dir.TrimEnd('\\').Equals(desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(userProfile) && dir.TrimEnd('\\').Equals(userProfile.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;

            string lower = dir.ToLowerInvariant();
            if (lower.Contains(@"\appdata\") ||
                lower.Contains(@"\.local\") ||
                lower.Contains(@"\node_modules\") ||
                lower.Contains(@"\.git\") ||
                lower.Contains(@"\xwechat_files\") ||
                lower.Contains(@"\wechat files\") ||
                lower.Contains(@"\$recycle.bin\") ||
                lower.Contains(@"\system volume information\") ||
                lower.Contains(@"\windows\") ||
                lower.Contains(@"\program files\") ||
                lower.Contains(@"\program files (x86)\"))
            {
                return false;
            }

            try
            {
                if (File.Exists(Path.Combine(dir, "unins000.exe")) ||
                    File.Exists(Path.Combine(dir, "uninstall.exe")) ||
                    File.Exists(Path.Combine(dir, "Uninstall.exe")))
                {
                    return false;
                }
            }
            catch { }

            string name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith(".")) return false;

            string[] blacklisted = new string[]
            {
                "Screenshots", "屏幕截图", "log", "logs", "Log", "Logs",
                "Downloads", "下载", "Temp", "tmp", "Desktop", "桌面",
                "qq下载", "qqmusic", "QQMusic", "weixin", "wechat", "cache",
                "bin", "obj", "dist", "build", "target", ".pnpm-store", ".npm-cache",
                "各类表格", "微信缓存", "qq缓存"
            };
            foreach (var b in blacklisted)
            {
                if (name.Equals(b, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }
    }
}
