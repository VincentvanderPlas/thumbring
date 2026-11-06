# Builds the release: dist\Thumbring-win-x64.zip with the app, the Logi plugin and the Chrome extension.
# Needs the .NET 8 and .NET 10 SDKs, and Logi Options+ for PluginApi.dll.
param([string]$Dotnet = 'dotnet')
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$dist = Join-Path $here 'dist'
$out = Join-Path $dist 'Thumbring'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

& $Dotnet publish "$here\app\Thumbring.csproj" -c Release -o $out --nologo -v q
if ($LASTEXITCODE) { throw 'App build failed' }

& $Dotnet build "$here\plugin\src\ThumbringPlugin.csproj" -c Release --nologo -v q
if ($LASTEXITCODE) { throw 'Plugin build failed' }

# A .lplug4 file is a zip with bin, metadata and events at its root.
$stage = Join-Path $dist 'plugin'
Copy-Item "$here\plugin\bin\Release" $stage -Recurse
Get-ChildItem $stage -Recurse -Include *.pdb, *.deps.json | Remove-Item -Force
Compress-Archive "$stage\*" "$dist\plugin.zip"
Move-Item "$dist\plugin.zip" "$out\Thumbring.lplug4"
Remove-Item $stage -Recurse -Force

Copy-Item "$here\extension" "$out\extension" -Recurse
Copy-Item "$here\LICENSE" $out
Compress-Archive "$out\*" "$dist\Thumbring-win-x64.zip"
Get-Item "$dist\Thumbring-win-x64.zip" | Select-Object Name, Length
