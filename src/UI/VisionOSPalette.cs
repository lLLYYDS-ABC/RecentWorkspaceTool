using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Threading;
using RecentWorkspaceWidget.Models;
using RecentWorkspaceWidget.Native;
using RecentWorkspaceWidget.Services;

namespace RecentWorkspaceWidget.UI
{
    public class VisionOSPalette : Window
    {
        public static EventWaitHandle WakeupEvent;

        // Active Agent state
        private AgentType currentAgent = AgentType.OpenCode;
        private readonly AgentType[] availableAgents = new AgentType[]
        {
            AgentType.OpenCode,
            AgentType.ClaudeCode,
            AgentType.Codex,
            AgentType.VSCode
        };
        private Dictionary<AgentType, Border> agentTabPills = new Dictionary<AgentType, Border>();
        private Dictionary<AgentType, TextBlock> agentTabTexts = new Dictionary<AgentType, TextBlock>();

        // Data
        private List<WorkspaceItem> allItems = new List<WorkspaceItem>();
        private List<WorkspaceItem> filteredItems = new List<WorkspaceItem>();
        private List<WorkspaceCardView> cardViews = new List<WorkspaceCardView>();
        private int selectedIndex = 0;
        private DateTime lastScanTime = DateTime.MinValue;
        private bool hasLoadedWorkspaces = false;
        private bool isRefreshing = false;
        private DispatcherTimer filterTimer;
        private DispatcherTimer copyFeedbackTimer;

        // UI Controls
        private TextBox searchBox;
        private TextBlock placeholderText;
        private StackPanel listPanel;
        private ScrollViewer scrollViewer;
        private TextBlock footerLeft;

        // Cached Brushes for Agent Selector
        private static readonly SolidColorBrush TabActiveBg = Brushes.White;
        private static readonly SolidColorBrush TabActiveBorder = new SolidColorBrush(Color.FromRgb(215, 226, 222));
        private static readonly SolidColorBrush TabActiveFg = new SolidColorBrush(Color.FromRgb(30, 110, 82));
        private static readonly SolidColorBrush TabInactiveBg = Brushes.Transparent;
        private static readonly SolidColorBrush TabInactiveBorder = Brushes.Transparent;
        private static readonly SolidColorBrush TabInactiveFg = new SolidColorBrush(Color.FromRgb(118, 136, 134));
        private static readonly SolidColorBrush TabHoverBg = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255));

        public VisionOSPalette()
        {
            this.Title = "最近工作空间 - AI Agent 启动中心";
            this.Width = 848;
            this.Height = 588;
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.UseLayoutRounding = true;
            this.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Topmost = true;
            this.ShowInTaskbar = false;

            AgentDetector.Initialize();

            BuildUI();
            this.PreviewKeyDown += Window_PreviewKeyDown;

            this.Loaded += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd).AddHook(WndProc);

                Win32Api.RegisterHotKey(hwnd, Win32Api.HOTKEY_ID_ALTW, Win32Api.MOD_ALT, 0x57); // Alt+W
                Win32Api.RegisterHotKey(hwnd, Win32Api.HOTKEY_ID_ALTO, Win32Api.MOD_ALT, 0x4F); // Alt+O
                Win32Api.RegisterHotKey(hwnd, Win32Api.HOTKEY_ID_ALTSPACE, Win32Api.MOD_ALT, 0x20); // Alt+Space

                RefreshWorkspacesAsync(true);
                WakeUpPalette();
            };

            this.Closed += (s, e) =>
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Api.UnregisterHotKey(hwnd, Win32Api.HOTKEY_ID_ALTW);
                Win32Api.UnregisterHotKey(hwnd, Win32Api.HOTKEY_ID_ALTO);
                Win32Api.UnregisterHotKey(hwnd, Win32Api.HOTKEY_ID_ALTSPACE);
            };

            // Kernel Event IPC Listener
            ThreadPool.QueueUserWorkItem((state) =>
            {
                while (true)
                {
                    try
                    {
                        if (WakeupEvent != null)
                        {
                            WakeupEvent.WaitOne();
                            this.Dispatcher.BeginInvoke(new Action(() => WakeUpPalette()));
                        }
                        else
                        {
                            Thread.Sleep(200);
                        }
                    }
                    catch { }
                }
            });
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Api.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == Win32Api.HOTKEY_ID_ALTW || id == Win32Api.HOTKEY_ID_ALTO || id == Win32Api.HOTKEY_ID_ALTSPACE)
                {
                    if (this.Visibility == Visibility.Visible)
                        HidePalette();
                    else
                        WakeUpPalette();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void WakeUpPalette()
        {
            CenterOnActiveScreen();

            if (searchBox != null)
            {
                if (!string.IsNullOrEmpty(searchBox.Text))
                {
                    searchBox.Text = "";
                }
                else
                {
                    selectedIndex = 0;
                    if (cardViews.Count > 0)
                    {
                        UpdateCardSelectionVisuals(-1, 0);
                        EnsureSelectionVisible();
                    }
                }
            }

            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Normal;
            this.Topmost = true;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                Win32Api.BringWindowToTop(hwnd);
                Win32Api.SetForegroundWindow(hwnd);
            }
            catch { }

            this.Activate();
            if (searchBox != null)
            {
                searchBox.Focus();
                Keyboard.Focus(searchBox);
            }

            if (!hasLoadedWorkspaces || (DateTime.Now - lastScanTime).TotalSeconds > 30)
            {
                RefreshWorkspacesAsync(false);
            }
        }

        private void CenterOnActiveScreen()
        {
            try
            {
                Win32Api.POINT p;
                if (Win32Api.GetCursorPos(out p))
                {
                    IntPtr hMon = Win32Api.MonitorFromPoint(p, Win32Api.MONITOR_DEFAULTTONEAREST);
                    if (hMon != IntPtr.Zero)
                    {
                        Win32Api.MONITORINFO mi = new Win32Api.MONITORINFO();
                        mi.cbSize = Marshal.SizeOf(typeof(Win32Api.MONITORINFO));
                        if (Win32Api.GetMonitorInfo(hMon, ref mi))
                        {
                            double workLeft = mi.rcWork.Left;
                            double workTop = mi.rcWork.Top;
                            double workWidth = mi.rcWork.Right - mi.rcWork.Left;
                            double workHeight = mi.rcWork.Bottom - mi.rcWork.Top;

                            this.Left = workLeft + (workWidth - this.Width) / 2;
                            this.Top = workTop + (workHeight - this.Height) / 2;
                            return;
                        }
                    }
                }
            }
            catch { }

            this.Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - this.Width) / 2;
            this.Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - this.Height) / 2;
        }

        public void HidePalette()
        {
            this.Visibility = Visibility.Hidden;
            MemoryOptimizer.TrimMemory();
        }

        private void BuildUI()
        {
            Border shadowChassis = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(250, 251, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(218, 223, 221)),
                BorderThickness = new Thickness(1),
                SnapsToDevicePixels = true,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Direction = 270,
                    Color = Color.FromRgb(0, 0, 0),
                    Opacity = 0.14
                }
            };

            shadowChassis.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    this.DragMove();
            };

            Grid mainGrid = new Grid();
            mainGrid.SnapsToDevicePixels = true;
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Search
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 2: List
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3: Footer

            // --- Row 0: Header with Agent Segmented Tabs ---
            Grid headerPanel = new Grid { Margin = new Thickness(24, 18, 24, 12) };
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Brand
            System.Windows.Shapes.Ellipse greenDot = new System.Windows.Shapes.Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = new SolidColorBrush(Color.FromRgb(56, 178, 133)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0),
                SnapsToDevicePixels = true
            };

            TextBlock titleBlock = new TextBlock
            {
                Text = "最近工作空间",
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(28, 39, 39)),
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal };
            brand.Children.Add(greenDot);
            brand.Children.Add(titleBlock);
            Grid.SetColumn(brand, 0);
            headerPanel.Children.Add(brand);

            // Agent Mode Selector Segment
            Border tabCapsule = new Border
            {
                Height = 32,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(Color.FromRgb(237, 242, 240)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 225)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            StackPanel tabStack = new StackPanel { Orientation = Orientation.Horizontal };

            foreach (var agent in availableAgents)
            {
                AgentType at = agent;
                string label = GetAgentShortName(at);
                bool isActive = (at == currentAgent);

                Border pill = new Border
                {
                    Height = 24,
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(11, 0, 11, 0),
                    Margin = new Thickness(1, 0, 1, 0),
                    Cursor = Cursors.Hand,
                    Background = isActive ? TabActiveBg : TabInactiveBg,
                    BorderBrush = isActive ? TabActiveBorder : TabInactiveBorder,
                    BorderThickness = new Thickness(isActive ? 1 : 0)
                };

                if (isActive)
                {
                    pill.Effect = new DropShadowEffect
                    {
                        BlurRadius = 5,
                        ShadowDepth = 1,
                        Direction = 270,
                        Color = Color.FromRgb(0, 0, 0),
                        Opacity = 0.08
                    };
                }

                TextBlock pillText = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isActive ? TabActiveFg : TabInactiveFg,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                pill.Child = pillText;

                pill.MouseEnter += (s, e) =>
                {
                    if (at != currentAgent)
                    {
                        pill.Background = TabHoverBg;
                    }
                };
                pill.MouseLeave += (s, e) =>
                {
                    if (at != currentAgent)
                    {
                        pill.Background = TabInactiveBg;
                    }
                };

                pill.MouseLeftButtonDown += (s, e) =>
                {
                    SelectAgent(at);
                };

                agentTabPills[at] = pill;
                agentTabTexts[at] = pillText;
                tabStack.Children.Add(pill);
            }

            tabCapsule.Child = tabStack;
            Grid.SetColumn(tabCapsule, 2);
            headerPanel.Children.Add(tabCapsule);

            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            // --- Row 1: Search Box Capsule ---
            Border searchCapsule = new Border
            {
                Height = 46,
                CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromRgb(243, 246, 245)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 226, 224)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(24, 0, 24, 14)
            };

            Grid searchGrid = new Grid();
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            TextBlock searchIcon = new TextBlock
            {
                Text = "⌕",
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromRgb(76, 157, 135)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(searchIcon, 0);
            searchGrid.Children.Add(searchIcon);

            searchBox = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 43, 42)),
                FontSize = 13,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
                CaretBrush = new SolidColorBrush(Color.FromRgb(68, 157, 132))
            };
            Grid.SetColumn(searchBox, 1);

            placeholderText = new TextBlock
            {
                Text = "搜索工作空间名称、拼音简拼或路径...",
                Foreground = new SolidColorBrush(Color.FromRgb(137, 151, 149)),
                FontSize = 13,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                IsHitTestVisible = false
            };
            Grid.SetColumn(placeholderText, 1);

            searchBox.TextChanged += (s, e) =>
            {
                placeholderText.Visibility = string.IsNullOrEmpty(searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                if (string.IsNullOrEmpty(searchBox.Text))
                {
                    filterTimer.Stop();
                    ExecuteFilter("");
                }
                else
                {
                    filterTimer.Stop();
                    filterTimer.Start();
                }
            };

            filterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            filterTimer.Tick += (s, e) =>
            {
                filterTimer.Stop();
                ExecuteFilter(searchBox.Text);
            };

            copyFeedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            copyFeedbackTimer.Tick += (s, e) =>
            {
                copyFeedbackTimer.Stop();
                UpdateFooterText();
            };

            searchGrid.Children.Add(placeholderText);
            searchGrid.Children.Add(searchBox);
            searchCapsule.Child = searchGrid;

            Grid.SetRow(searchCapsule, 1);
            mainGrid.Children.Add(searchCapsule);

            // --- Row 2: List of Cards ---
            scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Margin = new Thickness(24, 0, 24, 10)
            };

            listPanel = new StackPanel();
            scrollViewer.Content = listPanel;

            Grid.SetRow(scrollViewer, 2);
            mainGrid.Children.Add(scrollViewer);

            // --- Row 3: Footer ---
            DockPanel footerPanel = new DockPanel { Margin = new Thickness(26, 0, 26, 16) };

            footerLeft = new TextBlock
            {
                Text = GetDefaultFooterText(),
                Foreground = new SolidColorBrush(Color.FromRgb(125, 140, 138)),
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(footerLeft, Dock.Left);

            TextBlock footerRight = new TextBlock
            {
                Text = "Alt+W 唤出",
                Foreground = new SolidColorBrush(Color.FromRgb(125, 140, 138)),
                FontSize = 11,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(footerRight, Dock.Right);

            footerPanel.Children.Add(footerRight);
            footerPanel.Children.Add(footerLeft);

            Grid.SetRow(footerPanel, 3);
            mainGrid.Children.Add(footerPanel);

            shadowChassis.Child = mainGrid;
            this.Content = shadowChassis;
        }

        private void SelectAgent(AgentType agent)
        {
            if (currentAgent == agent) return;
            currentAgent = agent;
            UpdateAgentTabsVisual();
            UpdateCardActionButtonsVisual();
            UpdateFooterText();
        }

        private void CycleNextAgent()
        {
            int idx = Array.IndexOf(availableAgents, currentAgent);
            int nextIdx = (idx + 1) % availableAgents.Length;
            SelectAgent(availableAgents[nextIdx]);
        }

        private void UpdateAgentTabsVisual()
        {
            foreach (var kvp in agentTabPills)
            {
                AgentType at = kvp.Key;
                Border pill = kvp.Value;
                TextBlock text = agentTabTexts[at];

                bool isActive = (at == currentAgent);
                pill.Background = isActive ? TabActiveBg : TabInactiveBg;
                pill.BorderBrush = isActive ? TabActiveBorder : TabInactiveBorder;
                pill.BorderThickness = new Thickness(isActive ? 1 : 0);

                if (isActive)
                {
                    pill.Effect = new DropShadowEffect
                    {
                        BlurRadius = 5,
                        ShadowDepth = 1,
                        Direction = 270,
                        Color = Color.FromRgb(0, 0, 0),
                        Opacity = 0.08
                    };
                    text.FontWeight = FontWeights.SemiBold;
                    text.Foreground = TabActiveFg;
                }
                else
                {
                    pill.Effect = null;
                    text.FontWeight = FontWeights.Normal;
                    text.Foreground = TabInactiveFg;
                }
            }
        }

        private void UpdateCardActionButtonsVisual()
        {
            string label = GetAgentActionLabel(currentAgent);
            foreach (var cardView in cardViews)
            {
                if (cardView.ActionText != null)
                {
                    cardView.ActionText.Text = label;
                }
            }
        }

        private void UpdateFooterText()
        {
            if (footerLeft != null)
            {
                footerLeft.Text = GetDefaultFooterText();
                footerLeft.Foreground = new SolidColorBrush(Color.FromRgb(125, 140, 138));
            }
        }

        private string GetDefaultFooterText()
        {
            return string.Format("回车 启动 {0}  ·  ↑↓ 选择项目  ·  Ctrl+C 复制路径  ·  Ctrl+E 资源管理器  ·  Esc 退出", GetAgentDisplayName(currentAgent));
        }

        private string GetAgentShortName(AgentType agent)
        {
            switch (agent)
            {
                case AgentType.OpenCode: return "OpenCode";
                case AgentType.ClaudeCode: return "Claude Code";
                case AgentType.Codex: return "Codex";
                case AgentType.VSCode: return "VS Code";
                default: return agent.ToString();
            }
        }

        private string GetAgentDisplayName(AgentType agent)
        {
            switch (agent)
            {
                case AgentType.OpenCode: return "OpenCode";
                case AgentType.ClaudeCode: return "Claude Code";
                case AgentType.Codex: return "OpenAI Codex";
                case AgentType.VSCode: return "VS Code";
                case AgentType.Explorer: return "资源管理器";
                default: return agent.ToString();
            }
        }

        private string GetAgentActionLabel(AgentType agent)
        {
            switch (agent)
            {
                case AgentType.OpenCode: return "↵ 启动 OpenCode";
                case AgentType.ClaudeCode: return "↵ 启动 Claude";
                case AgentType.Codex: return "↵ 启动 Codex";
                case AgentType.VSCode: return "↵ 打开 VS Code";
                default: return "↵ 打开";
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (searchBox != null && !string.IsNullOrEmpty(searchBox.Text))
                {
                    searchBox.Text = "";
                }
                else
                {
                    HidePalette();
                }
                e.Handled = true;
                return;
            }

            // Tab key cycles through agents when searchBox is empty, or with Ctrl+Tab
            if ((e.Key == Key.Tab && (searchBox == null || string.IsNullOrEmpty(searchBox.Text))) ||
                (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
            {
                CycleNextAgent();
                e.Handled = true;
                return;
            }

            // Ctrl + C: Copy selected path to clipboard
            if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (filteredItems != null && selectedIndex >= 0 && selectedIndex < filteredItems.Count)
                {
                    try
                    {
                        Clipboard.SetText(filteredItems[selectedIndex].Path);
                        if (footerLeft != null)
                        {
                            footerLeft.Text = "✓ 已复制工作空间路径: " + filteredItems[selectedIndex].Path;
                            footerLeft.Foreground = new SolidColorBrush(Color.FromRgb(47, 132, 103));
                            copyFeedbackTimer.Stop();
                            copyFeedbackTimer.Start();
                        }
                    }
                    catch { }
                    e.Handled = true;
                    return;
                }
            }

            // Ctrl + E: Open in Explorer
            if (e.Key == Key.E && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (filteredItems != null && selectedIndex >= 0 && selectedIndex < filteredItems.Count)
                {
                    LaunchWorkspace(filteredItems[selectedIndex], AgentType.Explorer);
                    e.Handled = true;
                    return;
                }
            }

            // Alt + 1~9: Direct index launch with currently active Agent
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                int digitIndex = e.Key - Key.D1;
                if (filteredItems != null && digitIndex < filteredItems.Count)
                {
                    LaunchWorkspace(filteredItems[digitIndex], currentAgent);
                    e.Handled = true;
                    return;
                }
            }

            // Direct Enter: Launch with currently active Agent! (No combinations needed!)
            if (e.Key == Key.Enter)
            {
                if (filteredItems != null && selectedIndex >= 0 && selectedIndex < filteredItems.Count)
                {
                    LaunchWorkspace(filteredItems[selectedIndex], currentAgent);
                    e.Handled = true;
                    return;
                }
            }

            // Navigation
            if (filteredItems != null && filteredItems.Count > 0)
            {
                if (e.Key == Key.Down)
                {
                    ChangeSelection((selectedIndex + 1) % filteredItems.Count);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.Up)
                {
                    ChangeSelection((selectedIndex - 1 + filteredItems.Count) % filteredItems.Count);
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.PageDown)
                {
                    ChangeSelection(Math.Min(filteredItems.Count - 1, selectedIndex + 5));
                    e.Handled = true;
                    return;
                }
                else if (e.Key == Key.PageUp)
                {
                    ChangeSelection(Math.Max(0, selectedIndex - 5));
                    e.Handled = true;
                    return;
                }
            }
        }

        private void ChangeSelection(int newIndex)
        {
            if (filteredItems == null || filteredItems.Count == 0) return;
            if (newIndex < 0) newIndex = filteredItems.Count - 1;
            if (newIndex >= filteredItems.Count) newIndex = 0;

            if (newIndex == selectedIndex) return;

            int oldIndex = selectedIndex;
            selectedIndex = newIndex;
            UpdateCardSelectionVisuals(oldIndex, newIndex);
            EnsureSelectionVisible();
        }

        private void UpdateCardSelectionVisuals(int oldIndex, int newIndex)
        {
            if (oldIndex >= 0 && oldIndex < cardViews.Count)
            {
                var v = cardViews[oldIndex];
                v.Card.Background = Brushes.Transparent;
                v.Card.BorderBrush = Brushes.Transparent;
                v.Card.BorderThickness = new Thickness(0);
                v.IndexBlock.Foreground = ThemeBrushes.NormalIdx;
                if (v.DrivePill != null)
                {
                    v.DrivePill.Background = ThemeBrushes.NormalDriveBg;
                    v.DriveText.Foreground = ThemeBrushes.NormalDriveFg;
                }
                v.PathBlock.Foreground = ThemeBrushes.NormalPath;
                v.ActionBtn.Visibility = Visibility.Collapsed;
                v.DateBlock.Visibility = Visibility.Visible;
            }

            if (newIndex >= 0 && newIndex < cardViews.Count)
            {
                var v = cardViews[newIndex];
                v.Card.Background = ThemeBrushes.SelectedBg;
                v.Card.BorderBrush = ThemeBrushes.SelectedBorder;
                v.Card.BorderThickness = new Thickness(1);
                v.IndexBlock.Foreground = ThemeBrushes.SelectedIdx;
                if (v.DrivePill != null)
                {
                    v.DrivePill.Background = ThemeBrushes.SelectedDriveBg;
                    v.DriveText.Foreground = ThemeBrushes.SelectedDriveFg;
                }
                v.PathBlock.Foreground = ThemeBrushes.SelectedPath;
                v.ActionBtn.Visibility = Visibility.Visible;
                v.DateBlock.Visibility = Visibility.Collapsed;
            }
        }

        private void EnsureSelectionVisible()
        {
            if (selectedIndex >= 0 && selectedIndex < cardViews.Count)
            {
                cardViews[selectedIndex].Card.BringIntoView();
            }
        }

        private void ExecuteFilter(string query)
        {
            string q = (query ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(q))
            {
                filteredItems = new List<WorkspaceItem>(allItems);
            }
            else
            {
                string[] terms = q.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var scoredList = new List<Tuple<int, WorkspaceItem>>();

                foreach (var item in allItems)
                {
                    string nameL = item.Name.ToLower();
                    string pathL = item.Path.ToLower();
                    string pyL = item.PinyinInitials ?? "";

                    bool match = true;
                    int totalScore = 0;

                    foreach (var term in terms)
                    {
                        int termScore = 0;
                        if (nameL.Equals(term, StringComparison.OrdinalIgnoreCase))
                        {
                            termScore = 1000;
                        }
                        else if (nameL.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                        {
                            termScore = 600;
                        }
                        else if (nameL.Contains(term))
                        {
                            termScore = 350;
                        }
                        else if (!string.IsNullOrEmpty(pyL) && pyL.Contains(term))
                        {
                            termScore = 250;
                        }
                        else if (pathL.Contains(term))
                        {
                            termScore = 100;
                        }
                        else
                        {
                            match = false;
                            break;
                        }
                        totalScore += termScore;
                    }

                    if (match)
                    {
                        double daysAgo = (DateTime.Now - item.LastActive).TotalDays;
                        if (daysAgo <= 1) totalScore += 35;
                        else if (daysAgo <= 3) totalScore += 25;
                        else if (daysAgo <= 7) totalScore += 15;

                        scoredList.Add(new Tuple<int, WorkspaceItem>(totalScore, item));
                    }
                }

                scoredList.Sort((a, b) =>
                {
                    int cmp = b.Item1.CompareTo(a.Item1);
                    if (cmp != 0) return cmp;
                    return b.Item2.LastActive.CompareTo(a.Item2.LastActive);
                });

                filteredItems = new List<WorkspaceItem>();
                foreach (var tuple in scoredList)
                {
                    filteredItems.Add(tuple.Item2);
                }
            }

            selectedIndex = 0;
            RenderList();
        }

        private void RenderList()
        {
            listPanel.Children.Clear();
            cardViews.Clear();

            if (isRefreshing && allItems.Count == 0)
            {
                TextBlock loadingBlock = new TextBlock
                {
                    Text = "正在读取最近工作空间...",
                    Foreground = new SolidColorBrush(Color.FromRgb(127, 143, 141)),
                    FontSize = 13,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                listPanel.Children.Add(loadingBlock);
                return;
            }

            if (filteredItems.Count == 0)
            {
                TextBlock emptyBlock = new TextBlock
                {
                    Text = "没有匹配的工作空间",
                    Foreground = new SolidColorBrush(Color.FromRgb(127, 143, 141)),
                    FontSize = 13,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                listPanel.Children.Add(emptyBlock);
                return;
            }

            string currentActionLabel = GetAgentActionLabel(currentAgent);

            for (int i = 0; i < filteredItems.Count; i++)
            {
                int index = i;
                WorkspaceItem item = filteredItems[i];
                bool isSelected = (i == selectedIndex);

                Border card = new Border
                {
                    Height = 56,
                    CornerRadius = new CornerRadius(12),
                    Margin = new Thickness(0, 3, 0, 3),
                    Padding = new Thickness(16, 0, 16, 0),
                    Cursor = Cursors.Hand,
                    Background = isSelected ? ThemeBrushes.SelectedBg : Brushes.Transparent,
                    BorderBrush = isSelected ? ThemeBrushes.SelectedBorder : Brushes.Transparent,
                    BorderThickness = new Thickness(isSelected ? 1 : 0)
                };

                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // 1. Index
                TextBlock idxBlock = new TextBlock
                {
                    Text = string.Format("{0:D2}", i + 1),
                    Foreground = isSelected ? ThemeBrushes.SelectedIdx : ThemeBrushes.NormalIdx,
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(idxBlock, 0);
                rowGrid.Children.Add(idxBlock);

                // 2. Drive Pill
                Border drivePill = null;
                TextBlock driveText = null;
                if (!string.IsNullOrEmpty(item.Drive))
                {
                    drivePill = new Border
                    {
                        Width = 22,
                        Height = 17,
                        CornerRadius = new CornerRadius(4),
                        Background = isSelected ? ThemeBrushes.SelectedDriveBg : ThemeBrushes.NormalDriveBg,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    driveText = new TextBlock
                    {
                        Text = item.Drive,
                        Foreground = isSelected ? ThemeBrushes.SelectedDriveFg : ThemeBrushes.NormalDriveFg,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    drivePill.Child = driveText;
                    Grid.SetColumn(drivePill, 1);
                    rowGrid.Children.Add(drivePill);
                }

                // 3. Info Stack
                StackPanel infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                TextBlock nameBlock = new TextBlock
                {
                    Text = item.Name,
                    Foreground = new SolidColorBrush(Color.FromRgb(32, 43, 42)),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI")
                };
                string pathFormatted = item.Path.Replace("\\", " › ");
                TextBlock pathBlock = new TextBlock
                {
                    Text = pathFormatted,
                    Foreground = isSelected ? ThemeBrushes.SelectedPath : ThemeBrushes.NormalPath,
                    FontSize = 10.5,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                infoStack.Children.Add(nameBlock);
                infoStack.Children.Add(pathBlock);
                Grid.SetColumn(infoStack, 2);
                rowGrid.Children.Add(infoStack);

                // 4. Action Pill & Date
                Border actionBtn = new Border
                {
                    Height = 24,
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromRgb(222, 243, 232)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(143, 207, 177)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
                };
                TextBlock actionText = new TextBlock
                {
                    Text = currentActionLabel,
                    Foreground = new SolidColorBrush(Color.FromRgb(42, 121, 91)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                actionBtn.Child = actionText;
                Grid.SetColumn(actionBtn, 3);
                rowGrid.Children.Add(actionBtn);

                TextBlock dateBlock = new TextBlock
                {
                    Text = item.LastActive.Date == DateTime.Today
                        ? item.LastActive.ToString("HH:mm")
                        : (item.LastActive.Date == DateTime.Today.AddDays(-1) ? "昨天" : item.LastActive.ToString("MM-dd")),
                    Foreground = new SolidColorBrush(Color.FromRgb(127, 145, 143)),
                    FontSize = 11,
                    FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = isSelected ? Visibility.Collapsed : Visibility.Visible
                };
                Grid.SetColumn(dateBlock, 3);
                rowGrid.Children.Add(dateBlock);

                card.Child = rowGrid;

                Border currentCard = card;
                int currentIndex = index;
                currentCard.MouseEnter += (s, e) =>
                {
                    if (currentIndex != selectedIndex)
                    {
                        currentCard.Background = ThemeBrushes.HoverBg;
                    }
                };
                currentCard.MouseLeave += (s, e) =>
                {
                    if (currentIndex != selectedIndex)
                    {
                        currentCard.Background = Brushes.Transparent;
                    }
                };

                // Left click: Launch with currently active Agent
                card.MouseLeftButtonDown += (s, e) =>
                {
                    LaunchWorkspace(item, currentAgent);
                };

                // Right click: Open Agent Selection Context Menu
                card.MouseRightButtonUp += (s, e) =>
                {
                    ChangeSelection(currentIndex);
                    ContextMenu menu = CreateContextMenu(item);
                    menu.PlacementTarget = currentCard;
                    menu.IsOpen = true;
                    e.Handled = true;
                };

                listPanel.Children.Add(card);

                cardViews.Add(new WorkspaceCardView
                {
                    Card = card,
                    IndexBlock = idxBlock,
                    DrivePill = drivePill,
                    DriveText = driveText,
                    NameBlock = nameBlock,
                    PathBlock = pathBlock,
                    ActionBtn = actionBtn,
                    ActionText = actionText,
                    DateBlock = dateBlock,
                    Item = item
                });
            }
        }

        private ContextMenu CreateContextMenu(WorkspaceItem item)
        {
            ContextMenu menu = new ContextMenu
            {
                FontSize = 12,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI")
            };

            MenuItem miOpenCode = new MenuItem { Header = "启动 OpenCode", FontWeight = (currentAgent == AgentType.OpenCode ? FontWeights.Bold : FontWeights.Normal) };
            miOpenCode.Click += (s, e) => LaunchWorkspace(item, AgentType.OpenCode);

            MenuItem miClaude = new MenuItem { Header = "启动 Claude Code", FontWeight = (currentAgent == AgentType.ClaudeCode ? FontWeights.Bold : FontWeights.Normal) };
            miClaude.Click += (s, e) => LaunchWorkspace(item, AgentType.ClaudeCode);

            MenuItem miCodex = new MenuItem { Header = "启动 OpenAI Codex", FontWeight = (currentAgent == AgentType.Codex ? FontWeights.Bold : FontWeights.Normal) };
            miCodex.Click += (s, e) => LaunchWorkspace(item, AgentType.Codex);

            MenuItem miVSCode = new MenuItem { Header = "在 VS Code 中打开", FontWeight = (currentAgent == AgentType.VSCode ? FontWeights.Bold : FontWeights.Normal) };
            miVSCode.Click += (s, e) => LaunchWorkspace(item, AgentType.VSCode);

            MenuItem miExplorer = new MenuItem { Header = "在资源管理器中打开" };
            miExplorer.Click += (s, e) => LaunchWorkspace(item, AgentType.Explorer);

            MenuItem miCopy = new MenuItem { Header = "复制工作空间路径" };
            miCopy.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(item.Path);
                    if (footerLeft != null)
                    {
                        footerLeft.Text = "✓ 已复制工作空间路径: " + item.Path;
                        footerLeft.Foreground = new SolidColorBrush(Color.FromRgb(47, 132, 103));
                        copyFeedbackTimer.Stop();
                        copyFeedbackTimer.Start();
                    }
                }
                catch { }
            };

            menu.Items.Add(miOpenCode);
            menu.Items.Add(miClaude);
            menu.Items.Add(miCodex);
            menu.Items.Add(miVSCode);
            menu.Items.Add(new Separator());
            menu.Items.Add(miExplorer);
            menu.Items.Add(miCopy);

            return menu;
        }

        private void LaunchWorkspace(WorkspaceItem item, AgentType type)
        {
            if (item == null) return;
            HidePalette();
            ProcessLauncher.Launch(item, type);
        }

        private void RefreshWorkspacesAsync(bool forceRender = false)
        {
            if (isRefreshing) return;
            isRefreshing = true;
            ThreadPool.QueueUserWorkItem((state) =>
            {
                try
                {
                    List<WorkspaceItem> list = WorkspaceScanner.ScanDirectories();
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        this.allItems = list;
                        this.hasLoadedWorkspaces = true;
                        this.lastScanTime = DateTime.Now;
                        this.isRefreshing = false;
                        if (forceRender || this.Visibility == Visibility.Visible)
                        {
                            ExecuteFilter(searchBox != null ? searchBox.Text : "");
                        }
                        MemoryOptimizer.TrimMemory();
                    }));
                }
                catch
                {
                    this.Dispatcher.BeginInvoke(new Action(() => { this.isRefreshing = false; }));
                }
            });
        }
    }
}
