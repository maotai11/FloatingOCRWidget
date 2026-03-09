# repackage.ps1
# 轉換完 TrOCR 模型後執行此腳本:
#   1. dotnet publish 重新建置 (含版本號)
#   2. 重新打包 ZIP
#   3. 上傳到 GitHub Release (需要 gh CLI 登入)
#
# 用法 (在 FloatingOCRWidget 根目錄執行):
#   pwsh scripts/repackage.ps1 -Version "2.3.0" -Tag "v2.3.0"
#   或直接: pwsh scripts/repackage.ps1

param(
    [string]$Version = "2.5.0",
    [string]$Tag     = "v2.5.0"
)

$ErrorActionPreference = "Stop"
$Root      = Split-Path $PSScriptRoot
$Publish   = "$Root\publish\paddleocr-withdata"
$Standalone= "$Root\publish\paddleocr-standalone"
$ZipName   = "FloatingOCRWidget_${Tag}_WithTrOCR.zip"
$ZipPath   = "$Root\publish\$ZipName"

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "FloatingOCRWidget 重新打包腳本" -ForegroundColor Cyan
Write-Host "Version: $Version | Tag: $Tag" -ForegroundColor Cyan
Write-Host "======================================`n"

# ── 確認 TrOCR 模型存在（支援兩種資料夾名稱）─────────────────────
# trocr_onnx_quantized = v2.5+ release 慣例；trocr_models = repackage.ps1 舊慣例
$TrOCRModels = $null
foreach ($candidate in @("trocr_onnx_quantized", "trocr_models")) {
    $p = "$Publish\$candidate"
    if (Test-Path "$p\encoder_model.onnx") { $TrOCRModels = $p; break }
}
if (-Not $TrOCRModels) {
    Write-Warning "找不到 TrOCR 模型！請先執行: python scripts/convert_trocr_chinese.py"
    Write-Warning "模型應放在: $Publish\trocr_onnx_quantized\ 或 $Publish\trocr_models\"
    exit 1
}
Write-Host "[OK] TrOCR 模型已存在: $TrOCRModels" -ForegroundColor Green

# ── 更新版本號 ─────────────────────────────────────────────────────
Write-Host "`n[1/4] 更新版本號 → $Version..."
$csproj = "$Root\FloatingOCRWidget.csproj"
(Get-Content $csproj) `
    -replace '<AssemblyVersion>.*</AssemblyVersion>', "<AssemblyVersion>$Version.0</AssemblyVersion>" `
    -replace '<FileVersion>.*</FileVersion>',         "<FileVersion>$Version.0</FileVersion>" |
    Set-Content $csproj

# ── dotnet publish ─────────────────────────────────────────────────
Write-Host "`n[2/4] dotnet publish (完整版 + 單檔版)..."
Set-Location $Root

# 先備份 TrOCR 模型到 Temp，防止 publish 清除
$TrOCRBackup = "$env:TEMP\trocr_backup_$($Tag -replace '[^a-zA-Z0-9]', '')"
Write-Host "  備份 TrOCR 模型至 $TrOCRBackup..."
if (Test-Path $TrOCRBackup) { Remove-Item $TrOCRBackup -Recurse -Force }
Copy-Item $TrOCRModels $TrOCRBackup -Recurse

$ModelFolderName = Split-Path $TrOCRModels -Leaf

dotnet publish -c Release --self-contained -r win-x64 -o $Publish
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish (完整版) 失敗！"; exit $LASTEXITCODE }

dotnet publish -c Release --self-contained -r win-x64 -p:PublishSingleFile=true -o $Standalone
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish (單檔版) 失敗！"; exit $LASTEXITCODE }

# TrOCR 模型在 publish 之後可能被清掉，從備份補回
$TrOCRDest = "$Publish\$ModelFolderName"
if (-Not (Test-Path "$TrOCRDest\encoder_model.onnx")) {
    Write-Host "  TrOCR 模型被 publish 清除，從備份補回..."
    Copy-Item $TrOCRBackup $TrOCRDest -Recurse -Force
}
Write-Host "  TrOCR 模型確認存在: $TrOCRDest" -ForegroundColor Green
$TrOCRModels = $TrOCRDest

# ── EXE 時間戳驗證（B1：確認 publish 實際更新了 EXE）──────────────
$WithdataExe = "$Publish\FloatingOCRWidget.exe"
$StandaloneExe = "$Standalone\FloatingOCRWidget.exe"
foreach ($exePath in @($WithdataExe, $StandaloneExe)) {
    if (-Not (Test-Path $exePath)) {
        Write-Error "EXE 不存在，publish 可能失敗：$exePath"
        exit 1
    }
    $ts = (Get-Item $exePath).LastWriteTime
    Write-Host "  EXE: $(Split-Path $exePath -Parent | Split-Path -Leaf)\FloatingOCRWidget.exe  時間戳: $ts" -ForegroundColor Green
}

# ── 打包 ZIP ───────────────────────────────────────────────────────
Write-Host "`n[3/4] 打包 ZIP → $ZipName..."
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $Publish -DestinationPath $ZipPath
$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 0)
Write-Host "  ZIP 大小: ${sizeMB}MB" -ForegroundColor Green

# ── GitHub Release ─────────────────────────────────────────────────
Write-Host "`n[4/4] 建立 GitHub Release $Tag..."
$Notes = "TrOCR 中文 ONNX 模型已內建於 ZIP，解壓後離線可用。版本 $Version。"
gh release create $Tag `
    $ZipPath `
    "$Standalone\FloatingOCRWidget.exe" `
    --title "$Tag - TrOCR 繁體中文手寫 (離線)" `
    --notes $Notes

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "✓ 完成！Release: https://github.com/maotai11/FloatingOCRWidget/releases/tag/$Tag" -ForegroundColor Green
Write-Host "======================================`n"
