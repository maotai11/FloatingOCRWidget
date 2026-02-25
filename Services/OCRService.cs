using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using OpenCvSharp;

namespace FloatingOCRWidget.Services
{
    public class OCRService : IDisposable
    {
        private PaddleOcrAll _ocrEngine;
        private bool _disposed = false;

        public OCRService()
        {
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            try
            {
                // Use Chinese V3 models that support traditional Chinese, simplified Chinese, and English
                var models = LocalFullModels.ChineseV3;
                _ocrEngine = new PaddleOcrAll(models)
                {
                    AllowRotateDetection = true,    // Enable rotated text detection
                    Enable180Classification = true  // Enable handwriting and 180-degree text support
                };

                System.Diagnostics.Debug.WriteLine("PaddleOCR engine initialized successfully with Chinese V3 models");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PaddleOCR initialization error: {ex.Message}");
                throw new InvalidOperationException(
                    $"無法初始化 PaddleOCR 引擎: {ex.Message}\n請確保 PaddleOCR 模型正確安裝。");
            }
        }

        public async Task<string> RecognizeTextAsync(Bitmap image)
        {
            if (image == null || _disposed) return string.Empty;

            return await Task.Run(() =>
            {
                try
                {
                    // Convert Bitmap to OpenCvSharp Mat
                    using var ms = new MemoryStream();
                    image.Save(ms, ImageFormat.Png);
                    ms.Position = 0;

                    using var mat = Mat.FromStream(ms, ImreadModes.Color);
                    var result = _ocrEngine.Run(mat);

                    // Extract text from PaddleOCR result regions
                    var text = string.Join("\n", result.Regions.Select(r => r.Text));

                    // Maintain existing post-processing behavior
                    text = Regex.Replace(text, @"[ \t]+", " ");
                    text = Regex.Replace(text, @"\n{3,}", "\n\n");

                    System.Diagnostics.Debug.WriteLine($"PaddleOCR recognized {result.Regions.Length} text regions");
                    return text.Trim();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PaddleOCR error: {ex.Message}");
                    return string.Empty;
                }
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _ocrEngine?.Dispose();
                _disposed = true;
            }
        }
    }
}
