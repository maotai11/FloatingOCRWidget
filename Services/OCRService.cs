using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private TrOCRService _trOcr;
        private bool _disposed = false;
        private bool _isEngineWarmedUp = false;
        private bool _preferTraditional = true;

        public bool PreferTraditionalChinese
        {
            get => _preferTraditional;
            set => _preferTraditional = value;
        }

        public OCRService()
        {
            InitializeEngine();
            _trOcr = new TrOCRService();
            // 延遲 5s 再 warmup：讓 WPF 視窗先完成初始化，避免啟動時搶佔 CPU
            _ = Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ => WarmUpAsync());
            // 延遲 10s 再初始化 TrOCR：warmup 結束後才開始，避免同時佔用大量記憶體
            _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => _trOcr.TryInitializeAsync());
        }

        private void InitializeEngine()
        {
            try
            {
                // PP-OCRv5：最新一代，繁簡中文精度更高，直接內建於 Sdcb.PaddleOCR.Models.Local 3.0.1
                var models = LocalFullModels.ChineseV5;
                _ocrEngine = new PaddleOcrAll(models)
                {
                    AllowRotateDetection = true,
                    Enable180Classification = true
                };
                System.Diagnostics.Debug.WriteLine("PaddleOCR engine initialized with Chinese V5 (PP-OCRv5) models");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"PaddleOCR initialization error: {ex.Message}");
                throw new InvalidOperationException(
                    $"無法初始化 PaddleOCR 引擎: {ex.Message}\n請確保 PaddleOCR 模型正確安裝。");
            }
        }

        /// <summary>
        /// 啟動後在背景跑一張空白圖，讓 PaddleOCR DLL 和模型完整載入。
        /// 解決「第一次掃描空白/亂碼」問題。
        /// </summary>
        private async Task WarmUpAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    using var warmupBmp = new Bitmap(64, 64);
                    using (var g = Graphics.FromImage(warmupBmp))
                        g.Clear(Color.White);

                    using var ms = new MemoryStream();
                    warmupBmp.Save(ms, ImageFormat.Png);
                    ms.Position = 0;
                    using var mat = Mat.FromStream(ms, ImreadModes.Color);
                    _ocrEngine.Run(mat); // 暖機，結果丟棄
                    _isEngineWarmedUp = true;
                    System.Diagnostics.Debug.WriteLine("PaddleOCR warm-up complete");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"PaddleOCR warm-up error: {ex.Message}");
                    _isEngineWarmedUp = true; // 即使失敗也標記，避免影響後續行為
                }
            });
        }

        /// <summary>
        /// 若圖片太小（任一邊 &lt; 200px），先做 2x bicubic 放大再送 OCR，
        /// 提升低解析度文字的識別率。
        /// </summary>
        private Bitmap UpscaleIfNeeded(Bitmap source)
        {
            if (source.Width >= 200 && source.Height >= 200)
                return source;

            int newW = Math.Max(source.Width * 2, 200);
            int newH = Math.Max(source.Height * 2, 200);
            var result = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(result);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, newW, newH);
            System.Diagnostics.Debug.WriteLine($"Upscaled image: {source.Width}x{source.Height} → {newW}x{newH}");
            return result;
        }

        // 圖片預處理：CLAHE 對比增強 + Otsu 二值化 + 降噪，改善手寫辨識
        private Mat PreprocessForHandwriting(Mat original)
        {
            using var gray = new Mat();
            Cv2.CvtColor(original, gray, ColorConversionCodes.BGR2GRAY);

            using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new OpenCvSharp.Size(8, 8));
            using var enhanced = new Mat();
            clahe.Apply(gray, enhanced);

            using var binary = new Mat();
            Cv2.Threshold(enhanced, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

            using var denoised = new Mat();
            Cv2.MedianBlur(binary, denoised, 3);

            var result = new Mat();
            Cv2.CvtColor(denoised, result, ColorConversionCodes.GRAY2BGR);
            return result;
        }

        public async Task<string> RecognizeTextAsync(Bitmap image)
        {
            if (image == null || _disposed) return string.Empty;

            // 若圖片太小先放大
            bool upscaled = image.Width < 200 || image.Height < 200;
            Bitmap workImage = upscaled ? UpscaleIfNeeded(image) : image;

            try
            {
                return await Task.Run(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        workImage.Save(ms, ImageFormat.Png);
                        ms.Position = 0;

                        using var mat = Mat.FromStream(ms, ImreadModes.Color);

                        // PaddleOCR 雙引擎投票（原圖 + 預處理圖）
                        using var preprocessed = PreprocessForHandwriting(mat);
                        var result1 = _ocrEngine.Run(mat);
                        var result2 = _ocrEngine.Run(preprocessed);

                        var regions = result1.Regions.Length >= result2.Regions.Length
                            ? result1.Regions
                            : result2.Regions;

                        System.Diagnostics.Debug.WriteLine(
                            $"PaddleOCR: original={result1.Regions.Length}, preprocessed={result2.Regions.Length}, using={regions.Length}");

                        var paddleText = string.Join("\n", regions.Select(r => r.Text));

                        // TrOCR fallback：
                        //   - 暖機前閾值放寬（< 20 字元），避免冷啟動空白結果
                        //   - 暖機後維持 < 5 字元（不影響正常流程）
                        int trOcrThreshold = _isEngineWarmedUp ? 5 : 20;
                        string finalText = paddleText;
                        if (_trOcr.IsAvailable && paddleText.Trim().Length < trOcrThreshold)
                        {
                            var trOcrText = _trOcr.RecognizeHandwriting(workImage);
                            if (!string.IsNullOrWhiteSpace(trOcrText) && trOcrText.Length > paddleText.Length)
                            {
                                System.Diagnostics.Debug.WriteLine($"TrOCR supplement used (threshold={trOcrThreshold}): \"{trOcrText}\"");
                                finalText = trOcrText;
                            }
                        }

                        // 後處理：清理空白
                        finalText = Regex.Replace(finalText, @"[ \t]+", " ");
                        finalText = Regex.Replace(finalText, @"\n{3,}", "\n\n");
                        finalText = finalText.Trim();

                        // 繁中模式：智慧轉換
                        if (_preferTraditional && !string.IsNullOrEmpty(finalText))
                            finalText = ChineseConverter.SmartConvertToTraditional(finalText);

                        return finalText;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"PaddleOCR error: {ex.Message}");
                        return string.Empty;
                    }
                });
            }
            finally
            {
                if (upscaled) workImage.Dispose();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _ocrEngine?.Dispose();
                _trOcr?.Dispose();
                _disposed = true;
            }
        }
    }
}
