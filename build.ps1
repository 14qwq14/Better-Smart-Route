$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$GODOT = "C:\Program Files (x86)\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64.exe"

# 尝试获取 Steam 路径以找到 sts2.dll
$steamPath = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
if (-not $steamPath) {
    Write-Error "Could not find Steam path in registry."
}

$stsDllPath = "$steamPath\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll"
if (-not (Test-Path $stsDllPath)) {
    Write-Error "Could not find sts2.dll at $stsDllPath"
}

Write-Host "Copying sts2.dll to local directory..."
Copy-Item $stsDllPath -Destination .\sts2.dll -Force

Write-Host "Building Godot solutions..."
$process = Start-Process -FilePath $GODOT -ArgumentList "--build-solutions", "--quit", "--headless", "--verbose" -Wait -NoNewWindow -PassThru
if ($process.ExitCode -ne 0) {
    Write-Error "Build solutions failed with exit code $($process.ExitCode)"
}

Write-Host "Preparing dist directory..."
if (Test-Path dist) {
    Remove-Item -Recurse -Force dist
}
New-Item -ItemType Directory -Path dist | Out-Null

Write-Host "Copying RouteSuggest.dll..."
Copy-Item ".\.godot\mono\temp\bin\Debug\RouteSuggest.dll" -Destination "dist\RouteSuggest.dll" -Force

Write-Host "Exporting Godot package..."
$process = Start-Process -FilePath $GODOT -ArgumentList "--export-pack", "`"Windows Desktop`"", "dist/RouteSuggest.pck", "--headless" -Wait -NoNewWindow -PassThru
if ($process.ExitCode -ne 0) {
    Write-Error "Export pack failed with exit code $($process.ExitCode)"
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
