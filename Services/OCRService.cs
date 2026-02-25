using System;
using System.Drawing;

using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Tesseract;

namespace FloatingOCRWidget.Services
{
    public class OCRService : IDisposable
    {
        private TesseractEngine _engine;
        private readonly string _tessdataPath;
        private bool _disposed = false;

        public OCRService()
        {
            _tessdataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (!Directory.Exists(_tessdataPath))
                Directory.CreateDirectory(_tessdataPath);
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            // Try Chinese + English first, fallback to English only
            foreach (var lang in new[] { "chi_tra+chi_sim+eng", "eng" })
            {
                try
                {
                    _engine = new TesseractEngine(_tessdataPath, lang, EngineMode.Default);
                    _engine.SetVariable("preserve_interword_spaces", "1");
                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Tesseract init ({lang}): {ex.Message}");
                    _engine?.Dispose();
                    _engine = null;
                }
            }
            throw new InvalidOperationException(
                "無法初始化 OCR 引擎。\n請確保 tessdata 語言包放在程式目錄下的 tessdata/ 資料夾。");
        }

        public async Task<string> RecognizeTextAsync(Bitmap image)
        {
            if (image == null || _disposed) return string.Empty;

            return await Task.Run(() =>
            {
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        image.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                        ms.Seek(0, SeekOrigin.Begin);

                        using (var pix = Pix.LoadFromMemory(ms.ToArray()))
                        using (var page = _engine.Process(pix))
                        {
                            var text = page.GetText() ?? string.Empty;
                            // Collapse excess whitespace while preserving newlines
                            text = Regex.Replace(text, @"[ \t]+", " ");
                            text = Regex.Replace(text, @"\n{3,}", "\n\n");
                            return text.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OCR error: {ex.Message}");
                    return string.Empty;
                }
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _engine?.Dispose();
                _disposed = true;
            }
        }
    }
}
