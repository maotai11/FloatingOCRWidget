using System;

namespace FloatingOCRWidget.Models
{
    public class ClipboardItem
    {
        public string FullText { get; set; }
        public string Preview { get; set; }
        public DateTime Timestamp { get; set; }
        public ClipboardItemType Type { get; set; }

        public ClipboardItem(string text, ClipboardItemType type = ClipboardItemType.Text)
        {
            FullText = text ?? string.Empty;
            Preview = CreatePreview(FullText);
            Timestamp = DateTime.Now;
            Type = type;
        }

        private string CreatePreview(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "[空白]";

            // 移除換行符並限制長度
            var preview = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");

            // 限制預覽長度為30個字元
            if (preview.Length > 30)
            {
                preview = preview.Substring(0, 27) + "...";
            }

            return preview;
        }

        public override bool Equals(object obj)
        {
            if (obj is ClipboardItem other)
            {
                return FullText == other.FullText && Type == other.Type;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return (FullText?.GetHashCode() ?? 0) ^ Type.GetHashCode();
        }
    }

    public enum ClipboardItemType
    {
        Text,
        OCRResult,
        Image,
        File
    }
}