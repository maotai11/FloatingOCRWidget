# repackage.ps1
# 轉換完 TrOCR 模型後執行此腳本:
#   1. dotnet publish 重新建置 (含版本號)
#   2. (可選) 數位簽章 EXE
#   3. 重新打包 ZIP (附 SHA256 檢查碼)
#   4. 上傳到 GitHub Release (需要 gh CLI 登入)
#
# 用法 (在 FloatingOCRWidget 根目錄執行):
#   pwsh scripts/repackage.ps1
#   pwsh scripts/repackage.ps1 -Version "2.5.1" -Tag "v2.5.1"
#
# 數位簽章 (需先取得憑證並安裝到 CurrentUser\My):
#   pwsh scripts/repackage.ps1 -CertThumbprint "AABBCC..."
#
# 取得憑證指紋:
#   Get-ChildItem Cert:\CurrentUser\My | Select-Object Thumbprint, Subject
#
# 免費/低成本程式碼簽章選項:
#   - Azure Trusted Signing   : ~$9.99/月，最快速，搭配 azure-codesigning.json 使用
#   - SignPath.io (免費開源版): https://signpath.io/pricing (需 GitHub 倉庫公開)
#   - Certum Open Source Code Signing: 免費（審核較慢）
#   - 自簽憑證: 無法解除 SmartScreen 警告，不建議

param(
    [string]$Version       = "2.5.1",
    [string]$Tag           = "v2.5.1",
    [string]$CertThumbprint = "",          # 憑證指紋（留空 = 跳過簽章）
    [string]$TimestampUrl  = "http://timestamp.digicert.com"  # 時間戳伺服器
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
$WithdataExe   = "$Publish\FloatingOCRWidget.exe"
$StandaloneExe = "$Standalone\FloatingOCRWidget.exe"
foreach ($exePath in @($WithdataExe, $StandaloneExe)) {
    if (-Not (Test-Path $exePath)) {
        Write-Error "EXE 不存在，publish 可能失敗：$exePath"
        exit 1
    }
    $ts = (Get-Item $exePath).LastWriteTime
    Write-Host "  EXE: $(Split-Path $exePath -Parent | Split-Path -Leaf)\FloatingOCRWidget.exe  時間戳: $ts" -ForegroundColor Green
}

# ── [可選] 數位簽章 ────────────────────────────────────────────────
if (-not [string]::IsNullOrEmpty($CertThumbprint)) {
    Write-Host "`n[2.5/4] 數位簽章 EXE (憑證: $CertThumbprint)..."

    # 自動尋找 signtool.exe
    $signtool = $null
    $candidates = @(
        (Get-Command signtool.exe -ErrorAction SilentlyContinue)?.Source,
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe",
        "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c -ErrorAction SilentlyContinue)) { $signtool = $c; break }
    }

    if (-not $signtool) {
        Write-Warning "找不到 signtool.exe，請安裝 Windows SDK 10 並確認 PATH。"
        Write-Warning "下載: https://developer.microsoft.com/windows/downloads/windows-sdk/"
        Write-Warning "跳過簽章步驟，繼續打包未簽章 EXE..."
    } else {
        Write-Host "  使用 signtool: $signtool"
        foreach ($exePath in @($WithdataExe, $StandaloneExe)) {
            & $signtool sign /sha1 $CertThumbprint /fd SHA256 `
                /tr $TimestampUrl /td SHA256 $exePath
            if ($LASTEXITCODE -ne 0) {
                Write-Error "簽章失敗：$exePath（確認憑證指紋正確且已安裝在 CurrentUser\My）"
                exit $LASTEXITCODE
            }
            Write-Host "  [OK] 已簽章：$(Split-Path $exePath -Leaf)" -ForegroundColor Green
        }
    }
} else {
    Write-Host "`n[2.5/4] 跳過數位簽章（未傳入 -CertThumbprint）"
    Write-Host "        使用者下載後會看到 SmartScreen 警告，屬正常現象" -ForegroundColor DarkYellow
    Write-Host "        簽章說明見 scripts/repackage.ps1 頂端注釋" -ForegroundColor DarkYellow
}

# ── 打包 ZIP ───────────────────────────────────────────────────────
Write-Host "`n[3/4] 打包 ZIP → $ZipName..."
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $Publish -DestinationPath $ZipPath
$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 0)
Write-Host "  ZIP 大小: ${sizeMB}MB" -ForegroundColor Green

# 產生 SHA256 檢查碼（讓下載者可以驗證完整性）
$sha256Zip = (Get-FileHash $ZipPath -Algorithm SHA256).Hash
$sha256Exe = (Get-FileHash $StandaloneExe -Algorithm SHA256).Hash
$checksumContent = @"
SHA256 Checksums for FloatingOCRWidget $Tag
Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC' -AsUTC)

$sha256Zip  $ZipName
$sha256Exe  FloatingOCRWidget.exe
"@
$checksumPath = "$Root\publish\SHA256SUMS.txt"
$checksumContent | Set-Content $checksumPath -Encoding UTF8
Write-Host "  SHA256 (ZIP): $sha256Zip" -ForegroundColor Green
Write-Host "  SHA256 (EXE): $sha256Exe" -ForegroundColor Green

# ── GitHub Release ─────────────────────────────────────────────────
Write-Host "`n[4/4] 建立 GitHub Release $Tag..."
$Notes = @"
TrOCR 中文 ONNX 模型已內建於 ZIP，解壓後離線可用。版本 $Version。

## SHA256 檢查碼（驗證下載完整性）
``````
$sha256Zip  $ZipName
$sha256Exe  FloatingOCRWidget.exe
``````
"@
gh release create $Tag `
    $ZipPath `
    "$Standalone\FloatingOCRWidget.exe" `
    $checksumPath `
    --title "$Tag - TrOCR 繁體中文手寫 (離線)" `
    --notes $Notes
if ($LASTEXITCODE -ne 0) { Write-Error "gh release create 失敗！請確認 gh CLI 已登入且 tag 尚未存在"; exit $LASTEXITCODE }

Write-Host "`n======================================" -ForegroundColor Cyan
Write-Host "✓ 完成！Release: https://github.com/maotai11/FloatingOCRWidget/releases/tag/$Tag" -ForegroundColor Green
Write-Host "======================================`n"
