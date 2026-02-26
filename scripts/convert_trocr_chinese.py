"""
TrOCR 中文手寫模型轉換腳本
=====================================
功能:
  1. 下載指定的中文 TrOCR HuggingFace 模型
  2. 轉換成 ONNX 格式 (encoder + decoder)
  3. 複製到 publish 目錄讓 ZIP 打包時一起帶走

用法:
  pip install transformers optimum[onnxruntime] torch onnx
  python scripts/convert_trocr_chinese.py

如果要換模型, 修改下方 MODEL_ID 即可
"""

import subprocess
import sys
import shutil
import os
import json
from pathlib import Path

# ─── 設定 ────────────────────────────────────────────────────────────────────
# 可替換成任意 HuggingFace 上的 TrOCR 相容模型
# 選項 1 (預設): breezedeus/cnocr-v2.3-densenet-lite-136-gru (繁簡中文, 輕量)
# 選項 2: chineseocr/trocr-chinese (簡體手寫, CER=0.011, 需 Baidu Pan 手動下載)
# 選項 3: Xenova/trocr-base-handwritten (英文, 可直接下載 ONNX, 作為 fallback)
MODEL_ID = "Xenova/trocr-base-handwritten"   # ← 改這裡

# 腳本放在 FloatingOCRWidget/scripts/, 所以 root = 上一層
ROOT_DIR     = Path(__file__).resolve().parent.parent
ONNX_TEMP    = ROOT_DIR / "trocr_onnx_temp"
PUBLISH_DIR  = ROOT_DIR / "publish" / "paddleocr-withdata" / "trocr_models"
APPDATA_DIR  = Path(os.environ.get("APPDATA", "")) / "FloatingOCRWidget" / "TrOCR"

REQUIRED_FILES = ["encoder_model.onnx", "decoder_model.onnx", "tokenizer.json"]

# ─── 工具函式 ─────────────────────────────────────────────────────────────────
def run(cmd, **kwargs):
    print(f"\n>>> {' '.join(str(c) for c in cmd)}\n")
    subprocess.run([str(c) for c in cmd], check=True, **kwargs)

def copy_models(src_dir: Path, dst_dir: Path):
    dst_dir.mkdir(parents=True, exist_ok=True)
    for f in REQUIRED_FILES:
        candidates = [src_dir / f, src_dir / "onnx" / f]
        for src in candidates:
            if src.exists():
                shutil.copy2(src, dst_dir / f)
                size_mb = src.stat().st_size / 1024 / 1024
                print(f"  ✓ {f}  ({size_mb:.1f} MB)  →  {dst_dir / f}")
                break
        else:
            # quantized fallback (encoder_model.onnx might be encoder_model_quantized.onnx)
            qname = f.replace(".onnx", "_quantized.onnx")
            q = src_dir / qname
            if not q.exists():
                q = src_dir / "onnx" / qname
            if q.exists():
                shutil.copy2(q, dst_dir / f)
                size_mb = q.stat().st_size / 1024 / 1024
                print(f"  ✓ {f} (quantized)  ({size_mb:.1f} MB)")
            else:
                print(f"  ✗ {f} 未找到 (請手動放入: {dst_dir})")

# ─── 主流程 ───────────────────────────────────────────────────────────────────
def main():
    print("=" * 60)
    print("TrOCR 中文 ONNX 轉換腳本")
    print(f"模型: {MODEL_ID}")
    print("=" * 60)

    # Step 1: 安裝依賴
    print("\n[1/4] 安裝 Python 依賴...")
    run([sys.executable, "-m", "pip", "install",
         "transformers>=4.35", "optimum[onnxruntime]>=1.14", "torch", "onnx",
         "--quiet"])

    # Step 2: 若模型 repo 已有 ONNX (如 Xenova), 直接下載不需轉換
    print(f"\n[2/4] 嘗試直接下載 ONNX 模型...")
    try:
        from huggingface_hub import snapshot_download, list_repo_files
        files = list(list_repo_files(MODEL_ID))
        has_onnx = any(f.endswith(".onnx") for f in files)
        if has_onnx:
            print(f"  {MODEL_ID} 已有 ONNX 檔案，直接下載...")
            local = Path(snapshot_download(MODEL_ID))
            copy_models(local, ONNX_TEMP)
        else:
            raise ValueError("no prebuilt ONNX")
    except Exception as e:
        print(f"  直接下載 ONNX 失敗 ({e})，改用 optimum 轉換...")
        ONNX_TEMP.mkdir(exist_ok=True)
        # optimum CLI export
        run([sys.executable, "-m", "optimum.exporters.onnx",
             "--model", MODEL_ID,
             "--task",  "vision2seq-lm",
             str(ONNX_TEMP)])

    # Step 3: 複製到 publish 目錄 (ZIP 打包用)
    print(f"\n[3/4] 複製模型到 publish 目錄...")
    copy_models(ONNX_TEMP, PUBLISH_DIR)

    # Step 4: 同時複製到 AppData (讓當前安裝也能用)
    print(f"\n[4/4] 複製模型到 AppData (本機即時生效)...")
    copy_models(ONNX_TEMP, APPDATA_DIR)

    print("\n" + "=" * 60)
    print("✓ 轉換完成！")
    print(f"  ZIP 內路徑 : {PUBLISH_DIR}")
    print(f"  本機路徑   : {APPDATA_DIR}")
    print()
    print("接下來執行 scripts/repackage.ps1 重新打包 ZIP 並上傳 Release")
    print("=" * 60)

if __name__ == "__main__":
    main()
