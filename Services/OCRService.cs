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
                // Use Chinese V4 models (PP-OCRv4) - better handwriting and traditional Chinese support
                var models = LocalFullModels.ChineseV4;
                _ocrEngine = new PaddleOcrAll(models)
                {
                    AllowRotateDetection = true,    // Enable rotated text detection
                    Enable180Classification = true  // Enable handwriting and 180-degree text support
                };

                System.Diagnostics.Debug.WriteLine("PaddleOCR engine initialized successfully with Chinese V4 models");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PaddleOCR initialization error: {ex.Message}");
                throw new InvalidOperationException(
                    $"無法初始化 PaddleOCR 引擎: {ex.Message}\n請確保 PaddleOCR 模型正確安裝。");
            }
        }

        // Preprocess image to improve handwriting recognition accuracy
        // Enhances contrast and reduces noise before OCR
        private Mat PreprocessForHandwriting(Mat original)
        {
            using var gray = new Mat();
            Cv2.CvtColor(original, gray, ColorConversionCodes.BGR2GRAY);

            // CLAHE - adaptive contrast enhancement, works best for handwriting
            using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            // Otsu adaptive binarization
            using var binary = new Mat();
            Cv2.Threshold(enhanced, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            // Light denoising to remove speckles without blurring strokes
            using var denoised = new Mat();
            Cv2.MedianBlur(binary, denoised, 3);

            // Convert back to BGR for PaddleOCR
            var result = new Mat();
            Cv2.CvtColor(denoised, result, ColorConversionCodes.GRAY2BGR);
            return result;
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

                    // Run OCR on both original and preprocessed image, take the richer result
                    using var preprocessed = PreprocessForHandwriting(mat);

                    var result1 = _ocrEngine.Run(mat);
                    var result2 = _ocrEngine.Run(preprocessed);

                    // Pick result with more recognized content (better handwriting coverage)
                    var regions = result1.Regions.Length >= result2.Regions.Length
                        ? result1.Regions
                        : result2.Regions;

                    System.Diagnostics.Debug.WriteLine(
                        $"PaddleOCR: original={result1.Regions.Length} regions, preprocessed={result2.Regions.Length} regions, using={regions.Length}");

                    var text = string.Join("\n", regions.Select(r => r.Text));

                    // Post-processing
                    text = Regex.Replace(text, @"[ \t]+", " ");
                    text = Regex.Replace(text, @"\n{3,}", "\n\n");

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
