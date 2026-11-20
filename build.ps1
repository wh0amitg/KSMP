param(
    [string]$GameDir = "C:\Program Files (x86)\Steam\steamapps\common\Kiosk",
    [string]$OutDir  = "",
    [switch]$NoInstall
)

$ErrorActionPreference = "Stop"

$root    = $PSScriptRoot
$srcDir  = Join-Path $root "src\KioskMP"
if ($OutDir -eq "") { $OutDir = Join-Path $root "out" }

$managed = Join-Path $GameDir "Kiosk_Data\Managed"
$bepCore = Join-Path $GameDir "BepInEx\core"

if (-not (Test-Path (Join-Path $managed "mscorlib.dll"))) { throw "Game Managed folder not found: $managed" }
if (-not (Test-Path (Join-Path $bepCore "BepInEx.Core.dll"))) { throw "BepInEx not found: $bepCore" }

$csc = Get-ChildItem "C:\Program Files\dotnet\sdk\*\Roslyn\bincore\csc.dll" -ErrorAction SilentlyContinue |
        Sort-Object { try { [version]($_.Directory.Parent.Name) } catch { [version]"0.0" } } -Descending |
        Select-Object -First 1
if (-not $csc) { throw "csc.dll not found under C:\Program Files\dotnet\sdk" }

$gameRefs = @(
    "mscorlib.dll",
    "System.dll",
    "System.Core.dll",
    "System.Xml.dll",
    "System.Runtime.Serialization.dll",
    "netstandard.dll",
    "Assembly-CSharp.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.SharedInternalsModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.InputLegacyModule.dll",
    "UnityEngine.UIModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.ImageConversionModule.dll",
    "UnityEngine.AnimationModule.dll",
    "UnityEngine.AudioModule.dll",
    "UnityEngine.AIModule.dll",
    "UnityEngine.ParticleSystemModule.dll",
    "UnityEngine.UI.dll",
    "Unity.TextMeshPro.dll",
    "UnityEngine.JSONSerializeModule.dll",
    "Unity.InputSystem.dll",
    "Unity.RenderPipelines.Universal.Runtime.dll",
    "Unity.RenderPipelines.Core.Runtime.dll",
    "com.rlabrecque.steamworks.net.dll"
)

$bepRefs = @(
    "BepInEx.Core.dll",
    "BepInEx.Unity.Mono.dll",
    "BepInEx.Unity.Common.dll",
    "0Harmony.dll"
)

$refArgs = @()
foreach ($r in $gameRefs) {
    $p = Join-Path $managed $r
    if (-not (Test-Path $p)) { Write-Warning "missing game ref: $r" ; continue }
    $refArgs += "-r:`"$p`""
}
foreach ($r in $bepRefs) {
    $p = Join-Path $bepCore $r
    if (-not (Test-Path $p)) { throw "missing BepInEx ref: $r" }
    $refArgs += "-r:`"$p`""
}

$sources = Get-ChildItem -Path $srcDir -Recurse -Filter *.cs | ForEach-Object { "`"$($_.FullName)`"" }

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$outDll = Join-Path $OutDir "KioskMP.dll"

$assetArgs = @()
$assetsDir = Join-Path $root "assets"
$embedList = @(
    "Bob.fbx", "Bob.png",
    "Peoples1.fbx", "Peoples2.fbx", "Peoples3.fbx", "Peoples4.fbx",
    "Peoples1.jpg", "Peoples2.jpg", "Peoples3.jpg", "Peoples4.jpg",
    "AnimP12_Idle.fbx", "AnimP12_Walk.fbx", "AnimP12_Dance.fbx",
    "AnimP34_Idle.fbx", "AnimP34_Walk.fbx", "AnimP34_Dance.fbx",
    "PropWolf.fbx", "PropWolf_Body.png", "PropWolf_Eye.png"
)
foreach ($e in $embedList) {
    $p = Join-Path $assetsDir $e
    if (Test-Path $p) {
        $assetArgs += "-resource:`"$p`",KioskMP.$e"
    } else {
        Write-Warning "asset missing: $e"
    }
}

$args = @(
    "-nologo",
    "-noconfig",
    "-nostdlib+",
    "-target:library",
    "-langversion:9.0",
    "-unsafe+",
    "-optimize+",
    "-define:KIOSKMP",
    "-debug-",
    "-nowarn:1701,1702,0649,0414,0169,0618",
    "-out:`"$outDll`""
) + $refArgs + $assetArgs + $sources

Write-Host "Compiling $($sources.Count) sources -> $outDll"
& dotnet $csc.FullName @args
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }

Write-Host "OK: $outDll"

if (-not $NoInstall) {
    $pluginDir = Join-Path $GameDir "BepInEx\plugins\KioskMP"
    if (-not (Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir | Out-Null }
    Copy-Item -Path $outDll -Destination (Join-Path $pluginDir "KioskMP.dll") -Force
    Write-Host "Installed -> $pluginDir\KioskMP.dll"
}
