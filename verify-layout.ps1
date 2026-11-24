# Layout verification matrix for PhotoLensWin (Debug-only snapshot self-check).
# 2 window sizes x 2 themes x 4 modes = 16 snapshots; each must report PASS
# (no element overflow, no sibling overlap). Exits non-zero on any failure.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
Set-Location $root

$exe = Join-Path $root "src\PhotoLensWin.App\bin\Debug\net9.0\PhotoLensWin.App.exe"
dotnet build (Join-Path $root "PhotoLensWin.sln") -c Debug --nologo 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Debug build failed" }

# Fixture uses a long folder name so the info panel reaches MaxWidth(300);
# that is the condition under which the right-arrow clamp used to overlap.
$fixture = Join-Path $env:TEMP ("photolens-verify-" + ("p" * 60))
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
if (-not (Get-ChildItem $fixture -Filter *.jpg -ErrorAction SilentlyContinue)) {
    Add-Type -AssemblyName System.Drawing
    $specs = @(
        @("a_landscape.jpg", 800, 500, 40, 130, 180),
        @("b_portrait.jpg", 500, 800, 150, 60, 110),
        @("c_panorama.jpg", 1200, 400, 220, 140, 20)
    )
    foreach ($s in $specs) {
        $bmp = New-Object System.Drawing.Bitmap $s[1], $s[2]
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb($s[3], $s[4], $s[5]))
        $g.Dispose()
        $bmp.Save((Join-Path $fixture $s[0]), [System.Drawing.Imaging.ImageFormat]::Jpeg)
        $bmp.Dispose()
    }
}

$settingsDir = Join-Path $env:APPDATA "PhotoLensWin"
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$settings = Join-Path $settingsDir "settings.json"
$backup = Join-Path $env:TEMP "photolens-verify-settings.bak"
if (Test-Path $settings) { Copy-Item $settings $backup -Force }

$cases = @()
foreach ($w in 760, 1100) {
    $h = if ($w -eq 760) { 540 } else { 760 }
    foreach ($theme in @("Dark", "Light")) {
        foreach ($mode in @("single", "info", "compare", "compareinfo")) {
            $cases += [pscustomobject]@{ W = $w; H = $h; Theme = $theme; Mode = $mode }
        }
    }
}

$failures = @()
try {
    foreach ($c in $cases) {
        $dir = Join-Path $env:TEMP ("photolens-verify-" + $c.W + "-" + $c.Theme + "-" + $c.Mode)
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        Remove-Item (Join-Path $dir "layout-check.txt") -ErrorAction SilentlyContinue

        $obj = [ordered]@{
            SortRule = 'NameAscending'; ShowFilmstrip = $true; LastFolder = $null
            WindowLeft = 40; WindowTop = 40; WindowWidth = $c.W; WindowHeight = $c.H
            Theme = $c.Theme; WheelMode = 'Zoom'; RecentFolders = @()
        }
        ($obj | ConvertTo-Json) | Set-Content -Path $settings -Encoding UTF8

        $env:PHOTOLENS_SNAPSHOT = Join-Path $dir "shot.png"
        $env:PHOTOLENS_SNAPSHOT_FOLDER = $fixture
        $env:PHOTOLENS_SNAPSHOT_SELECT = "1"
        if ($c.Mode -eq "info" -or $c.Mode -eq "compareinfo") { $env:PHOTOLENS_SNAPSHOT_INFO = "1" }
        if ($c.Mode -eq "compare" -or $c.Mode -eq "compareinfo") { $env:PHOTOLENS_SNAPSHOT_COMPARE = "1" }

        Start-Process $exe -Wait

        Remove-Item Env:PHOTOLENS_SNAPSHOT, Env:PHOTOLENS_SNAPSHOT_FOLDER, Env:PHOTOLENS_SNAPSHOT_SELECT, `
            Env:PHOTOLENS_SNAPSHOT_INFO, Env:PHOTOLENS_SNAPSHOT_COMPARE -ErrorAction SilentlyContinue

        $text = Get-Content (Join-Path $dir "layout-check.txt") -Raw -ErrorAction SilentlyContinue
        $ok = $text -and $text.TrimStart().StartsWith("PASS")
        $label = "{0}x{1} {2} {3}" -f $c.W, $c.H, $c.Theme, $c.Mode
        if ($ok) {
            Write-Host ("[PASS] " + $label)
        } else {
            $detail = ($text -replace "`r?`n", " | ")
            Write-Host ("[FAIL] " + $label + " -> " + $detail)
            $failures += ($label + " -> " + $detail)
        }
    }
}
finally {
    Remove-Item Env:PHOTOLENS_SNAPSHOT, Env:PHOTOLENS_SNAPSHOT_FOLDER, Env:PHOTOLENS_SNAPSHOT_SELECT, `
        Env:PHOTOLENS_SNAPSHOT_INFO, Env:PHOTOLENS_SNAPSHOT_COMPARE -ErrorAction SilentlyContinue
    if (Test-Path $backup) { Copy-Item $backup $settings -Force }
}

if ($failures.Count -gt 0) {
    Write-Host ("Layout verification FAILED: " + $failures.Count + " case(s)")
    exit 1
}
Write-Host ("Layout verification PASSED: " + $cases.Count + " cases")
exit 0
