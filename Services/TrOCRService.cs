using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json.Linq;

namespace FloatingOCRWidget.Services
{
    /// <summary>
    /// TrOCR handwriting recognition engine via ONNX Runtime.
    ///
    /// Default model: Xenova/trocr-base-handwritten (English handwriting, ~172MB quantized)
    ///
    /// For Traditional Chinese handwriting, place your own Chinese TrOCR ONNX files at:
    ///   %AppData%\FloatingOCRWidget\TrOCR\encoder_model.onnx
    ///   %AppData%\FloatingOCRWidget\TrOCR\decoder_model.onnx
    ///   %AppData%\FloatingOCRWidget\TrOCR\tokenizer.json
    ///
    /// Export a Chinese TrOCR model to ONNX using Python:
    ///   pip install optimum
    ///   optimum-cli export onnx --model chineseocr/trocr-chinese ./trocr_onnx/
    /// </summary>
    public class TrOCRService : IDisposable
    {
        private InferenceSession _encoderSession;
        private InferenceSession _decoderSession;
        private Dictionary<int, string> _idToToken;
        private bool _isInitialized;
        private bool _disposed;

        public static readonly string ModelDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FloatingOCRWidget", "TrOCR");

        // Xenova pre-exported quantized ONNX models (~84MB encoder + ~88MB decoder)
        // Quantized = smaller download, slightly less accuracy, still very good for handwriting
        private const string HF_ONNX_BASE =
            "https://huggingface.co/Xenova/trocr-base-handwritten/resolve/main/onnx/";
        private const string HF_TOKENIZER_URL =
            "https://huggingface.co/Xenova/trocr-base-handwritten/resolve/main/tokenizer.json";

        // TrOCR (roberta-based) special token IDs
        // decoder_start_token_id = 2 (</s>), EOS = 2
        private const long DECODER_START_TOKEN = 2;
        private const long EOS_TOKEN_ID = 2;

        private const int IMAGE_HEIGHT = 384;
        private const int IMAGE_WIDTH = 384;
        private const int MAX_NEW_TOKENS = 64;

        // ImageNet-style normalization used by TrOCR feature extractor
        private static readonly float[] PIXEL_MEAN = { 0.5f, 0.5f, 0.5f };
        private static readonly float[] PIXEL_STD  = { 0.5f, 0.5f, 0.5f };

        public bool IsAvailable => _isInitialized && !_disposed;

        /// <summary>
        /// Initialize TrOCR. Downloads quantized ONNX models on first run (~172MB).
        /// Returns false silently if download fails - OCR continues with PaddleOCR only.
        /// </summary>
        public async Task<bool> TryInitializeAsync(IProgress<string> progress = null)
        {
            try
            {
                Directory.CreateDirectory(ModelDir);
                await EnsureModelsAsync(progress);

                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                _encoderSession = new InferenceSession(
                    Path.Combine(ModelDir, "encoder_model.onnx"), options);
                _decoderSession = new InferenceSession(
                    Path.Combine(ModelDir, "decoder_model.onnx"), options);

                _idToToken = LoadTokenizer(Path.Combine(ModelDir, "tokenizer.json"));
                _isInitialized = true;
                Debug.WriteLine($"TrOCR initialized. Vocab size: {_idToToken?.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrOCR init skipped: {ex.Message}");
                return false;
            }
        }

        private async Task EnsureModelsAsync(IProgress<string> progress)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

            var modelFiles = new[]
            {
                ("encoder_model_quantized.onnx", "encoder_model.onnx"),
                ("decoder_model_quantized.onnx", "decoder_model.onnx"),
            };

            foreach (var (remote, local) in modelFiles)
            {
                var localPath = Path.Combine(ModelDir, local);
                if (!File.Exists(localPath))
                {
                    progress?.Report($"TrOCR: 下載 {local} (量化版，較小)…");
                    Debug.WriteLine($"TrOCR: downloading {remote}");
                    var data = await http.GetByteArrayAsync(HF_ONNX_BASE + remote);
                    await File.WriteAllBytesAsync(localPath, data);
                    Debug.WriteLine($"TrOCR: {local} saved ({data.Length / 1024 / 1024}MB)");
                }
            }

            var tokenizerPath = Path.Combine(ModelDir, "tokenizer.json");
            if (!File.Exists(tokenizerPath))
            {
                progress?.Report("TrOCR: 下載 tokenizer…");
                var data = await http.GetByteArrayAsync(HF_TOKENIZER_URL);
                await File.WriteAllBytesAsync(tokenizerPath, data);
            }
        }

        private static Dictionary<int, string> LoadTokenizer(string path)
        {
            var json = File.ReadAllText(path);
            var obj = JObject.Parse(json);
            // tokenizer.json → model.vocab: { "token": id }
            var vocab = obj["model"]?["vocab"] as JObject;
            if (vocab == null)
            {
                Debug.WriteLine("TrOCR: vocab not found in tokenizer.json");
                return new Dictionary<int, string>();
            }
            var result = new Dictionary<int, string>();
            foreach (var kv in vocab)
                result[(int)(long)kv.Value!] = kv.Key;
            return result;
        }

        /// <summary>
        /// Recognize handwritten text in the image using TrOCR.
        /// Returns null if unavailable or inference fails.
        /// </summary>
        public string RecognizeHandwriting(Bitmap image)
        {
            if (!IsAvailable) return null;

            try
            {
                var pixelValues = PreprocessImage(image);

                // ── Encoder ──────────────────────────────────────────────────
                var encInputs = new[]
                {
                    NamedOnnxValue.CreateFromTensor("pixel_values", pixelValues)
                };
                using var encOutputs = _encoderSession!.Run(encInputs);
                // Clone so we can reuse across decoder steps
                var encoderHidden = encOutputs
                    .First(x => x.Name == "last_hidden_state")
                    .AsTensor<float>()
                    .Clone();

                // ── Autoregressive decode ─────────────────────────────────────
                var tokens = new List<long> { DECODER_START_TOKEN };
                var attnMask = new List<long> { 1 };

                for (int step = 0; step < MAX_NEW_TOKENS; step++)
                {
                    var inputIdsTensor = new DenseTensor<long>(
                        tokens.ToArray(), new[] { 1, tokens.Count });
                    var attnMaskTensor = new DenseTensor<long>(
                        attnMask.ToArray(), new[] { 1, attnMask.Count });

                    var decInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids",            inputIdsTensor),
                        NamedOnnxValue.CreateFromTensor("attention_mask",       attnMaskTensor),
                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states",encoderHidden),
                    };

                    using var decOutputs = _decoderSession!.Run(decInputs);
                    var logits = decOutputs
                        .First(x => x.Name == "logits")
                        .AsTensor<float>();

                    // Greedy: argmax at last sequence position
                    int lastPos   = tokens.Count - 1;
                    int vocabSize = logits.Dimensions[2];
                    long nextToken = 0;
                    float maxVal  = float.MinValue;
                    for (int v = 0; v < vocabSize; v++)
                    {
                        float val = logits[0, lastPos, v];
                        if (val > maxVal) { maxVal = val; nextToken = v; }
                    }

                    // Stop on EOS (skip if it's the very first generated token)
                    if (nextToken == EOS_TOKEN_ID && step > 0) break;

                    tokens.Add(nextToken);
                    attnMask.Add(1);
                }

                // Skip the initial DECODER_START_TOKEN
                var text = DecodeTokens(tokens.Skip(1).Select(t => (int)t));
                Debug.WriteLine($"TrOCR result: \"{text}\"");
                return text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrOCR inference error: {ex.Message}");
                return null;
            }
        }

        // Resize to 384×384 and normalize to [-1,1] (CHW float32 tensor)
        private DenseTensor<float> PreprocessImage(Bitmap image)
        {
            using var resized = new Bitmap(image, new Size(IMAGE_WIDTH, IMAGE_HEIGHT));
            var tensor = new DenseTensor<float>(new[] { 1, 3, IMAGE_HEIGHT, IMAGE_WIDTH });

            for (int y = 0; y < IMAGE_HEIGHT; y++)
            for (int x = 0; x < IMAGE_WIDTH; x++)
            {
                var px = resized.GetPixel(x, y);
                tensor[0, 0, y, x] = (px.R / 255f - PIXEL_MEAN[0]) / PIXEL_STD[0];
                tensor[0, 1, y, x] = (px.G / 255f - PIXEL_MEAN[1]) / PIXEL_STD[1];
                tensor[0, 2, y, x] = (px.B / 255f - PIXEL_MEAN[2]) / PIXEL_STD[2];
            }
            return tensor;
        }

        // GPT-2 byte-level BPE: Ġ = space, Ċ = newline
        private string DecodeTokens(IEnumerable<int> ids)
        {
            if (_idToToken == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var id in ids)
                if (_idToToken.TryGetValue(id, out var tok))
                    sb.Append(tok);

            return sb.ToString()
                .Replace("Ġ", " ")
                .Replace("Ċ", "\n")
                .Trim();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _encoderSession?.Dispose();
                _decoderSession?.Dispose();
                _disposed = true;
            }
        }
    }
}
