using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Drawing;
using WinForms = System.Windows.Forms;
using FloatingOCRWidget.Services;
using FloatingOCRWidget.Models;

namespace FloatingOCRWidget
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private ScreenCapture _screenCapture;
        private OCRService _ocrService;
        private ClipboardManager _clipboardManager;
        private SettingsManager _settingsManager;
        private WinForms.NotifyIcon _notifyIcon;
        private bool _isHidden = false;
        private bool _preferTraditional = true;

        // 目前篩選中的標籤；空集合 = 顯示全部
        private readonly HashSet<string> _selectedTags = new HashSet<string>();
        private const string UntaggedKey = "__untagged__";

        public ObservableCollection<ClipboardItem> ClipboardItems { get; set; }
        private ObservableCollection<ClipboardItem> _filteredItems;

        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();

            _settingsManager = new SettingsManager();
            _preferTraditional = _settingsManager.Settings.PreferTraditionalChinese;

            _screenCapture = new ScreenCapture();
            _ocrService = new OCRService();
            _ocrService.PreferTraditionalChinese = _preferTraditional;

            _clipboardManager = new ClipboardManager();
            ClipboardItems = new ObservableCollection<ClipboardItem>();
            _filteredItems = new ObservableCollection<ClipboardItem>();
            _clipboardManager.ClipboardChanged += OnClipboardChanged;

            InitializeSystemTray();

            var history = _clipboardManager.LoadHistory();
            foreach (var item in history) ClipboardItems.Add(item);

            ClipboardHistory.ItemsSource = _filteredItems;
            RefreshTagFilter();
            ApplyTagFilter();

            DataContext = this;

            var settings = _settingsManager.Settings;
            if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
            {
                this.Left = settings.WindowLeft;
                this.Top  = settings.WindowTop;
            }
            else
            {
                this.Left = SystemParameters.PrimaryScreenWidth - this.Width - 20;
                this.Top  = 20;
            }

            UpdateTraditionalButton();
        }

        // ── 繁中模式 ──────────────────────────────────────────────────────────

        private void TraditionalButton_Click(object sender, RoutedEventArgs e)
        {
            _preferTraditional = !_preferTraditional;
            _ocrService.PreferTraditionalChinese = _preferTraditional;
            _settingsManager.Settings.PreferTraditionalChinese = _preferTraditional;
            _settingsManager.SaveSettings();
            UpdateTraditionalButton();
        }

        private void UpdateTraditionalButton()
        {
            if (_preferTraditional)
            {
                TraditionalButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 130, 206));
                TraditionalButton.Foreground = System.Windows.Media.Brushes.White;
                TraditionalButton.ToolTip    = "繁中模式：開啟（點擊關閉）";
            }
            else
            {
                TraditionalButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 224));
                TraditionalButton.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104));
                TraditionalButton.ToolTip    = "繁中模式：關閉（點擊開啟）";
            }
        }

        // ── 標籤篩選 chip ──────────────────────────────────────────────────────

        private void RefreshTagFilter()
        {
            TagFilterPanel.Children.Clear();

            // [全部] chip
            bool allSelected = _selectedTags.Count == 0;
            TagFilterPanel.Children.Add(MakeFilterChip("全部", allSelected, () =>
            {
                _selectedTags.Clear();
                RefreshTagFilter();
                ApplyTagFilter();
            }));

            // 每個已知標籤
            foreach (var tag in _settingsManager.Settings.KnownTags)
            {
                var t = tag;
                bool sel = _selectedTags.Contains(t);
                TagFilterPanel.Children.Add(MakeFilterChip(t, sel, () => ToggleTagFilter(t)));
            }

            // [未分類] chip（篩出沒有任何標籤的項目）
            bool untaggedSel = _selectedTags.Contains(UntaggedKey);
            TagFilterPanel.Children.Add(MakeFilterChip("未分類", untaggedSel, () => ToggleTagFilter(UntaggedKey)));
        }

        private void ToggleTagFilter(string tag)
        {
            if (_selectedTags.Contains(tag))
                _selectedTags.Remove(tag);
            else
                _selectedTags.Add(tag);

            RefreshTagFilter();
            ApplyTagFilter();
        }

        private Button MakeFilterChip(string label, bool selected, Action onClick)
        {
            var btn = new Button
            {
                Content    = label,
                Style      = (Style)FindResource("TagChipStyle"),
                Background = selected
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 130, 206))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)),
                Foreground = selected
                    ? System.Windows.Media.Brushes.White
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104)),
            };
            btn.Click += (_, __) => onClick();
            return btn;
        }

        private void ApplyTagFilter()
        {
            _filteredItems.Clear();

            if (_selectedTags.Count == 0)
            {
                foreach (var item in ClipboardItems)
                    _filteredItems.Add(item);
                return;
            }

            foreach (var item in ClipboardItems)
            {
                bool include =
                    (_selectedTags.Contains(UntaggedKey) && item.Tags.Count == 0) ||
                    item.Tags.Any(t => _selectedTags.Contains(t));

                if (include) _filteredItems.Add(item);
            }
        }

        // ── 剪貼簿事件 ────────────────────────────────────────────────────────

        private void OnClipboardChanged(object sender, ClipboardItem newItem)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var existing = ClipboardItems.FirstOrDefault(x => x.FullText == newItem.FullText);
                if (existing != null)
                {
                    ClipboardItems.Remove(existing);
                    ClipboardItems.Insert(0, existing);
                }
                else
                {
                    ClipboardItems.Insert(0, newItem);
                    while (ClipboardItems.Count > 50)
                        ClipboardItems.RemoveAt(ClipboardItems.Count - 1);
                }

                ApplyTagFilter();
                _clipboardManager.SaveHistory(ClipboardItems.ToList());
            });
        }

        // ── 滑鼠事件 ──────────────────────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
                _settingsManager.UpdateWindowPosition(this.Left, this.Top);
            }
        }

        private void ClipboardHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClipboardHistory.SelectedItem is ClipboardItem item)
            {
                _clipboardManager.SetClipboard(item.FullText);
                // 不跳通知
                ClipboardHistory.SelectedItem = null;
            }
        }

        private void ClipboardHistory_RightClick(object sender, MouseButtonEventArgs e)
        {
            var item = (ClipboardHistory.SelectedItem ?? GetItemAtMouse()) as ClipboardItem;
            if (item == null) return;

            var menu = new ContextMenu();

            // 複製（無通知）
            menu.Items.Add(new MenuItem
            {
                Header  = "複製",
                Command = new RelayCommand(() => _clipboardManager.SetClipboard(item.FullText))
            });

            // 管理標籤
            menu.Items.Add(new MenuItem
            {
                Header  = "管理標籤...",
                Command = new RelayCommand(() => ShowTagEditor(item))
            });

            menu.Items.Add(new Separator());

            // 刪除此項
            menu.Items.Add(new MenuItem
            {
                Header  = "刪除此項",
                Command = new RelayCommand(() =>
                {
                    ClipboardItems.Remove(item);
                    ApplyTagFilter();
                    _clipboardManager.SaveHistory(ClipboardItems.ToList());
                })
            });

            menu.PlacementTarget = ClipboardHistory;
            menu.Placement       = PlacementMode.MousePoint;
            menu.IsOpen          = true;
        }

        private ClipboardItem GetItemAtMouse()
        {
            var pos = Mouse.GetPosition(ClipboardHistory);
            var hit = VisualTreeHelper.HitTest(ClipboardHistory, pos);
            if (hit == null) return null;
            DependencyObject obj = hit.VisualHit;
            while (obj != null && obj != ClipboardHistory)
            {
                if (obj is FrameworkElement fe && fe.DataContext is ClipboardItem ci)
                    return ci;
                obj = VisualTreeHelper.GetParent(obj);
            }
            return null;
        }

        // ── 標籤編輯 dialog ────────────────────────────────────────────────────

        /// <summary>
        /// 右鍵「管理標籤」：針對單一 ClipboardItem 新增/移除標籤。
        /// </summary>
        private void ShowTagEditor(ClipboardItem item)
        {
            var knownTags = _settingsManager.Settings.KnownTags;

            var dlg = new Window
            {
                Title  = "管理標籤",
                Width  = 300,
                Height = 250,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner      = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // 預覽文字
            var preview = new TextBlock
            {
                Text         = item.Preview,
                FontSize     = 10,
                Foreground   = System.Windows.Media.Brushes.Gray,
                Margin       = new Thickness(0, 0, 0, 6),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(preview, 0);
            root.Children.Add(preview);

            // 標籤 chips（可點選 toggle）
            var chipPanel = new WrapPanel { Orientation = Orientation.Horizontal };
            var chipScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = chipPanel,
                Margin  = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(chipScroll, 1);
            root.Children.Add(chipScroll);

            // 目前 item 的標籤（複製一份用來編輯）
            var editingTags = new HashSet<string>(item.Tags);

            void RebuildChips()
            {
                chipPanel.Children.Clear();
                foreach (var tag in knownTags)
                {
                    var t = tag;
                    bool on = editingTags.Contains(t);
                    var chip = new Button
                    {
                        Content    = t,
                        Style      = (Style)FindResource("TagChipStyle"),
                        Background = on
                            ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 130, 206))
                            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(226, 232, 240)),
                        Foreground = on
                            ? System.Windows.Media.Brushes.White
                            : new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104)),
                        Margin = new Thickness(0, 0, 4, 4)
                    };
                    chip.Click += (_, __) =>
                    {
                        if (editingTags.Contains(t)) editingTags.Remove(t);
                        else editingTags.Add(t);
                        RebuildChips();
                    };
                    chipPanel.Children.Add(chip);
                }
            }
            RebuildChips();

            // 新增標籤列
            var addPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var newTagBox = new TextBox { Width = 140, FontSize = 10, Margin = new Thickness(0, 0, 4, 0) };
            var addBtn    = new Button  { Content = "+ 新增標籤", FontSize = 10, Padding = new Thickness(6, 2, 6, 2) };
            addBtn.Click += (_, __) =>
            {
                var newTag = newTagBox.Text.Trim();
                if (string.IsNullOrEmpty(newTag)) return;
                if (!knownTags.Contains(newTag))
                {
                    knownTags.Add(newTag);
                    _settingsManager.SaveSettings();
                    RefreshTagFilter();
                }
                editingTags.Add(newTag);
                newTagBox.Clear();
                RebuildChips();
            };
            // Enter 鍵新增
            newTagBox.KeyDown += (_, ke) => { if (ke.Key == Key.Return) addBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            addPanel.Children.Add(newTagBox);
            addPanel.Children.Add(addBtn);
            Grid.SetRow(addPanel, 2);
            root.Children.Add(addPanel);

            // OK / Cancel
            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok     = new Button { Content = "確定", Width = 60, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 60, IsCancel = true };
            btnRow.Children.Add(ok);
            btnRow.Children.Add(cancel);
            Grid.SetRow(btnRow, 3);
            root.Children.Add(btnRow);

            dlg.Content = root;

            bool confirmed = false;
            ok.Click     += (_, __) => { confirmed = true; dlg.Close(); };
            cancel.Click += (_, __) => dlg.Close();
            dlg.ShowDialog();

            if (!confirmed) return;

            item.Tags = editingTags.ToList();
            ApplyTagFilter();
            _clipboardManager.SaveHistory(ClipboardItems.ToList());
        }

        // ── OCR ───────────────────────────────────────────────────────────────

        private async void OCRButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OCRButton.IsEnabled = false;
                OCRButton.Content   = "選擇...";
                this.WindowState    = WindowState.Minimized;

                var screenshot = await _screenCapture.CaptureSelectedAreaAsync();
                this.WindowState = WindowState.Normal;

                if (screenshot != null)
                {
                    OCRButton.Content = "識別中...";
                    var text = await _ocrService.RecognizeTextAsync(screenshot);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _clipboardManager.SetClipboard(text, ClipboardItemType.OCRResult);
                        Notify("OCR 完成", $"已識別 {text.Length} 字元並複製到剪貼簿");
                    }
                    else
                    {
                        Notify("OCR 警告", "未能識別到文字內容");
                    }
                }
            }
            catch (Exception ex)
            {
                this.WindowState = WindowState.Normal;
                Notify("OCR 錯誤", ex.Message);
            }
            finally
            {
                OCRButton.IsEnabled = true;
                OCRButton.Content   = "OCR";
            }
        }

        // ── 選單 ──────────────────────────────────────────────────────────────

        private void HideButton_Click(object sender, RoutedEventArgs e) => HideWidget();

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            var manageTags = new MenuItem { Header = "管理標籤清單..." };
            manageTags.Click += (_, __) => ManageKnownTags();
            menu.Items.Add(manageTags);

            menu.Items.Add(new MenuItem
            {
                Header  = "清除歷史記錄",
                Command = new RelayCommand(() =>
                {
                    ClipboardItems.Clear();
                    ApplyTagFilter();
                    _clipboardManager.ClearHistory();
                })
            });
            menu.Items.Add(new MenuItem { Header = "關於", Command = new RelayCommand(ShowAbout) });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "結束程式", Command = new RelayCommand(ExitApplication) });
            menu.PlacementTarget = MenuButton;
            menu.Placement       = PlacementMode.Bottom;
            menu.IsOpen          = true;
        }

        /// <summary>
        /// 漢堡選單→管理標籤清單：新增/刪除全域已知標籤。
        /// </summary>
        private void ManageKnownTags()
        {
            var knownTags = _settingsManager.Settings.KnownTags;

            var dlg = new Window
            {
                Title  = "管理標籤清單",
                Width  = 280,
                Height = 260,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Owner       = this,
                ResizeMode  = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });

            // 標籤清單（每項含刪除按鈕）
            var listPanel = new StackPanel();
            var listScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = listPanel,
                Margin  = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(listScroll, 0);
            root.Children.Add(listScroll);

            void RebuildList()
            {
                listPanel.Children.Clear();
                foreach (var tag in knownTags.ToList())
                {
                    var t   = tag;
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
                    var lbl = new TextBlock { Text = t, FontSize = 11, Width = 170, VerticalAlignment = VerticalAlignment.Center };
                    var del = new Button   { Content = "✕", FontSize = 9, Width = 22, Height = 20, Padding = new Thickness(0), Margin = new Thickness(4, 0, 0, 0) };
                    del.Click += (_, __) =>
                    {
                        knownTags.Remove(t);
                        _settingsManager.SaveSettings();
                        RefreshTagFilter();
                        RebuildList();
                    };
                    row.Children.Add(lbl);
                    row.Children.Add(del);
                    listPanel.Children.Add(row);
                }
            }
            RebuildList();

            // 新增標籤列
            var addPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var newTagBox = new TextBox { Width = 150, FontSize = 10, Margin = new Thickness(0, 0, 4, 0) };
            var addBtn    = new Button  { Content = "+ 新增", FontSize = 10, Padding = new Thickness(6, 2, 6, 2) };
            addBtn.Click += (_, __) =>
            {
                var newTag = newTagBox.Text.Trim();
                if (string.IsNullOrEmpty(newTag) || knownTags.Contains(newTag)) return;
                knownTags.Add(newTag);
                _settingsManager.SaveSettings();
                RefreshTagFilter();
                newTagBox.Clear();
                RebuildList();
            };
            newTagBox.KeyDown += (_, ke) => { if (ke.Key == Key.Return) addBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            addPanel.Children.Add(newTagBox);
            addPanel.Children.Add(addBtn);
            Grid.SetRow(addPanel, 1);
            root.Children.Add(addPanel);

            // 關閉
            var closeBtn = new Button { Content = "關閉", Width = 60, HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
            closeBtn.Click += (_, __) => dlg.Close();
            Grid.SetRow(closeBtn, 2);
            root.Children.Add(closeBtn);

            dlg.Content = root;
            dlg.ShowDialog();
        }

        // ── 系統托盤 / 視窗控制 ───────────────────────────────────────────────

        private void InitializeSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon    = SystemIcons.Application,
                Visible = true,
                Text    = "Floating OCR Widget"
            };

            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("顯示", null, (s, e) => ShowWidget());
            contextMenu.Items.Add("隱藏", null, (s, e) => HideWidget());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("結束", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWidget();
        }

        private void HideWidget()
        {
            this.Hide();
            _isHidden = true;
            Notify("OCR Widget", "已隱藏，雙擊托盤圖示恢復");
        }

        private void ShowWidget()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            _isHidden = false;
        }

        private void ShowAbout()
        {
            MessageBox.Show(
                "Floating OCR Widget v2.5.0\n\n" +
                "• 螢幕框選 OCR 識別（PaddleOCR V4）\n" +
                "• 繁中模式：自動偵測並轉換簡→繁\n" +
                "• TrOCR 手寫補充引擎\n" +
                "• 剪貼簿歷史 + 多標籤管理\n" +
                "• 浮動透明視窗 + 系統托盤",
                "關於", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Notify(string title, string message)
        {
            _notifyIcon?.ShowBalloonTip(3000, title, message, WinForms.ToolTipIcon.Info);
        }

        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            _clipboardManager?.Dispose();
            _ocrService?.Dispose();
            Application.Current.Shutdown();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            HideWidget();
        }

        protected virtual void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute    = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add    { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter)    => _execute?.Invoke();
    }
}
