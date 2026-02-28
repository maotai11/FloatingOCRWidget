using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace FloatingOCRWidget.Services
{
    /// <summary>
    /// 使用 Windows kernel32 LCMapStringEx API 做繁/簡中文轉換。
    /// 不需要額外 NuGet 套件。
    /// </summary>
    public static class ChineseConverter
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int LCMapStringEx(
            string lpLocaleName,
            uint dwMapFlags,
            string lpSrcStr,
            int cchSrc,
            StringBuilder lpDestStr,
            int cchDest,
            IntPtr lpVersionInformation,
            IntPtr lpReserved,
            IntPtr sortHandle);

        private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;
        private const uint LCMAP_SIMPLIFIED_CHINESE  = 0x02000000;

        /// <summary>
        /// 簡體 → 繁體
        /// </summary>
        public static string ToTraditional(string text) => Map(text, LCMAP_TRADITIONAL_CHINESE);

        /// <summary>
        /// 繁體 → 簡體
        /// </summary>
        public static string ToSimplified(string text) => Map(text, LCMAP_SIMPLIFIED_CHINESE);

        private static string Map(string text, uint flag)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // 先取得需要的 buffer 大小
            int size = LCMapStringEx("zh-TW", flag, text, -1, null, 0,
                                     IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (size <= 0) return text;

            var sb = new StringBuilder(size);
            int result = LCMapStringEx("zh-TW", flag, text, -1, sb, size,
                                       IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return result > 0 ? sb.ToString() : text;
        }

        /// <summary>
        /// 自動判斷並在「繁中模式」下嘗試將 OCR 結果轉回繁體。
        ///
        /// 邏輯：
        ///   1. 對文字做 s→t 轉換，計算有多少字元實際改變。
        ///   2. 改變比例 &lt; 15% → 大概是繁體文件被 OCR 錯誤輸出成簡體 → 套用轉換。
        ///   3. 改變比例 ≥ 15% → 大概是真正的簡體文件 → 保留原文。
        ///
        /// 若文字完全不含中文字，直接回傳原文。
        /// </summary>
        public static string SmartConvertToTraditional(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            var converted = ToTraditional(text);
            if (converted == text) return text; // 沒有任何改變，直接回傳

            // 統計改變的字元數（只計算中文字元範圍）
            int chineseTotal   = text.Count(c => c >= '\u4E00' && c <= '\u9FFF');
            if (chineseTotal == 0) return text; // 純英文/數字，不需要轉

            int changedCount = 0;
            for (int i = 0; i < Math.Min(text.Length, converted.Length); i++)
            {
                if (text[i] != converted[i] && text[i] >= '\u4E00' && text[i] <= '\u9FFF')
                    changedCount++;
            }

            double changeRatio = (double)changedCount / chineseTotal;
            System.Diagnostics.Debug.WriteLine(
                $"ChineseConverter: {changedCount}/{chineseTotal} chars changed ({changeRatio:P1})");

            // 改變比例低 → 繁體文件被 OCR 輸出成簡體 → 轉繁
            if (changeRatio < 0.15)
                return converted;

            // 改變比例高 → 真正的簡體文件 → 保留原文
            return text;
        }
    }
}
