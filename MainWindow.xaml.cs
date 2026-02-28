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
        private string _selectedCategory = "全部分類";

        // 完整歷史；ClipboardHistory.ItemsSource 會依分類篩選
        public ObservableCollection<ClipboardItem> ClipboardItems { get; set; }
        // 目前篩選後顯示的清單
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

            // 載入歷史記錄
            var history = _clipboardManager.LoadHistory();
            foreach (var item in history) ClipboardItems.Add(item);

            // 初始化分類篩選 ComboBox
            RefreshCategoryFilter();

            ClipboardHistory.ItemsSource = _filteredItems;
            ApplyCategoryFilter();

            DataContext = this;

            // 套用儲存的視窗位置
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

            // 更新繁中按鈕外觀
            UpdateTraditionalButton();
        }

        // ── 繁中模式 ─────────────────────────────────────────────────────────

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
                TraditionalButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(49, 130, 206)); // #3182CE
                TraditionalButton.Foreground = System.Windows.Media.Brushes.White;
                TraditionalButton.ToolTip    = "繁中模式：開啟（點擊關閉）";
            }
            else
            {
                TraditionalButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 224)); // #CBD5E0
                TraditionalButton.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104));   // #4A5568
                TraditionalButton.ToolTip    = "繁中模式：關閉（點擊開啟）";
            }
        }

        // ── 分類篩選 ─────────────────────────────────────────────────────────

        private void RefreshCategoryFilter()
        {
            var categories = new List<string> { "全部分類" };
            categories.AddRange(_settingsManager.Settings.Categories);

            CategoryFilter.ItemsSource   = categories;
            CategoryFilter.SelectedIndex = 0;
        }

        private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedCategory = CategoryFilter.SelectedItem as string ?? "全部分類";
            ApplyCategoryFilter();
        }

        private void ApplyCategoryFilter()
        {
            _filteredItems.Clear();
            var source = _selectedCategory == "全部分類"
                ? ClipboardItems
                : ClipboardItems.Where(x => x.Category == _selectedCategory);

            foreach (var item in source)
                _filteredItems.Add(item);
        }

        // ── 剪貼簿歷史（去重 + 分類） ──────────────────────────────────────

        private void OnClipboardChanged(object sender, ClipboardItem newItem)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 去重：若已存在相同文字，移到最頂；否則新增
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

                ApplyCategoryFilter();
                _clipboardManager.SaveHistory(ClipboardItems.ToList());
            });
        }

        // ── 滑鼠事件 ─────────────────────────────────────────────────────────

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
                Notify("已複製", "歷史記錄已複製到剪貼簿");
                ClipboardHistory.SelectedItem = null;
            }
        }

        private void ClipboardHistory_RightClick(object sender, MouseButtonEventArgs e)
        {
            var item = (ClipboardHistory.SelectedItem ?? GetItemAtMouse()) as ClipboardItem;
            if (item == null) return;

            var menu = new ContextMenu();

            // 複製
            menu.Items.Add(new MenuItem
            {
                Header  = "複製",
                Command = new RelayCommand(() =>
                {
                    _clipboardManager.SetClipboard(item.FullText);
                    Notify("已複製", "已複製到剪貼簿");
                })
            });

            // 設定分類 → 子選單
            var categoryMenu = new MenuItem { Header = "設定分類" };
            foreach (var cat in _settingsManager.Settings.Categories)
            {
                var catName = cat;
                categoryMenu.Items.Add(new MenuItem
                {
                    Header   = catName,
                    FontWeight = item.Category == catName ? FontWeights.Bold : FontWeights.Normal,
                    Command  = new RelayCommand(() =>
                    {
                        item.Category = catName;
                        ApplyCategoryFilter();
                        _clipboardManager.SaveHistory(ClipboardItems.ToList());
                    })
                });
            }
            menu.Items.Add(categoryMenu);

            menu.Items.Add(new Separator());

            // 刪除此項
            menu.Items.Add(new MenuItem
            {
                Header  = "刪除此項",
                Command = new RelayCommand(() =>
                {
                    ClipboardItems.Remove(item);
                    ApplyCategoryFilter();
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

        // ── OCR ──────────────────────────────────────────────────────────────

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

        // ── 選單 ─────────────────────────────────────────────────────────────

        private void HideButton_Click(object sender, RoutedEventArgs e) => HideWidget();

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            // 管理分類
            var manageCat = new MenuItem { Header = "管理分類..." };
            manageCat.Click += (_, __) => ManageCategories();
            menu.Items.Add(manageCat);

            menu.Items.Add(new MenuItem
            {
                Header  = "清除歷史記錄",
                Command = new RelayCommand(() =>
                {
                    ClipboardItems.Clear();
                    ApplyCategoryFilter();
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

        private void ManageCategories()
        {
            var current = string.Join(", ", _settingsManager.Settings.Categories);

            // 輕量 WPF 輸入視窗
            var dlg = new Window
            {
                Title           = "管理分類",
                Width           = 320,
                Height          = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner           = this,
                ResizeMode      = ResizeMode.NoResize,
                WindowStyle     = WindowStyle.ToolWindow
            };

            var sp  = new StackPanel { Margin = new Thickness(10) };
            sp.Children.Add(new TextBlock { Text = "輸入分類名稱（用逗號分隔）：", Margin = new Thickness(0, 0, 0, 6) });

            var tb  = new TextBox { Text = current, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(tb);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok  = new Button { Content = "確定", Width = 60, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 60, IsCancel = true };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;

            bool confirmed = false;
            ok.Click     += (_, __) => { confirmed = true; dlg.Close(); };
            cancel.Click += (_, __) => dlg.Close();
            dlg.ShowDialog();

            if (!confirmed || string.IsNullOrWhiteSpace(tb.Text)) return;

            var cats = tb.Text.Split(',')
                              .Select(c => c.Trim())
                              .Where(c => !string.IsNullOrEmpty(c))
                              .Distinct()
                              .ToList();

            _settingsManager.Settings.Categories = cats;
            _settingsManager.SaveSettings();
            RefreshCategoryFilter();
        }

        // ── 系統托盤 / 視窗控制 ──────────────────────────────────────────────

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
                "Floating OCR Widget v2.4.0\n\n" +
                "• 螢幕框選 OCR 識別（PaddleOCR V4）\n" +
                "• 繁中模式：自動偵測並轉換簡→繁\n" +
                "• TrOCR 手寫補充引擎\n" +
                "• 剪貼簿歷史去重 + 分類管理\n" +
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
