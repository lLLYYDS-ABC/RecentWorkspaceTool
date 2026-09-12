using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using RecentWorkspaceWidget.Models;
using RecentWorkspaceWidget.Native;

namespace RecentWorkspaceWidget.Services
{
    public static class WorkspaceScanner
    {
        private class WorkspaceCandidate
        {
            public string Path { get; set; }
            public DateTime LastActive { get; set; }
            public string Source { get; set; }
            public int Credibility { get; set; }
        }

        public static List<WorkspaceItem> ScanDirectories()
        {
            Dictionary<string, WorkspaceCandidate> candidates = new Dictionary<string, WorkspaceCandidate>(StringComparer.OrdinalIgnoreCase);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // 1. Current Working Directory
            try
            {
                string curDir = Directory.GetCurrentDirectory();
                if (!string.IsNullOrEmpty(curDir))
                {
                    AddOrUpdateWorkspace(candidates, curDir, DateTime.Now, "当前目录", 90, appData, localAppData, desktop, userProfile);
                }
            }
            catch { }

            // 2. JetBrains IDEs (recentProjects.xml)
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

                                        if (itemTime > DateTime.MinValue && itemTime <= DateTime.Now.AddHours(1))
                                        {
                                            AddOrUpdateWorkspace(candidates, rawKey, itemTime, "JetBrains IDE", 95, appData, localAppData, desktop, userProfile);
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

            // 3. VS Code / Cursor / Windsurf / Trae / VSCodium workspaceStorage
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
                                            if (dbTime > itemTime && dbTime <= DateTime.Now.AddHours(1)) itemTime = dbTime;
                                        }

                                        AddOrUpdateWorkspace(candidates, raw, itemTime, "VS Code / IDE", 90, appData, localAppData, desktop, userProfile);
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
                                AddOrUpdateWorkspace(candidates, cwd, files[i].LastWriteTime, "Claude 会话", 85, appData, localAppData, desktop, userProfile);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 5. Controlled Depth Recursion (MaxDepth = 2) on developer roots
            try
            {
                List<string> candidateRoots = new List<string>();
                if (Directory.Exists(desktop)) candidateRoots.Add(desktop);

                if (!string.IsNullOrEmpty(userProfile))
                {
                    string[] userDevFolders = new string[]
                    {
                        Path.Combine(userProfile, "source", "repos"),
                        Path.Combine(userProfile, "IdeaProjects"),
                        Path.Combine(userProfile, "PyCharmProjects"),
                        Path.Combine(userProfile, "Projects"),
                        Path.Combine(userProfile, "Workspace"),
                        Path.Combine(userProfile, "workspaces"),
                        Path.Combine(userProfile, "Code"),
                        Path.Combine(userProfile, "dev")
                    };

                    foreach (var udf in userDevFolders)
                    {
                        if (Directory.Exists(udf) && !candidateRoots.Contains(udf))
                        {
                            candidateRoots.Add(udf);
                        }
                    }
                }

                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                        {
                            string root = drive.RootDirectory.FullName;
                            string[] commonDriveFolders = new string[]
                            {
                                Path.Combine(root, "Projects"),
                                Path.Combine(root, "Project"),
                                Path.Combine(root, "Workspace"),
                                Path.Combine(root, "workspaces"),
                                Path.Combine(root, "Code"),
                                Path.Combine(root, "Development"),
                                Path.Combine(root, "repos"),
                                Path.Combine(root, "dev")
                            };

                            foreach (var cdf in commonDriveFolders)
                            {
                                if (Directory.Exists(cdf) && !candidateRoots.Contains(cdf))
                                {
                                    candidateRoots.Add(cdf);
                                }
                            }
                        }
                    }
                    catch { }
                }

                HashSet<string> visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var baseDir in candidateRoots)
                {
                    ScanControlledDepthRecursion(baseDir, 0, 2, candidates, visitedPaths, appData, localAppData, desktop, userProfile);
                }
            }
            catch { }

            // 6. Windows Recent .lnk
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
                                        string normalized = NormalizeWorkspace(target);
                                        int score;
                                        if (IsStrictWorkspaceDirectory(normalized, out score))
                                        {
                                            AddOrUpdateWorkspace(candidates, normalized, file.LastWriteTime, "最近访问", 50, appData, localAppData, desktop, userProfile);
                                        }
                                    }
                                    else if (File.Exists(target))
                                    {
                                        string ext = Path.GetExtension(target).ToLowerInvariant();
                                        if (IsCodeExtension(ext))
                                        {
                                            string parent = Path.GetDirectoryName(target);
                                            string root = FindWorkspaceRoot(parent);
                                            if (!string.IsNullOrEmpty(root))
                                            {
                                                AddOrUpdateWorkspace(candidates, root, file.LastWriteTime, "最近访问代码", 50, appData, localAppData, desktop, userProfile);
                                            }
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

            // 7. Foreground Window Identification (Tokenized & Longest-Match)
            try
            {
                IntPtr fgHwnd = Win32Api.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint pid = 0;
                    Win32Api.GetWindowThreadProcessId(fgHwnd, out pid);
                    if (pid > 0)
                    {
                        Process proc = Process.GetProcessById((int)pid);
                        if (proc != null)
                        {
                            string pName = proc.ProcessName.ToLowerInvariant();
                            if (pName.Contains("pycharm") || pName.Contains("idea") || pName.Contains("webstorm") ||
                                pName.Contains("code") || pName.Contains("cursor") || pName.Contains("windsurf") ||
                                pName.Contains("trae") || pName.Contains("devenv") || pName.Contains("opencode") ||
                                pName.Contains("windowsterminal") || pName.Contains("terminal"))
                            {
                                StringBuilder sb = new StringBuilder(512);
                                Win32Api.GetWindowText(fgHwnd, sb, sb.Capacity);
                                string title = sb.ToString();

                                if (!string.IsNullOrEmpty(title))
                                {
                                    string matchedKey = null;
                                    int maxMatchLen = 0;
                                    foreach (var kvp in candidates)
                                    {
                                        string wsName = Path.GetFileName(kvp.Key);
                                        if (!string.IsNullOrEmpty(wsName) && wsName.Length >= 3)
                                        {
                                            if (IsTitleMatchingProjectName(title, wsName))
                                            {
                                                if (wsName.Length > maxMatchLen)
                                                {
                                                    maxMatchLen = wsName.Length;
                                                    matchedKey = kvp.Key;
                                                }
                                            }
                                        }
                                    }

                                    if (matchedKey != null && candidates.ContainsKey(matchedKey))
                                    {
                                        candidates[matchedKey].LastActive = DateTime.Now;
                                        candidates[matchedKey].Source = "当前前台项目";
                                        candidates[matchedKey].Credibility += 20;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Sort by LastActive descending
            List<WorkspaceCandidate> sorted = new List<WorkspaceCandidate>(candidates.Values);
            sorted.Sort((a, b) => b.LastActive.CompareTo(a.LastActive));

            List<WorkspaceItem> results = new List<WorkspaceItem>();
            foreach (var cand in sorted)
            {
                string path = cand.Path;
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;

                string drive = "";
                try { drive = Path.GetPathRoot(path).Replace("\\", "").ToUpper(); } catch { }

                string pinyin = PinyinHelper.GetPinyinInitials(name);

                results.Add(new WorkspaceItem
                {
                    Path = path,
                    Name = name,
                    Drive = drive,
                    PinyinInitials = pinyin,
                    LastActive = cand.LastActive,
                    Source = cand.Source,
                    Credibility = cand.Credibility
                });

                if (results.Count >= 50) break;
            }

            return results;
        }

        public static bool IsTitleMatchingProjectName(string title, string wsName)
        {
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(wsName)) return false;
            if (wsName.Length < 3) return false;

            int idx = title.IndexOf(wsName, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                // Check left boundary
                bool validBefore = (idx == 0);
                if (!validBefore)
                {
                    char prev = title[idx - 1];
                    validBefore = char.IsWhiteSpace(prev) || IsTitleBoundaryChar(prev);
                }

                // Check right boundary
                int nextIdx = idx + wsName.Length;
                bool validAfter = (nextIdx >= title.Length);
                if (!validAfter)
                {
                    char next = title[nextIdx];
                    validAfter = char.IsWhiteSpace(next) || IsTitleBoundaryChar(next);
                }

                if (validBefore && validAfter) return true;

                idx = title.IndexOf(wsName, idx + 1, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static bool IsTitleBoundaryChar(char c)
        {
            return c == '-' || c == '—' || c == '–' || c == ':' || c == '·' ||
                   c == '/' || c == '\\' || c == '|' || c == '(' || c == ')' ||
                   c == '[' || c == ']' || c == '{' || c == '}' || c == '<' ||
                   c == '>' || c == '"' || c == '\'' || c == '.' || c == ',' ||
                   c == '@' || c == '#' || c == '*';
        }

        private static void ScanControlledDepthRecursion(
            string currentDir,
            int currentDepth,
            int maxDepth,
            Dictionary<string, WorkspaceCandidate> candidates,
            HashSet<string> visitedPaths,
            string appData,
            string localAppData,
            string desktop,
            string userProfile)
        {
            if (string.IsNullOrEmpty(currentDir) || !Directory.Exists(currentDir)) return;
            if (visitedPaths.Contains(currentDir)) return;
            visitedPaths.Add(currentDir);

            if (!IsValidWorkspacePath(currentDir, appData, localAppData, desktop, userProfile)) return;

            // If current directory is at depth > 0 and is a genuine workspace root:
            if (currentDepth > 0)
            {
                string gitDir = Path.Combine(currentDir, ".git");
                if (Directory.Exists(gitDir))
                {
                    DateTime gitTime = GetGitRepositoryLastActiveTime(gitDir);
                    if (gitTime > DateTime.MinValue)
                    {
                        AddOrUpdateWorkspace(candidates, currentDir, gitTime, "Git 仓库", 80, appData, localAppData, desktop, userProfile);
                    }
                    return; // Stop descending deeper inside a project repository
                }

                int score;
                if (IsStrictWorkspaceDirectory(currentDir, out score) && score >= 30)
                {
                    DateTime writeTime = Directory.GetLastWriteTime(currentDir);
                    AddOrUpdateWorkspace(candidates, currentDir, writeTime, "目录扫描", 60, appData, localAppData, desktop, userProfile);
                    return; // Stop descending once project root is identified
                }
            }

            if (currentDepth >= maxDepth) return;

            string[] subDirs = null;
            try
            {
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch { }

            if (subDirs == null) return;

            foreach (var sub in subDirs)
            {
                try
                {
                    string subName = Path.GetFileName(sub);
                    if (string.IsNullOrEmpty(subName) || subName.StartsWith(".")) continue;
                    if (IsCommonSubfolderName(subName)) continue;

                    ScanControlledDepthRecursion(sub, currentDepth + 1, maxDepth, candidates, visitedPaths, appData, localAppData, desktop, userProfile);
                }
                catch { }
            }
        }

        private static void AddOrUpdateWorkspace(
            Dictionary<string, WorkspaceCandidate> candidates,
            string dir,
            DateTime time,
            string source,
            int credibility,
            string appData,
            string localAppData,
            string desktop,
            string userProfile)
        {
            if (string.IsNullOrEmpty(dir)) return;
            dir = NormalizeWorkspace(dir);
            if (!IsValidWorkspacePath(dir, appData, localAppData, desktop, userProfile)) return;

            int detectedScore;
            if (!IsStrictWorkspaceDirectory(dir, out detectedScore)) return;

            credibility += detectedScore;

            WorkspaceCandidate existing;
            if (!candidates.TryGetValue(dir, out existing))
            {
                candidates[dir] = new WorkspaceCandidate
                {
                    Path = dir,
                    LastActive = time,
                    Source = source,
                    Credibility = credibility
                };
            }
            else
            {
                if (time > existing.LastActive)
                {
                    existing.LastActive = time;
                }
                if (credibility > existing.Credibility)
                {
                    existing.Credibility = credibility;
                    existing.Source = source;
                }
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
                        if (wt > best && wt <= DateTime.Now.AddHours(1)) best = wt;
                    }
                }

                if (best == DateTime.MinValue && Directory.Exists(gitDir))
                {
                    DateTime dt = Directory.GetLastWriteTime(gitDir);
                    if (dt <= DateTime.Now.AddHours(1)) best = dt;
                }
            }
            catch { }
            return best;
        }

        public static bool IsStrictWorkspaceDirectory(string dir, out int markerScore)
        {
            markerScore = 0;
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            try
            {
                // 1. First-class: Git repository
                if (Directory.Exists(Path.Combine(dir, ".git")))
                {
                    markerScore += 50;
                    return true;
                }

                // 2. Clear project descriptor files
                string[] primaryProjectFiles = new string[]
                {
                    "package.json", "pom.xml", "build.gradle", "build.gradle.kts",
                    "requirements.txt", "pyproject.toml", "Pipfile", "setup.py",
                    "Cargo.toml", "go.mod", "composer.json", "CMakeLists.txt",
                    "Makefile", "Directory.Build.props"
                };
                foreach (var pf in primaryProjectFiles)
                {
                    if (File.Exists(Path.Combine(dir, pf)))
                    {
                        markerScore += 40;
                        return true;
                    }
                }

                // 3. Solution or C# project files
                if (Directory.GetFiles(dir, "*.sln").Length > 0 ||
                    Directory.GetFiles(dir, "*.csproj").Length > 0 ||
                    Directory.GetFiles(dir, "*.fsproj").Length > 0)
                {
                    markerScore += 40;
                    return true;
                }

                // 4. Secondary descriptors (Dockerfile, docker-compose)
                bool hasSecondary = File.Exists(Path.Combine(dir, "Dockerfile")) ||
                                    File.Exists(Path.Combine(dir, "docker-compose.yml")) ||
                                    File.Exists(Path.Combine(dir, "environment.yml"));

                // 5. Structure + genuine code files
                bool hasStructure = Directory.Exists(Path.Combine(dir, "src")) ||
                                    Directory.Exists(Path.Combine(dir, "app")) ||
                                    Directory.Exists(Path.Combine(dir, "lib")) ||
                                    Directory.Exists(Path.Combine(dir, "components"));

                int codeFileCount = 0;
                foreach (var file in Directory.GetFiles(dir))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (IsCodeExtension(ext))
                    {
                        codeFileCount++;
                        if (codeFileCount >= 2) break;
                    }
                }

                if (hasSecondary && (codeFileCount > 0 || hasStructure))
                {
                    markerScore += 30;
                    return true;
                }

                if (hasStructure && codeFileCount >= 1)
                {
                    markerScore += 25;
                    return true;
                }

                if (codeFileCount >= 2 && !IsCommonSubfolderName(Path.GetFileName(dir)))
                {
                    markerScore += 15;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static bool IsCommonSubfolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string[] subNames = new string[]
            {
                "src", "test", "tests", "docs", "doc", "bin", "obj", "dist", "build",
                "Services", "UI", "Models", "Native", "Views", "Controllers", "Utils",
                "Helper", "Helpers", "lib", "components", "pages", "assets", "public"
            };
            foreach (var sn in subNames)
            {
                if (name.Equals(sn, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool IsCodeExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            string[] primaryCodeExts = new string[]
            {
                ".py", ".js", ".ts", ".tsx", ".jsx", ".java", ".vue",
                ".rs", ".go", ".cpp", ".c", ".cs", ".php", ".ipynb",
                ".sln", ".csproj", ".fsproj", ".rb", ".swift", ".kt"
            };
            foreach (var ce in primaryCodeExts)
            {
                if (ext.Equals(ce, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string FindWorkspaceRoot(string startDir)
        {
            if (string.IsNullOrEmpty(startDir) || !Directory.Exists(startDir)) return null;

            string bestRoot = null;
            string curr = startDir;
            int depth = 0;

            while (!string.IsNullOrEmpty(curr) && depth < 5)
            {
                int score;
                if (IsStrictWorkspaceDirectory(curr, out score))
                {
                    if (score >= 40)
                    {
                        bestRoot = curr;
                    }
                    else if (bestRoot == null)
                    {
                        bestRoot = curr;
                    }
                }

                string parent = Path.GetDirectoryName(curr);
                if (string.IsNullOrEmpty(parent) || parent.Equals(curr, StringComparison.OrdinalIgnoreCase)) break;
                curr = parent;
                depth++;
            }

            return bestRoot;
        }

        public static string NormalizeWorkspace(string dir)
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

            string curr = dir;
            for (int i = 0; i < 3; i++)
            {
                string folderName = Path.GetFileName(curr);
                if (IsCommonSubfolderName(folderName))
                {
                    string parent = Path.GetDirectoryName(curr);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        int score;
                        if (IsStrictWorkspaceDirectory(parent, out score))
                        {
                            dir = parent;
                            curr = parent;
                            continue;
                        }
                    }
                }
                break;
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
                lower.Contains(@"\program files (x86)\") ||
                lower.Contains(@"\programdata\") ||
                lower.Contains(@"\360downloads\") ||
                lower.Contains(@"\baidunetdiskdownload\") ||
                lower.Contains(@"\qldownload\"))
            {
                return false;
            }

            try
            {
                if (File.Exists(Path.Combine(dir, "unins000.exe")) ||
                    File.Exists(Path.Combine(dir, "uninstall.exe")) ||
                    File.Exists(Path.Combine(dir, "Uninstall.exe")) ||
                    File.Exists(Path.Combine(dir, "Uninstall Anyi.lnk")))
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
                "各类表格", "微信缓存", "qq缓存", "视频", "图片", "文档", "Music", "Videos", "Pictures", "Documents",
                "Open Browser下载", "ToDesk", "RustDesk", "finalshell", "ludashi", "ProgramData"
            };
            foreach (var b in blacklisted)
            {
                if (name.Equals(b, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }
    }
}