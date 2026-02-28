using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace FloatingOCRWidget
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 單一實例檢查：若已有一個執行中，直接退出
            _mutex = new Mutex(true, "FloatingOCRWidget_SingleInstance", out bool createdNew);
            if (!createdNew)
            {
                _mutex.Dispose();
                MessageBox.Show(
                    "OCR Widget 已在背景執行中。\n\n請至系統托盤（右下角）找到圖示，雙擊可重新顯示視窗。",
                    "已在執行中",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An error occurred: {e.Exception.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}