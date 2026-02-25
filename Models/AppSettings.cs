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
        public string TesseractLanguage { get; set; } = "chi_tra+chi_sim+eng"; // 繁中+簡中+英文
        public string HotKey { get; set; } = "Ctrl+Alt+O"; // 全域快捷鍵
        public bool ShowNotifications { get; set; } = true;

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