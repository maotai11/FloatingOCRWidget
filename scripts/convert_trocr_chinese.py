"""
TrOCR 繁體中文手寫模型轉換腳本
=====================================
模型來源:
  ZihCiLin/trocr-traditional-chinese-historical-finetune
  - 繁體中文 (Traditional Chinese)
  - 在手寫歷史文稿上 fine-tune
  - 13,172 字符集 (CNS11643)
  - 0.3B 參數

功能:
  1. 下載繁體中文 TrOCR 模型
  2. 轉換成 ONNX 格式 (encoder + decoder)
  3. 複製到 publish 目錄 (ZIP 打包用) 和 AppData (即時生效)

用法:
  pip install transformers optimum[onnxruntime] torch onnx huggingface_hub
  python scripts/convert_trocr_chinese.py

換模型選項 (改 MODEL_ID):
  手寫/古文: ZihCiLin/trocr-traditional-chinese-historical-finetune  (推薦)
  印刷/合成: ZihCiLin/trocr-traditional-chinese-baseline
  英文手寫:  Xenova/trocr-base-handwritten (有現成 ONNX)
"""

import subprocess
import sys
import shutil
import os
import json
from pathlib import Path

# ─── 設定 ─────────────────────────────────────────────────────────────────────
MODEL_ID   = "ZihCiLin/trocr-traditional-chinese-historical-finetune"   # ← 改這裡
ROOT_DIR   = Path(__file__).resolve().parent.parent
ONNX_TEMP  = ROOT_DIR / "trocr_onnx_temp"
PUBLISH_DIR= ROOT_DIR / "publish" / "paddleocr-withdata" / "trocr_models"
APPDATA_DIR= Path(os.environ.get("APPDATA","")) / "FloatingOCRWidget" / "TrOCR"

COPY_FILES = [
    "encoder_model.onnx",
    "decoder_model.onnx",
    "tokenizer.json",
    "tokenizer_config.json",   # C# 需要讀 BOS/EOS token
    "special_tokens_map.json",
    "config.json",
]

# ─── 工具 ─────────────────────────────────────────────────────────────────────
def pip(*pkgs):
    subprocess.run([sys.executable, "-m", "pip", "install", *pkgs, "--quiet"], check=True)

def run(cmd):
    print(f"\n>>> {' '.join(str(c) for c in cmd)}")
    subprocess.run([str(c) for c in cmd], check=True)

def copy_to(src_dir: Path, dst_dir: Path):
    dst_dir.mkdir(parents=True, exist_ok=True)
    ok, miss = [], []
    for f in COPY_FILES:
        # 找原檔 (可能在子目錄 onnx/ 裡)
        candidates = [src_dir / f, src_dir / "onnx" / f]
        # quantized fallback
        if f.endswith(".onnx"):
            q = f.replace(".onnx", "_quantized.onnx")
            candidates += [src_dir / q, src_dir / "onnx" / q]
        for src in candidates:
            if src.exists():
                shutil.copy2(src, dst_dir / f)
                mb = src.stat().st_size / 1024 / 1024
                ok.append(f"  ✓ {f} ({mb:.1f}MB)")
                break
        else:
            if not f.endswith(".onnx"):  # 非必要檔案
                miss.append(f"  - {f} (可選，跳過)")
            else:
                miss.append(f"  ✗ {f} 未找到")
    for m in ok + miss: print(m)

# ─── 主流程 ───────────────────────────────────────────────────────────────────
def main():
    print("=" * 60)
    print("繁體中文 TrOCR → ONNX 轉換腳本")
    print(f"模型: {MODEL_ID}")
    print("=" * 60)

    # ── Step 1: 安裝依賴 ──────────────────────────────────────────────────────
    print("\n[1/4] 安裝 Python 依賴...")
    pip("transformers>=4.35", "optimum[onnxruntime]>=1.14",
        "torch", "onnx", "huggingface_hub", "Pillow")

    from huggingface_hub import list_repo_files

    # ── Step 2: 嘗試直接用現成 ONNX (Xenova 系列有備) ────────────────────────
    print(f"\n[2/4] 檢查 {MODEL_ID} 是否有現成 ONNX...")
    ONNX_TEMP.mkdir(exist_ok=True)
    try:
        all_files = list(list_repo_files(MODEL_ID))
        onnx_files = [f for f in all_files if f.endswith(".onnx")]
        if onnx_files:
            print(f"  發現 {len(onnx_files)} 個 ONNX 檔案，直接下載...")
            from huggingface_hub import snapshot_download
            local = Path(snapshot_download(MODEL_ID))
            copy_to(local, ONNX_TEMP)
        else:
            raise ValueError("no prebuilt ONNX")
    except Exception as e:
        print(f"  無現成 ONNX ({e})，使用 optimum 轉換 (需要時間)...")
        # optimum 轉換: 產生 encoder_model.onnx + decoder_model.onnx
        run([sys.executable, "-m", "optimum.exporters.onnx",
             "--model", MODEL_ID,
             "--task",  "vision2seq-lm",
             "--framework", "pt",
             str(ONNX_TEMP)])
        # 同時把 tokenizer 相關檔案下載下來
        from huggingface_hub import hf_hub_download
        for f in ["tokenizer.json", "tokenizer_config.json",
                  "special_tokens_map.json", "config.json"]:
            try:
                local_f = hf_hub_download(MODEL_ID, f)
                shutil.copy2(local_f, ONNX_TEMP / f)
                print(f"  ✓ {f}")
            except Exception:
                pass

    # ── Step 3: 複製到 publish/ (ZIP 打包用) ─────────────────────────────────
    print(f"\n[3/4] 複製到 publish 目錄 (ZIP 打包用)...")
    copy_to(ONNX_TEMP, PUBLISH_DIR)

    # ── Step 4: 複製到 AppData (本機即時生效) ────────────────────────────────
    print(f"\n[4/4] 複製到 AppData (本機即時生效)...")
    copy_to(ONNX_TEMP, APPDATA_DIR)

    # ── 確認結果 ──────────────────────────────────────────────────────────────
    print("\n" + "=" * 60)
    print("✓ 轉換完成！")
    missing = [f for f in ["encoder_model.onnx","decoder_model.onnx","tokenizer.json"]
               if not (PUBLISH_DIR / f).exists()]
    if missing:
        print(f"  警告: 以下必要檔案缺失: {missing}")
        print("  請手動把 ONNX 檔案放入:")
        print(f"  {PUBLISH_DIR}")
    else:
        print(f"  ZIP 目錄 : {PUBLISH_DIR}")
        print(f"  本機目錄 : {APPDATA_DIR}")
        print()
        print("接下來執行:")
        print("  pwsh scripts/repackage.ps1 -Version 2.3.0 -Tag v2.3.0")
    print("=" * 60)

if __name__ == "__main__":
    main()
