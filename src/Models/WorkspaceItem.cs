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
    }
}
