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
dotnet publish src\PhotoLensWin.App -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $out

Write-Host "[4/4] 打包..."
$zip = Join-Path "dist" "PhotoLensWin-$Version-$Runtime.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip

Write-Host "完成：$zip"
