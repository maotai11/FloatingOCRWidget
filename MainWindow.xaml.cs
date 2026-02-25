using System;
using System.Linq;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
        private WinForms.NotifyIcon _notifyIcon;
        private bool _isHidden = false;

        public ObservableCollection<ClipboardItem> ClipboardItems { get; set; }
        public event PropertyChangedEventHandler PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            _screenCapture = new ScreenCapture();
            _ocrService = new OCRService();
            _clipboardManager = new ClipboardManager();
            ClipboardItems = new ObservableCollection<ClipboardItem>();
            _clipboardManager.ClipboardChanged += OnClipboardChanged;

            InitializeSystemTray();

            var history = _clipboardManager.LoadHistory();
            foreach (var item in history) ClipboardItems.Add(item);

            ClipboardHistory.ItemsSource = ClipboardItems;
            DataContext = this;

            this.Left = SystemParameters.PrimaryScreenWidth - this.Width - 20;
            this.Top = 20;
        }

        private void InitializeSystemTray()
        {
            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Floating OCR Widget"
            };

            var contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("顯示", null, (s, e) => ShowWidget());
            contextMenu.Items.Add("隱藏", null, (s, e) => HideWidget());
            contextMenu.Items.Add("-");
            contextMenu.Items.Add("結束", null, (s, e) => ExitApplication());
            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWidget();
        }

        private void OnClipboardChanged(object sender, ClipboardItem newItem)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ClipboardItems.Insert(0, newItem);
                while (ClipboardItems.Count > 50)
                    ClipboardItems.RemoveAt(ClipboardItems.Count - 1);
                _clipboardManager.SaveHistory(ClipboardItems.ToList());
            });
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }

        private async void OCRButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OCRButton.IsEnabled = false;
                OCRButton.Content = "選擇...";
                this.WindowState = WindowState.Minimized;

                var screenshot = await _screenCapture.CaptureSelectedAreaAsync();
                this.WindowState = WindowState.Normal;

                if (screenshot != null)
                {
                    OCRButton.Content = "識別中...";
                    var text = await _ocrService.RecognizeTextAsync(screenshot);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        _clipboardManager.SetClipboard(text);
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
                OCRButton.Content = "OCR";
            }
        }

        private void HideButton_Click(object sender, RoutedEventArgs e) => HideWidget();

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

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "清除歷史記錄", Command = new RelayCommand(() => { ClipboardItems.Clear(); _clipboardManager.ClearHistory(); }) });
            menu.Items.Add(new MenuItem { Header = "關於", Command = new RelayCommand(ShowAbout) });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "結束程式", Command = new RelayCommand(ExitApplication) });
            menu.PlacementTarget = MenuButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
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

        private void ShowAbout()
        {
            MessageBox.Show(
                "Floating OCR Widget v1.0\n\n• 螢幕框選 OCR 識別\n• 浮動透明視窗\n• 剪貼簿歷史記錄\n• 系統托盤整合",
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
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute?.Invoke();
    }
}
