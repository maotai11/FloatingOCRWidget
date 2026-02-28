using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace FloatingOCRWidget.Models
{
    public class ClipboardItem : INotifyPropertyChanged
    {
        private List<string> _tags = new List<string>();

        public string FullText { get; set; }
        public string Preview { get; set; }
        public DateTime Timestamp { get; set; }
        public ClipboardItemType Type { get; set; }

        // 舊版相容欄位（只用於讀取舊 JSON，不再寫入）
        public string Category { get; set; }

        public List<string> Tags
        {
            get => _tags;
            set
            {
                _tags = value ?? new List<string>();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Tags)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        // 供 JSON 反序列化用
        public ClipboardItem() { _tags = new List<string>(); }

        public ClipboardItem(string text, ClipboardItemType type = ClipboardItemType.Text)
        {
            FullText = text ?? string.Empty;
            Preview = CreatePreview(FullText);
            Timestamp = DateTime.Now;
            Type = type;
            _tags = new List<string>();
        }

        private string CreatePreview(string text)
        {
            if (string.IsNullOrEmpty(text)) return "[空白]";
            var preview = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            return preview.Length > 30 ? preview.Substring(0, 27) + "..." : preview;
        }

        public override bool Equals(object obj) =>
            obj is ClipboardItem other && FullText == other.FullText && Type == other.Type;

        public override int GetHashCode() =>
            (FullText?.GetHashCode() ?? 0) ^ Type.GetHashCode();
    }

    public enum ClipboardItemType
    {
        Text,
        OCRResult,
        Image,
        File
    }
}
