using System.Windows.Media;

namespace RecentWorkspaceWidget.UI
{
    public static class ThemeBrushes
    {
        public static readonly SolidColorBrush WindowBg = new SolidColorBrush(Color.FromRgb(252, 253, 252));
        public static readonly SolidColorBrush WindowBorder = new SolidColorBrush(Color.FromRgb(210, 218, 215));

        public static readonly SolidColorBrush SelectedBg = new SolidColorBrush(Color.FromRgb(237, 248, 243));
        public static readonly SolidColorBrush SelectedBorder = new SolidColorBrush(Color.FromRgb(83, 181, 146));
        public static readonly SolidColorBrush SelectedIdx = new SolidColorBrush(Color.FromRgb(35, 120, 90));
        public static readonly SolidColorBrush NormalIdx = new SolidColorBrush(Color.FromRgb(110, 130, 128));

        public static readonly SolidColorBrush SelectedDriveBg = new SolidColorBrush(Color.FromRgb(215, 240, 228));
        public static readonly SolidColorBrush NormalDriveBg = new SolidColorBrush(Color.FromRgb(232, 240, 237));
        public static readonly SolidColorBrush SelectedDriveFg = new SolidColorBrush(Color.FromRgb(35, 120, 90));
        public static readonly SolidColorBrush NormalDriveFg = new SolidColorBrush(Color.FromRgb(75, 110, 100));

        public static readonly SolidColorBrush SelectedPath = new SolidColorBrush(Color.FromRgb(60, 115, 96));
        public static readonly SolidColorBrush NormalPath = new SolidColorBrush(Color.FromRgb(105, 122, 120));

        public static readonly SolidColorBrush HoverBg = new SolidColorBrush(Color.FromArgb(170, 242, 247, 245));

        // Source badge brushes
        public static readonly SolidColorBrush BadgeBg = new SolidColorBrush(Color.FromRgb(240, 244, 242));
        public static readonly SolidColorBrush BadgeFg = new SolidColorBrush(Color.FromRgb(95, 115, 112));
        public static readonly SolidColorBrush BadgeBorder = new SolidColorBrush(Color.FromRgb(220, 228, 225));

        // Status brushes
        public static readonly SolidColorBrush StatusGreen = new SolidColorBrush(Color.FromRgb(40, 140, 95));
        public static readonly SolidColorBrush StatusWarn = new SolidColorBrush(Color.FromRgb(210, 120, 30));
        public static readonly SolidColorBrush StatusText = new SolidColorBrush(Color.FromRgb(100, 115, 113));

        static ThemeBrushes()
        {
            WindowBg.Freeze();
            WindowBorder.Freeze();
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
            BadgeBg.Freeze();
            BadgeFg.Freeze();
            BadgeBorder.Freeze();
            StatusGreen.Freeze();
            StatusWarn.Freeze();
            StatusText.Freeze();
        }
    }
}
