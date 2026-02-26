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
    [string]$Version = "2.3.0",
    [string]$Tag     = "v2.3.0"
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

# ── 確認 trocr_models 存在 ─────────────────────────────────────────
$TrOCRModels = "$Publish\trocr_models"
if (-Not (Test-Path "$TrOCRModels\encoder_model.onnx")) {
    Write-Warning "trocr_models\encoder_model.onnx 不存在！"
    Write-Warning "請先執行: python scripts/convert_trocr_chinese.py"
    exit 1
}
Write-Host "[OK] trocr_models 已存在" -ForegroundColor Green

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
dotnet publish -c Release --self-contained -r win-x64 -o $Publish
dotnet publish -c Release --self-contained -r win-x64 -p:PublishSingleFile=true -o $Standalone

# trocr_models 資料夾在 publish 之後可能被清掉，重新確認
if (-Not (Test-Path "$TrOCRModels\encoder_model.onnx")) {
    Write-Host "  trocr_models 被 publish 清除，從 AppData 補回..."
    $AppDataTrOCR = "$env:APPDATA\FloatingOCRWidget\TrOCR"
    New-Item -ItemType Directory -Path $TrOCRModels -Force | Out-Null
    foreach ($f in @("encoder_model.onnx","decoder_model.onnx","tokenizer.json")) {
        $src = "$AppDataTrOCR\$f"
        if (Test-Path $src) { Copy-Item $src "$TrOCRModels\$f" -Force }
    }
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
