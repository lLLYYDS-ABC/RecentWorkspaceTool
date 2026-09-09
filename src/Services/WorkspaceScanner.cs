using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
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

            // 1. Explorer JumpList (AutomaticDestinations)
            try
            {
                string jumpList = Path.Combine(appData, @"Microsoft\Windows\Recent\AutomaticDestinations\f01b4d95cf55d32a.automaticDestinations-ms");
                if (File.Exists(jumpList))
                {
                    byte[] bytes = File.ReadAllBytes(jumpList);
                    string content = Encoding.Unicode.GetString(bytes);
                    MatchCollection matches = Regex.Matches(content, @"([a-zA-Z]:\\[^""<>|:*?\r\n\x00-\x1f]{3,})");
                    int order = 0;
                    DateTime baseTime = File.GetLastWriteTime(jumpList);

                    foreach (Match m in matches)
                    {
                        string p = NormalizeWorkspace(m.Value);
                        if (IsValidWorkspace(p, appData, localAppData, desktop))
                        {
                            if (!folderTimes.ContainsKey(p))
                            {
                                DateTime itemTime = baseTime.AddMinutes(-order);
                                try
                                {
                                    DateTime dirTime = Directory.GetLastWriteTime(p);
                                    if (dirTime > itemTime && dirTime <= DateTime.Now) itemTime = dirTime;
                                }
                                catch { }
                                folderTimes[p] = itemTime;
                                order++;
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. VS Code, Cursor & Windsurf workspaceStorage (ultra fast & zero RAM)
            try
            {
                string[] wsDirs = new string[]
                {
                    Path.Combine(appData, @"Code\User\workspaceStorage"),
                    Path.Combine(appData, @"Cursor\User\workspaceStorage"),
                    Path.Combine(appData, @"Windsurf\User\workspaceStorage")
                };

                foreach (var wsDir in wsDirs)
                {
                    if (Directory.Exists(wsDir))
                    {
                        var dirs = new DirectoryInfo(wsDir).GetDirectories();
                        Array.Sort(dirs, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                        int limit = Math.Min(dirs.Length, 35);
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
                                        string p = NormalizeWorkspace(raw);
                                        if (IsValidWorkspace(p, appData, localAppData, desktop))
                                        {
                                            if (!folderTimes.ContainsKey(p))
                                            {
                                                DateTime itemTime = dirs[i].LastWriteTime;
                                                try
                                                {
                                                    DateTime dirTime = Directory.GetLastWriteTime(p);
                                                    if (dirTime > itemTime && dirTime <= DateTime.Now) itemTime = dirTime;
                                                }
                                                catch { }
                                                folderTimes[p] = itemTime;
                                            }
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

            // 3. Recent .lnk (limit to top 25 recent items for fast scanning)
            try
            {
                string recentDir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (Directory.Exists(recentDir))
                {
                    var files = new DirectoryInfo(recentDir).GetFiles("*.lnk");
                    Array.Sort(files, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

                    int maxLnk = Math.Min(files.Length, 25);
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

                                    string dir = null;
                                    if (Directory.Exists(target)) dir = target;
                                    else if (File.Exists(target)) dir = Path.GetDirectoryName(target);

                                    if (!string.IsNullOrEmpty(dir))
                                    {
                                        dir = NormalizeWorkspace(dir);
                                        if (IsValidWorkspace(dir, appData, localAppData, desktop))
                                        {
                                            DateTime fileTime = file.LastWriteTime;
                                            if (!folderTimes.ContainsKey(dir) || fileTime > folderTimes[dir])
                                            {
                                                folderTimes[dir] = fileTime;
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

            // 4. Desktop directories
            try
            {
                if (Directory.Exists(desktop))
                {
                    foreach (var dir in Directory.GetDirectories(desktop))
                    {
                        string norm = NormalizeWorkspace(dir);
                        if (IsValidWorkspace(norm, appData, localAppData, desktop))
                        {
                            DateTime dt = Directory.GetLastWriteTime(norm);
                            if (!folderTimes.ContainsKey(norm) || dt > folderTimes[norm])
                            {
                                folderTimes[norm] = dt;
                            }
                        }
                    }
                }
            }
            catch { }

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

                if (results.Count >= 35) break;
            }

            return results;
        }

        private static string NormalizeWorkspace(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return dir;
            dir = dir.TrimEnd('\\');
            string[] stripSuffixes = new string[] { "\\docs\\", "\\doc\\", "\\bin\\", "\\obj\\", "\\dist\\", "\\build\\", "\\target\\", "\\.idea\\", "\\.vscode\\" };
            foreach (var s in stripSuffixes)
            {
                int idx = dir.IndexOf(s, StringComparison.OrdinalIgnoreCase);
                if (idx > 0)
                {
                    string parent = dir.Substring(0, idx);
                    if (Directory.Exists(parent)) return parent;
                }
            }
            return dir;
        }

        private static bool IsValidWorkspace(string dir, string appData, string localAppData, string desktop)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;

            string root = Path.GetPathRoot(dir);
            if (dir.TrimEnd('\\').Equals(root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
            if (dir.TrimEnd('\\').Equals(desktop.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;

            if (dir.IndexOf("\\AppData\\", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (dir.IndexOf("\\.local\\", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (dir.IndexOf("\\node_modules\\", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (dir.IndexOf("\\.git\\", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (dir.IndexOf("\\xwechat_files\\", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (dir.StartsWith(@"C:\Windows", StringComparison.OrdinalIgnoreCase)) return false;
            if (dir.StartsWith(@"C:\Program Files", StringComparison.OrdinalIgnoreCase)) return false;

            string name = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith(".")) return false;

            string[] blacklisted = new string[] { "Screenshots", "屏幕截图", "log", "logs", "Log", "Logs", "Downloads", "下载", "Temp", "tmp", "Desktop", "桌面" };
            foreach (var b in blacklisted)
            {
                if (name.Equals(b, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }
    }
}
