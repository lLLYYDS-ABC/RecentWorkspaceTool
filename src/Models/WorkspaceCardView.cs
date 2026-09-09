using System.Windows.Controls;

namespace RecentWorkspaceWidget.Models
{
    public class WorkspaceCardView
    {
        public Border Card { get; set; }
        public TextBlock IndexBlock { get; set; }
        public Border DrivePill { get; set; }
        public TextBlock DriveText { get; set; }
        public TextBlock NameBlock { get; set; }
        public TextBlock PathBlock { get; set; }
        public Border ActionBtn { get; set; }
        public TextBlock ActionText { get; set; }
        public TextBlock DateBlock { get; set; }
        public WorkspaceItem Item { get; set; }
    }
}
