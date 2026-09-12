using System;

namespace RecentWorkspaceWidget.Models
{
    public class WorkspaceItem
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public string Drive { get; set; }
        public string PinyinInitials { get; set; }
        public DateTime LastActive { get; set; }
        public string Source { get; set; }        // e.g. "Git 项目", "IDE 最近项目", "Agent 会话", "最近访问", "启发式扫描"
        public int Credibility { get; set; }       // Higher credibility = more reliable workspace marker
    }
}
