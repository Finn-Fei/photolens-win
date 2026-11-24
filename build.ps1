# PhotoLensWin 一键构建与打包（Windows PowerShell）
# 用法：powershell -ExecutionPolicy Bypass -File build.ps1 [-Version <版本号>] [-Runtime win-x64]

param(
    [string]$Version = "",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# 版本号唯一来源：Directory.Build.props（与 csproj/程序集版本同源）
if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content (Join-Path $PSScriptRoot "Directory.Build.props")
    $Version = [string]$props.Project.PropertyGroup.Version
}

Write-Host "[1/4] 还原与编译..."
dotnet build PhotoLensWin.sln -c Release

Write-Host "[2/4] 单元测试..."
dotnet test PhotoLensWin.sln -c Release --no-build

Write-Host "[3/4] 发布自包含单文件 ($Runtime)..."
$out = Join-Path "dist" $Runtime
# 清理旧产物：否则上一版的 .pdb / 残留依赖会混进本次发布
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
# -p:Version 必须显式传入：否则 zip 名是新版本、程序集 FileVersion 还是 Directory.Build.props 的旧值
# EnableCompressionInSingleFile：单文件产物默认不压缩（实测 exe 约 96MB）
# DebugType=embedded：不再产出独立的 .pdb
dotnet publish src\PhotoLensWin.App -c Release -r $Runtime --self-contained true `
    -p:Version=$Version `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
    -o $out

Write-Host "[4/4] 打包..."
$zip = Join-Path "dist" "PhotoLensWin-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip }
# 双保险：发行目录不应出现调试符号
Get-ChildItem -Path $out -Filter *.pdb -File -Recurse -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

Write-Host "完成：$zip"
