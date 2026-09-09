using System.Windows.Media;

namespace RecentWorkspaceWidget.UI
{
    public static class ThemeBrushes
    {
        public static readonly SolidColorBrush SelectedBg = new SolidColorBrush(Color.FromRgb(237, 248, 243));
        public static readonly SolidColorBrush SelectedBorder = new SolidColorBrush(Color.FromRgb(83, 181, 146));
        public static readonly SolidColorBrush SelectedIdx = new SolidColorBrush(Color.FromRgb(47, 132, 103));
        public static readonly SolidColorBrush NormalIdx = new SolidColorBrush(Color.FromRgb(124, 142, 140));
        public static readonly SolidColorBrush SelectedDriveBg = new SolidColorBrush(Color.FromRgb(215, 240, 228));
        public static readonly SolidColorBrush NormalDriveBg = new SolidColorBrush(Color.FromRgb(232, 240, 237));
        public static readonly SolidColorBrush SelectedDriveFg = new SolidColorBrush(Color.FromRgb(47, 132, 103));
        public static readonly SolidColorBrush NormalDriveFg = new SolidColorBrush(Color.FromRgb(93, 127, 116));
        public static readonly SolidColorBrush SelectedPath = new SolidColorBrush(Color.FromRgb(71, 128, 108));
        public static readonly SolidColorBrush NormalPath = new SolidColorBrush(Color.FromRgb(127, 145, 143));
        public static readonly SolidColorBrush HoverBg = new SolidColorBrush(Color.FromArgb(140, 240, 246, 243));

        static ThemeBrushes()
        {
            SelectedBg.Freeze();
            SelectedBorder.Freeze();
            SelectedIdx.Freeze();
            NormalIdx.Freeze();
            SelectedDriveBg.Freeze();
            NormalDriveBg.Freeze();
            SelectedDriveFg.Freeze();
            NormalDriveFg.Freeze();
            SelectedPath.Freeze();
            NormalPath.Freeze();
            HoverBg.Freeze();
        }
    }
}
