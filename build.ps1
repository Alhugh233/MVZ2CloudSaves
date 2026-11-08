param(
    [string]$InteropDir = "",
    [string]$DshCoreDir = "",
    [string]$LpeDir = "",
    [string]$GameDir = "",
    [switch]$Clean,
    [switch]$Pack
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($InteropDir)) { $InteropDir = Join-Path $root "interop" }
if ([string]::IsNullOrWhiteSpace($DshCoreDir)) { $DshCoreDir = Join-Path $root "libs\dshcore" }
if ([string]::IsNullOrWhiteSpace($LpeDir)) { $LpeDir = Join-Path $root "libs" }

if ($Clean) {
    Remove-Item -Recurse -Force (Join-Path $root "bin") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $root "obj") -ErrorAction SilentlyContinue
}

$props = @(
    "-p:InteropDir=$InteropDir",
    "-p:DshCoreDir=$DshCoreDir",
    "-p:LpeDir=$LpeDir"
)

dotnet build $root -c Debug @props
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $root "bin\Debug\net6.0\MVZ2CloudSaves.dll"
if ($GameDir -and (Test-Path $GameDir)) {
    $plugins = Join-Path $GameDir "BepInEx\plugins"
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    Copy-Item $dll $plugins -Force
    Write-Host "Deployed to $plugins"
}

if ($Pack) {
    $dist = Join-Path $root "dist"
    New-Item -ItemType Directory -Force -Path $dist | Out-Null
    Copy-Item $dll $dist -Force
    Copy-Item (Join-Path $root "README.zh-CN.md") $dist -Force
    Copy-Item (Join-Path $root "README.md") $dist -Force
    Copy-Item (Join-Path $root "LICENSE") $dist -Force
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $dist -Force
    Copy-Item (Join-Path $root "SOURCE.txt") $dist -Force
    $licenseDest = Join-Path $dist "LICENSES"
    Remove-Item $licenseDest -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $root "LICENSES") $licenseDest -Recurse -Force

    # GPL-3.0 section 6: ship the complete corresponding source with the binary.
    $src = Join-Path $dist "src"
    Remove-Item $src -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $src | Out-Null
    Copy-Item (Join-Path $root "*.cs") $src -Force
    Copy-Item (Join-Path $root "*.csproj") $src -Force
    Copy-Item (Join-Path $root "build.ps1") $src -Force
    Copy-Item (Join-Path $root "README.md") $src -Force
    Copy-Item (Join-Path $root "README.zh-CN.md") $src -Force
    Copy-Item (Join-Path $root "LICENSE") $src -Force
    Copy-Item (Join-Path $root "THIRD-PARTY-NOTICES.md") $src -Force
    Copy-Item (Join-Path $root "SOURCE.txt") $src -Force
    Copy-Item (Join-Path $root "Core") $src -Recurse -Force
    Copy-Item (Join-Path $root "GitHub") $src -Recurse -Force
    Copy-Item (Join-Path $root "Patches") $src -Recurse -Force
    Copy-Item (Join-Path $root "UI") $src -Recurse -Force
    Copy-Item (Join-Path $root "Commands") $src -Recurse -Force
    Copy-Item (Join-Path $root "Resources") $src -Recurse -Force
    Copy-Item (Join-Path $root "LICENSES") (Join-Path $src "LICENSES") -Recurse -Force
    $srcLibs = Join-Path $src "libs"
    New-Item -ItemType Directory -Force -Path $srcLibs | Out-Null
    Copy-Item (Join-Path $root "libs\LanguagePackEnhance.API.dll") $srcLibs -Force

    Write-Host "Packed to $dist (binary + docs + src/)"
}
