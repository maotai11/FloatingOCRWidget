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

### 方法一：直接執行 (推薦)

1. 下載 `publish/withdata/` 整個資料夾
2. 直接雙擊 `FloatingOCRWidget.exe` 執行
3. 無需安裝任何其他軟體

### 方法二：單檔版本

1. 下載 `publish/standalone/FloatingOCRWidget.exe`
2. 直接執行（檔案較大，啟動稍慢）

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
- **OCR 引擎**: Tesseract 5.2.0
- **語言支援**: 繁中/簡中/英文

### 系統需求
- **作業系統**: Windows 10/11 (x64)
- **記憶體**: 建議 4GB 以上
- **磁碟空間**: 約 150MB
- **.NET Runtime**: 不需要（已包含）

### 檔案結構
```
FloatingOCRWidget/
├── FloatingOCRWidget.exe     # 主程式
├── tessdata/                 # OCR 語言模型
│   ├── eng.traineddata      # 英文 (23MB)
│   ├── chi_tra.traineddata  # 繁體中文 (56MB)
│   └── chi_sim.traineddata  # 簡體中文 (42MB)
├── *.dll                     # 相依套件
└── 其他支援檔案...
```

## ⚡ 效能優化

### 已實施的優化
- ✅ **異步處理** - OCR 操作不會凍結 UI
- ✅ **引擎重用** - 避免重複初始化 Tesseract
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
A: 確認是否為 Windows x64 系統，並檢查是否有防毒軟體攔截。

### Q: OCR 識別率不高？
A: 確保截圖區域清晰，文字對比度高，避免手寫或藝術字體。

### Q: 記憶體佔用過高？
A: 清除剪貼簿歷史記錄，或重新啟動程式。

### Q: 無法截圖某些程式？
A: 某些受保護的程式（如銀行軟體）可能阻止螢幕截圖。

## 📄 授權

本專案採用 MIT 授權條款

---

Generated with [Claude Code](https://claude.ai/code)
via [Happy](https://happy.engineering)