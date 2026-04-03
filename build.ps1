param (
    [string]$GodotPath = $env:GODOT_PATH,
    [string]$Sts2Path = $env:STS2_PATH
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $GodotPath) {
    # Default to godot command if in PATH
    $GodotPath = (Get-Command 'godot' -ErrorAction SilentlyContinue).Source
    if (-not $GodotPath) {
        throw "Could not find Godot executable. Please provide -GodotPath or set GODOT_PATH environment variable."
    }
}

if (-not $Sts2Path) {
    # 尝试获取 Steam 路径以找到 Slay the Spire 2 目录
    $steamPath = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steamPath) {
        $Sts2Path = "$steamPath\steamapps\common\Slay the Spire 2"
    } else {
        throw "Could not find Steam path in registry. Please provide -Sts2Path or set STS2_PATH environment variable."
    }
}

$stsDllPath = "$Sts2Path\data_sts2_windows_x86_64\sts2.dll"
if (-not (Test-Path $stsDllPath)) {
    throw "Could not find sts2.dll at $stsDllPath"
}

Write-Host "Copying sts2.dll to local directory..."
Copy-Item $stsDllPath -Destination .\sts2.dll -Force

Write-Host "Building Godot solutions..."
$process = Start-Process -FilePath $GodotPath -ArgumentList "--build-solutions", "--quit", "--headless", "--verbose" -Wait -NoNewWindow -PassThru
if ($process.ExitCode -ne 0) {
    throw "Build solutions failed with exit code $($process.ExitCode)"
}

Write-Host "Preparing dist directory..."
if (Test-Path dist) {
    Remove-Item -Recurse -Force dist
}
New-Item -ItemType Directory -Path dist | Out-Null

Write-Host "Copying RouteSuggest.dll..."
$dllPath = ".\.godot\mono\temp\bin\Debug\RouteSuggest.dll"
if (-not (Test-Path $dllPath)) {
    throw "Error: RouteSuggest.dll not found at $dllPath"
}
Copy-Item $dllPath -Destination "dist\RouteSuggest.dll" -Force

Write-Host "Exporting Godot package..."
$process = Start-Process -FilePath $GodotPath -ArgumentList "--export-pack", "`"Windows Desktop`"", "dist/RouteSuggest.pck", "--headless" -Wait -NoNewWindow -PassThru
if ($process.ExitCode -ne 0) {
    throw "Export pack failed with exit code $($process.ExitCode)"
}

Write-Host "Copying RouteSuggest.json..."
Copy-Item RouteSuggest.json -Destination "dist\RouteSuggest.json" -Force

Write-Host "Reading version..."
$config = Get-Content -Raw RouteSuggest.json | ConvertFrom-Json
$VERSION = $config.version

Write-Host "Creating zip archive RouteSuggest-v$VERSION.zip..."
$zipFile = "RouteSuggest-v$VERSION.zip"
if (Test-Path $zipFile) {
    Remove-Item -Force $zipFile
}

# 压缩 dist 文件夹下的内容
Compress-Archive -Path "dist\*" -DestinationPath $zipFile

Write-Host "Mod built and packaged successfully! Zip archive: $zipFile"
