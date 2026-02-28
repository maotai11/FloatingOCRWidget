using System.Collections.Generic;

namespace FloatingOCRWidget.Models
{
    public class AppSettings
    {
        public double WindowOpacity { get; set; } = 0.9;
        public double WindowLeft { get; set; } = -1; // -1 means use default position
        public double WindowTop { get; set; } = -1;
        public bool StartWithWindows { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public int MaxClipboardHistory { get; set; } = 50;
        public string OCREngine { get; set; } = "PaddleOCR"; // OCR 引擎類型
        public bool EnableHandwriting { get; set; } = true; // 啟用手寫識別
        public bool EnableRotationDetection { get; set; } = true; // 啟用旋轉文字檢測
        public string HotKey { get; set; } = "Ctrl+Alt+O"; // 全域快捷鍵
        public bool ShowNotifications { get; set; } = true;
        public bool PreferTraditionalChinese { get; set; } = true; // 繁中模式（智慧轉換）
        public List<string> Categories { get; set; } = new List<string> { "未分類", "工作", "個人" };

        // OCR 相關設定
        public class OCRSettings
        {
            public bool AutoCopyToClipboard { get; set; } = true;
            public bool ShowSelectionOverlay { get; set; } = true;
            public double SelectionLineThickness { get; set; } = 2.0;
            public string SelectionColor { get; set; } = "#FF0000";
        }

        public OCRSettings OCR { get; set; } = new OCRSettings();
    }
}