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
    /// Supports two model types automatically:
    ///   - Traditional Chinese: ZihCiLin/trocr-traditional-chinese-historical-finetune
    ///     (13,172 char BERT-style tokenizer, BOS=[CLS]=101, EOS=[SEP]=102)
    ///   - English fallback: Xenova/trocr-base-handwritten
    ///     (GPT-2 BPE tokenizer, BOS/EOS=2)
    ///
    /// Model lookup order:
    ///   1. <exe_dir>/trocr_models/  (bundled in ZIP)
    ///   2. %AppData%/FloatingOCRWidget/TrOCR/  (auto-downloaded)
    ///
    /// To use the Traditional Chinese model, run:
    ///   python scripts/convert_trocr_chinese.py
    ///   pwsh scripts/repackage.ps1 -Version 2.3.0 -Tag v2.3.0
    /// </summary>
    public class TrOCRService : IDisposable
    {
        private InferenceSession _encoderSession;
        private InferenceSession _decoderSession;
        private Dictionary<int, string> _idToToken;
        private long _decoderStartToken = 2;  // set from tokenizer_config.json
        private long _eosTokenId        = 2;
        private bool _isBpeTokenizer    = true; // false = Chinese char-level
        private bool _isInitialized;
        private bool _disposed;

        // ── Model directory resolution ─────────────────────────────────────────
        // Priority 1: bundled next to exe (ZIP users get offline Chinese model)
        // Priority 2: AppData  (auto-download English model on first use)
        private static string ResolveModelDir()
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "trocr_models");
            if (File.Exists(Path.Combine(bundled, "encoder_model.onnx")))
                return bundled;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FloatingOCRWidget", "TrOCR");
        }

        // ── Fallback: English handwriting model (Xenova, pre-exported ONNX) ───
        private const string HF_ONNX_BASE =
            "https://huggingface.co/Xenova/trocr-base-handwritten/resolve/main/onnx/";
        private const string HF_TOKENIZER_URL =
            "https://huggingface.co/Xenova/trocr-base-handwritten/resolve/main/tokenizer.json";

        private const int IMAGE_HEIGHT    = 384;
        private const int IMAGE_WIDTH     = 384;
        private const int MAX_NEW_TOKENS  = 64;

        private static readonly float[] PIXEL_MEAN = { 0.5f, 0.5f, 0.5f };
        private static readonly float[] PIXEL_STD  = { 0.5f, 0.5f, 0.5f };

        public bool IsAvailable => _isInitialized && !_disposed;

        // ── Init ───────────────────────────────────────────────────────────────
        public async Task<bool> TryInitializeAsync(IProgress<string> progress = null)
        {
            try
            {
                var activeDir = ResolveModelDir();
                Directory.CreateDirectory(activeDir);
                await EnsureModelsAsync(progress, activeDir);

                var options = new SessionOptions();
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

                _encoderSession = new InferenceSession(
                    Path.Combine(activeDir, "encoder_model.onnx"), options);
                _decoderSession = new InferenceSession(
                    Path.Combine(activeDir, "decoder_model.onnx"), options);

                _idToToken = LoadVocab(Path.Combine(activeDir, "tokenizer.json"));
                LoadSpecialTokens(activeDir, _idToToken);

                Debug.WriteLine($"TrOCR loaded from: {activeDir}");
                Debug.WriteLine($"TrOCR vocab={_idToToken?.Count}, BOS={_decoderStartToken}, EOS={_eosTokenId}, BPE={_isBpeTokenizer}");
                _isInitialized = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrOCR init skipped: {ex.Message}");
                return false;
            }
        }

        // ── Download fallback English model if no bundled model exists ─────────
        private async Task EnsureModelsAsync(IProgress<string> progress, string targetDir)
        {
            // If bundled models already present, skip download
            if (File.Exists(Path.Combine(targetDir, "encoder_model.onnx")))
                return;

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

            var modelFiles = new[]
            {
                ("encoder_model_quantized.onnx", "encoder_model.onnx"),
                ("decoder_model_quantized.onnx", "decoder_model.onnx"),
            };

            foreach (var (remote, local) in modelFiles)
            {
                var localPath = Path.Combine(targetDir, local);
                if (!File.Exists(localPath))
                {
                    progress?.Report($"TrOCR: 下載英文手寫模型 {local}…");
                    Debug.WriteLine($"TrOCR: downloading {remote}");
                    var data = await http.GetByteArrayAsync(HF_ONNX_BASE + remote);
                    await File.WriteAllBytesAsync(localPath, data);
                    Debug.WriteLine($"TrOCR: saved {local} ({data.Length / 1024 / 1024}MB)");
                }
            }

            var tokPath = Path.Combine(targetDir, "tokenizer.json");
            if (!File.Exists(tokPath))
            {
                progress?.Report("TrOCR: 下載 tokenizer…");
                var data = await http.GetByteArrayAsync(HF_TOKENIZER_URL);
                await File.WriteAllBytesAsync(tokPath, data);
            }
        }

        // ── Load vocab: tokenizer.json → { id: token_string } ─────────────────
        private static Dictionary<int, string> LoadVocab(string path)
        {
            var obj = JObject.Parse(File.ReadAllText(path));

            // Standard HuggingFace tokenizer.json format: model.vocab
            var vocabObj = obj["model"]?["vocab"] as JObject;

            // Some models store vocab at top level
            if (vocabObj == null)
                vocabObj = obj["vocab"] as JObject;

            if (vocabObj == null)
            {
                Debug.WriteLine("TrOCR: vocab not found in tokenizer.json");
                return new Dictionary<int, string>();
            }

            var result = new Dictionary<int, string>();
            foreach (var kv in vocabObj)
                result[(int)(long)kv.Value] = kv.Key;
            return result;
        }

        // ── Detect tokenizer type and load BOS/EOS from tokenizer_config.json ──
        private void LoadSpecialTokens(string dir, Dictionary<int, string> vocab)
        {
            // Build reverse lookup for token string → id
            var tokenToId = vocab.ToDictionary(kv => kv.Value, kv => (long)kv.Key);

            // Detect tokenizer type:
            // Chinese BERT-style: vocab has [CLS], [SEP], [PAD]  → char-level
            // English GPT-2:      vocab has <s>, </s>, Ġ tokens  → BPE
            bool hasCls = tokenToId.ContainsKey("[CLS]");
            bool hasSep = tokenToId.ContainsKey("[SEP]");
            _isBpeTokenizer = !hasCls;

            if (hasCls && hasSep)
            {
                // Chinese BERT-style tokenizer
                _decoderStartToken = tokenToId["[CLS]"];   // typically 101
                _eosTokenId        = tokenToId["[SEP]"];   // typically 102
                Debug.WriteLine($"TrOCR: Chinese BERT tokenizer. [CLS]={_decoderStartToken} [SEP]={_eosTokenId}");
                return;
            }

            // Try tokenizer_config.json for explicit special token values
            var configPath = Path.Combine(dir, "tokenizer_config.json");
            if (!File.Exists(configPath)) return;

            try
            {
                var cfg = JObject.Parse(File.ReadAllText(configPath));

                string bosStr = cfg["bos_token"]?.ToString()
                             ?? cfg["decoder_start_token"]?.ToString()
                             ?? "</s>";
                string eosStr = cfg["eos_token"]?.ToString() ?? "</s>";

                if (tokenToId.TryGetValue(bosStr, out long b)) _decoderStartToken = b;
                if (tokenToId.TryGetValue(eosStr, out long e)) _eosTokenId = e;

                Debug.WriteLine($"TrOCR: tokenizer_config → BOS='{bosStr}'({_decoderStartToken}) EOS='{eosStr}'({_eosTokenId})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrOCR: tokenizer_config read error: {ex.Message}");
            }
        }

        // ── Inference ──────────────────────────────────────────────────────────
        public string RecognizeHandwriting(Bitmap image)
        {
            if (!IsAvailable) return null;

            try
            {
                var pixelValues = PreprocessImage(image);

                // Encoder
                var encInputs = new[] { NamedOnnxValue.CreateFromTensor("pixel_values", pixelValues) };
                using var encOutputs = _encoderSession.Run(encInputs);
                var encoderHidden = encOutputs
                    .First(x => x.Name == "last_hidden_state")
                    .AsTensor<float>()
                    .Clone();

                // Autoregressive greedy decode
                var tokens   = new List<long> { _decoderStartToken };
                var attnMask = new List<long> { 1 };

                for (int step = 0; step < MAX_NEW_TOKENS; step++)
                {
                    var idsTensor  = new DenseTensor<long>(tokens.ToArray(),   new[] { 1, tokens.Count });
                    var maskTensor = new DenseTensor<long>(attnMask.ToArray(), new[] { 1, attnMask.Count });

                    var decInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids",             idsTensor),
                        NamedOnnxValue.CreateFromTensor("attention_mask",        maskTensor),
                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHidden),
                    };

                    using var decOutputs = _decoderSession.Run(decInputs);
                    var logits = decOutputs.First(x => x.Name == "logits").AsTensor<float>();

                    int lastPos   = tokens.Count - 1;
                    int vocabSize = logits.Dimensions[2];
                    long nextToken = 0;
                    float maxVal   = float.MinValue;
                    for (int v = 0; v < vocabSize; v++)
                    {
                        float val = logits[0, lastPos, v];
                        if (val > maxVal) { maxVal = val; nextToken = v; }
                    }

                    if (nextToken == _eosTokenId && step > 0) break;
                    tokens.Add(nextToken);
                    attnMask.Add(1);
                }

                var text = DecodeTokens(tokens.Skip(1).Select(t => (int)t));
                Debug.WriteLine($"TrOCR: \"{text}\"");
                return text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TrOCR inference error: {ex.Message}");
                return null;
            }
        }

        // ── Image preprocessing ────────────────────────────────────────────────
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

        // ── Token decoding ─────────────────────────────────────────────────────
        // Handles both:
        //   BPE (English):  Ġ=space, Ċ=newline, skip ## subword markers
        //   BERT (Chinese): direct char concatenation, skip [special] tokens
        private string DecodeTokens(IEnumerable<int> ids)
        {
            if (_idToToken == null) return string.Empty;

            var sb = new StringBuilder();
            foreach (var id in ids)
            {
                if (!_idToToken.TryGetValue(id, out var tok)) continue;

                if (_isBpeTokenizer)
                {
                    // GPT-2 BPE: Ġ = space prefix
                    sb.Append(tok.Replace("Ġ", " ").Replace("Ċ", "\n"));
                }
                else
                {
                    // BERT char-level: skip [MASK], [UNK], [PAD] etc.
                    // and ##subword markers
                    if (tok.StartsWith("[") && tok.EndsWith("]")) continue;
                    if (tok.StartsWith("##"))
                        sb.Append(tok.Substring(2));
                    else
                        sb.Append(tok);
                }
            }

            return sb.ToString().Trim();
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
