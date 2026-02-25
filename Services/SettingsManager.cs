using System;
using System.IO;
using Newtonsoft.Json;
using FloatingOCRWidget.Models;

namespace FloatingOCRWidget.Services
{
    public class SettingsManager
    {
        private readonly string _settingsFilePath;
        private AppSettings _currentSettings;

        public SettingsManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FloatingOCRWidget");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _settingsFilePath = Path.Combine(appDataPath, "settings.json");
            LoadSettings();
        }

        public AppSettings Settings => _currentSettings;

        public void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _currentSettings = JsonConvert.DeserializeObject<AppSettings>(json);

                    // 驗證設定值
                    ValidateSettings();
                }
                else
                {
                    // 使用預設設定
                    _currentSettings = new AppSettings();
                    SaveSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load settings error: {ex.Message}");
                _currentSettings = new AppSettings();
            }
        }

        public void SaveSettings()
        {
            try
            {
                ValidateSettings();
                var json = JsonConvert.SerializeObject(_currentSettings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save settings error: {ex.Message}");
            }
        }

        private void ValidateSettings()
        {
            if (_currentSettings == null)
            {
                _currentSettings = new AppSettings();
                return;
            }

            // 驗證透明度範圍
            if (_currentSettings.WindowOpacity < 0.1 || _currentSettings.WindowOpacity > 1.0)
                _currentSettings.WindowOpacity = 0.9;

            // 驗證剪貼簿歷史數量
            if (_currentSettings.MaxClipboardHistory < 10 || _currentSettings.MaxClipboardHistory > 200)
                _currentSettings.MaxClipboardHistory = 50;

            // 驗證 OCR 設定
            if (_currentSettings.OCR == null)
                _currentSettings.OCR = new AppSettings.OCRSettings();

            // 確保 OCR 引擎設定不為空
            if (string.IsNullOrEmpty(_currentSettings.OCREngine))
                _currentSettings.OCREngine = "PaddleOCR";

            // 為舊版本設定提供向下相容性 (遷移 Tesseract 設定)
            if (_currentSettings.OCREngine == "chi_tra+chi_sim+eng" || _currentSettings.OCREngine.Contains("tesseract"))
                _currentSettings.OCREngine = "PaddleOCR";

            // 確保快捷鍵設定不為空
            if (string.IsNullOrEmpty(_currentSettings.HotKey))
                _currentSettings.HotKey = "Ctrl+Alt+O";
        }

        public void UpdateWindowPosition(double left, double top)
        {
            _currentSettings.WindowLeft = left;
            _currentSettings.WindowTop = top;
            SaveSettings();
        }

        public void UpdateWindowOpacity(double opacity)
        {
            _currentSettings.WindowOpacity = Math.Max(0.1, Math.Min(1.0, opacity));
            SaveSettings();
        }

        public void ResetToDefaults()
        {
            _currentSettings = new AppSettings();
            SaveSettings();
        }

        public string GetSettingsFilePath()
        {
            return _settingsFilePath;
        }
    }
}