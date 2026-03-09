# FloatingOCRWidget 浮動 OCR 桌面小工具

一個輕量級的桌面浮動視窗 OCR 工具，支援螢幕框選文字識別和剪貼簿歷史記錄管理。

## ✨ 主要功能

- 🖱️ **螢幕框選 OCR** - 滑鼠拖拉選擇任意區域進行文字識別
- 🌐 **多語言支援** - 支援繁體中文、簡體中文、英文
- 📋 **剪貼簿管理** - 自動儲存 OCR 結果到剪貼簿，並保留歷史記錄
- 🎯 **浮動視窗** - 透明度可調整的置頂小工具
- 🔄 **拖拉移動** - 滑鼠左鍵拖拉移動視窗位置
- 📱 **系統托盤整合** - 支援隱藏到系統托盤
- 💾 **離線使用** - 完全本地化，無需網路連線

## 🚀 快速開始

### 方法一：ZIP 完整版 (推薦，含 TrOCR 手寫模型)

1. 至 [Releases](../../releases) 頁面下載 `FloatingOCRWidget_vX.X.X_WithTrOCR.zip`
2. **解壓縮到任意資料夾**（例如桌面或 D 槽）
3. 直接雙擊 `FloatingOCRWidget.exe` 執行

> ⚠️ **重要：`trocr_onnx_quantized/` 資料夾不可移動或刪除**，必須和 EXE 放在同一目錄，否則手寫辨識功能失效

### 方法二：單檔版本 (不含 TrOCR 手寫模型)

1. 至 Releases 下載單獨的 `FloatingOCRWidget.exe`
2. 直接執行（僅 PaddleOCR 印刷體辨識，首次啟動較慢）

> **首次啟動提示**：Windows SmartScreen 可能出現「Windows 已保護您的電腦」警告。
> 請點選「**更多資訊**」→「**仍要執行**」即可正常啟動。這是未簽章程式的正常現象。

## 📖 使用方法

1. **啟動程式** - 雙擊 `FloatingOCRWidget.exe`
2. **OCR 識別**：
   - 點擊 `OCR` 按鈕
   - 用滑鼠拖拉選擇要識別的螢幕區域
   - 識別結果會自動複製到剪貼簿
3. **查看歷史** - 在視窗下方查看剪貼簿歷史記錄
4. **調整透明度** - 使用滑桿調整視窗透明度
5. **隱藏/顯示** - 點擊 `Hide` 或託盤圖示

### 🎯 快捷操作

- **拖拉移動** - 滑鼠左鍵拖拉視窗
- **選擇歷史** - 點擊歷史記錄項目重新複製到剪貼簿
- **系統托盤** - 右鍵托盤圖示查看選單
- **關閉程式** - 點擊 `≡` 按鈕 → 結束程式

## 🛠️ 技術規格

### 核心技術
- **Framework**: .NET 8.0 (Windows)
- **UI**: WPF + Windows Forms 混合
- **OCR 引擎**: PaddleOCR PP-OCRv5 (Sdcb.PaddleOCR 3.0.1)
- **手寫補充引擎**: TrOCR (ONNX Runtime，繁體中文 fine-tune)
- **圖像處理**: OpenCvSharp4 4.11.0
- **語言支援**: 繁中/簡中/英文 + 手寫辨識

### 系統需求
- **作業系統**: Windows 10/11 (x64)
- **記憶體**: 建議 4GB 以上
- **磁碟空間**: 完整版 ZIP 解壓後約 750MB；建議至少 2GB 可用空間（首次啟動暫存）
- **.NET Runtime**: 不需要（已包含）

### 檔案結構（ZIP 版解壓後）
```
FloatingOCRWidget_v2.5.0/
├── FloatingOCRWidget.exe             # 主程式（單一執行檔）
└── trocr_onnx_quantized/             # TrOCR 手寫模型（不可移動）
    ├── encoder_model.onnx            # 視覺編碼器 (84MB)
    ├── decoder_model.onnx            # 解碼器 (218MB)
    ├── tokenizer.json                # 詞彙表 (13,176 繁中字元)
    ├── config.json
    ├── tokenizer_config.json
    └── special_tokens_map.json
```

## ⚡ 效能優化

### 已實施的優化
- ✅ **異步處理** - OCR 操作不會凍結 UI
- ✅ **引擎重用** - 避免重複初始化 PaddleOCR
- ✅ **記憶體管理** - 正確的資源釋放機制
- ✅ **自包含部署** - 無需安裝額外 Runtime
- ✅ **本地化處理** - 完全離線運作

### 避免卡頓的設計
- OCR 處理使用背景執行緒
- 適當的 UI 回饋和載入狀態
- 限制剪貼簿歷史數量 (50 筆)
- 高效的圖片記憶體處理

## 🔒 資安考量

### 安全特性
- ✅ **無網路通訊** - 完全離線運作
- ✅ **本地資料** - 所有資料儲存在本機
- ✅ **無遠程連線** - 不會發送任何資料到外部伺服器
- ✅ **最小權限** - 只要求必要的系統權限

### 資料隱私
- **螢幕截圖**: 僅在記憶體中暫存，OCR 完成後立即釋放
- **剪貼簿歷史**: 儲存在本地 JSON 檔案，可隨時刪除
- **設定資料**: 僅包含 UI 偏好設定，無敏感資訊

### 潛在風險與防護
1. **螢幕截圖風險**
   - 風險：可能截取到敏感資訊
   - 防護：截圖僅暫存記憶體，不寫入磁碟

2. **剪貼簿資料**
   - 風險：歷史記錄可能包含敏感文字
   - 防護：可隨時清除歷史記錄

3. **系統權限**
   - 風險：需要螢幕截圖和剪貼簿權限
   - 防護：僅在用戶主動操作時使用

## 🏗️ 開發環境

### 建置需求
- Visual Studio 2022 或 .NET SDK 8.0
- Windows 10/11 開發環境

### 專案結構
```
FloatingOCRWidget/
├── MainWindow.xaml(.cs)      # 主視窗
├── Services/                 # 服務層
│   ├── OCRService.cs        # OCR 處理
│   ├── ScreenCapture.cs     # 螢幕截圖
│   ├── ClipboardManager.cs  # 剪貼簿管理
│   └── SettingsManager.cs   # 設定管理
├── Models/                   # 資料模型
│   ├── ClipboardItem.cs     # 剪貼簿項目
│   └── AppSettings.cs       # 應用設定
└── Resources/               # 資源檔案
```

### 建置指令
```bash
# 開發版本
dotnet build -c Debug

# 發佈版本
dotnet publish -c Release --self-contained -r win-x64 -o publish/withdata

# 單檔版本
dotnet publish -c Release --self-contained -r win-x64 -p:PublishSingleFile=true -o publish/standalone
```

## 🤝 貢獻

歡迎提交 Issue 和 Pull Request！

### 開發者注意事項
- 遵循現有的代碼風格
- 新功能請先開 Issue 討論
- 提交前請確保所有測試通過

## 🆘 常見問題

### Q: 程式無法啟動？
A: 確認是否為 Windows x64 系統，並檢查是否有防毒軟體攔截。若出現 SmartScreen 警告，請點「更多資訊」→「仍要執行」。

### Q: OCR 識別率不高？
A: 確保截圖區域清晰，文字對比度高。PaddleOCR 支援手寫文字，但極草書或藝術字體效果有限。

### Q: 記憶體佔用過高？
A: 清除剪貼簿歷史記錄，或重新啟動程式。

### Q: 無法截圖某些程式？
A: 某些受保護的程式（如銀行軟體）可能阻止螢幕截圖。

## 📄 授權

本專案採用 MIT 授權條款

---

## 📋 更新紀錄

### v2.5.0 (2026-03-09)
**TrOCR 模型路徑修正 + 全面中文化**

**修正：**
- 修正 TrOCR 模型路徑解析邏輯，同時支援 `trocr_onnx_quantized/`（v2.5 release）和 `trocr_models/`（舊版）資料夾名稱
- 修正全域錯誤訊息從英文改為中文
- 修正系統托盤 tooltip 從英文改為中文
- 修正「關於」對話框 OCR 引擎版本顯示（PP-OCRv4 → PP-OCRv5）
- 修正 `repackage.ps1` 缺少 `dotnet publish` 失敗檢查（B7）
- 修正 `repackage.ps1` 預設版本號過舊（2.3.0 → 2.5.0）
- 更新 README：正確的下載指引、SmartScreen 說明、磁碟空間需求

---

### v2.1.0 (2026-02-26)
**手寫辨識強化：ChineseV4 + 圖片預處理**

**改善：**
- 升級 OCR 模型 PP-OCRv3 → PP-OCRv4（ChineseV3 → ChineseV4）
- 新增圖片預處理管線（CLAHE 對比增強 + Otsu 二值化 + 降噪）
- 雙引擎投票：同一張圖同時辨識原圖與預處理圖，取結果較豐富的版本
- 繁體中文手寫辨識穩定性提升

---

### v2.0.0 (2026-02-26)
**重大升級：Tesseract → PaddleOCR**

**新增功能：**
- ✨ 升級 OCR 引擎為 PaddleOCR PP-OCRv3（百度飛槳）
- ✍️ 支援手寫中文（繁體/簡體）文字辨識
- ✍️ 支援手寫英文文字辨識
- 🔄 支援旋轉文字自動偵測（0°/90°/180°/270°）
- 📊 提升混合中英文識別準確率 (~+10%)

**技術變更：**
- 新增 `Sdcb.PaddleOCR 3.0.1` 依賴
- 新增 `OpenCvSharp4 4.11.0` 圖像處理
- 移除 Tesseract 及 tessdata 語言包（原 121MB）
- PaddleOCR 模型內嵌至 DLL（ChineseV3，132MB）
- 設定結構更新：`TesseractLanguage` → `OCREngine`（向下相容）

**部署：**
- 完整版：`publish/paddleocr-withdata/`（736MB）
- 單檔版：`publish/paddleocr-standalone/`（448MB）

---

### v1.0.0 (2026-02-25)
**初始版本**

- 🖱️ 螢幕框選 OCR 識別（基於 Tesseract 5.2.0）
- 📋 剪貼簿歷史記錄管理（最多 50 筆）
- 🎯 浮動透明視窗（透明度可調）
- 📱 系統托盤整合
- 💾 離線自包含 exe 部署（135MB 單檔版）
- 🌐 支援繁體中文、簡體中文、英文（tessdata 語言包）

---

Generated with [Claude Code](https://claude.ai/code)
via [Happy](https://happy.engineering)