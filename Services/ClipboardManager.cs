using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using FloatingOCRWidget.Models;

namespace FloatingOCRWidget.Services
{
    public class ClipboardManager : IDisposable
    {
        private readonly string _historyFilePath;
        private DispatcherTimer _clipboardMonitor;
        private string _lastClipboardText = string.Empty;
        private bool _disposed = false;

        public event EventHandler<ClipboardItem> ClipboardChanged;

        public ClipboardManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FloatingOCRWidget");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _historyFilePath = Path.Combine(appDataPath, "clipboard_history.json");

            _clipboardMonitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _clipboardMonitor.Tick += OnTick;

            try
            {
                if (Clipboard.ContainsText())
                    _lastClipboardText = Clipboard.GetText();
            }
            catch { }

            _clipboardMonitor.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                var current = Clipboard.GetText();
                if (!string.IsNullOrEmpty(current) && current != _lastClipboardText)
                {
                    _lastClipboardText = current;
                    ClipboardChanged?.Invoke(this, new ClipboardItem(current, ClipboardItemType.Text));
                }
            }
            catch { }
        }

        public void SetClipboard(string text, ClipboardItemType type = ClipboardItemType.OCRResult)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                _lastClipboardText = text;
                Clipboard.SetText(text);
                ClipboardChanged?.Invoke(this, new ClipboardItem(text, type));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Set clipboard error: {ex.Message}");
            }
        }

        public List<ClipboardItem> LoadHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    return JsonConvert.DeserializeObject<List<ClipboardItem>>(json) ?? new List<ClipboardItem>();
                }
            }
            catch { }
            return new List<ClipboardItem>();
        }

        public void SaveHistory(List<ClipboardItem> history)
        {
            try
            {
                File.WriteAllText(_historyFilePath,
                    JsonConvert.SerializeObject(history.Take(50).ToList(), Formatting.Indented));
            }
            catch { }
        }

        public void ClearHistory()
        {
            try { if (File.Exists(_historyFilePath)) File.Delete(_historyFilePath); } catch { }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _clipboardMonitor?.Stop();
                _disposed = true;
            }
        }
    }
}
