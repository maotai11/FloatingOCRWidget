using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FloatingOCRWidget
{
    public partial class App : Application
    {
        private static Mutex _mutex;

        // 啟動 log：寫到 %LocalAppData%\FloatingOCRWidget\startup.log
        internal static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FloatingOCRWidget", "startup.log");

        internal static void WriteLog(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            catch { /* log 失敗不影響主程式 */ }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            WriteLog("=== 啟動 ===");

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

            // 捕捉 WPF Dispatcher 未處理異常（managed）
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 捕捉非 Dispatcher 執行緒未處理異常（Task、Thread 等）
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

            // 捕捉 Task.Run 等背景任務的未觀察例外（防止 .NET 靜默吞掉 Task 崩潰）
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            base.OnStartup(e);
            WriteLog("OnStartup 完成");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            WriteLog($"=== 結束（code={e.ApplicationExitCode}）===");
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            WriteLog($"[FATAL Dispatcher] {e.Exception}");
            MessageBox.Show(
                $"發生未預期的錯誤：{e.Exception.Message}\n\n" +
                $"詳細資訊已記錄至：\n{LogPath}",
                "錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
            // 必須 Shutdown：否則主視窗未建立時 App 會成為殭屍進程（有進程、無視窗、無托盤）
            Shutdown(1);
        }

        private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            WriteLog($"[FATAL AppDomain] isTerminating={e.IsTerminating} — {e.ExceptionObject}");
            // 非 UI 執行緒異常：只能寫 log，CLR 會自行終止進程
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            WriteLog($"[UnobservedTask] {e.Exception}");
            e.SetObserved(); // 標記為已觀察，防止 .NET 6+ 預設的進程終止
        }
    }
}