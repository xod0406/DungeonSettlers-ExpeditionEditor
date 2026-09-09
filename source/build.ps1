param(
    [Parameter(Mandatory=$true)]
    [string]$GameRoot
)

$ErrorActionPreference = "Stop"
$Project = Join-Path $PSScriptRoot "ExpeditionEditor.csproj"
$OutDir = Join-Path $PSScriptRoot "bin\Release\net6.0"
$PluginDir = Join-Path $GameRoot "BepInEx\plugins"
$InteropDir = Join-Path $GameRoot "BepInEx\interop"

$Required = @(
    (Join-Path $InteropDir "UnityEngine.CoreModule.dll"),
    (Join-Path $InteropDir "UnityEngine.IMGUIModule.dll"),
    (Join-Path $InteropDir "UnityEngine.TextRenderingModule.dll"),
    (Join-Path $InteropDir "Il2Cppmscorlib.dll")
)
foreach ($Path in $Required) {
    if (!(Test-Path $Path)) {
        throw "Required BepInEx interop file not found: $Path`nRun Dungeon Settlers once with BepInEx 6 IL2CPP installed, then try again."
    }
}

Write-Host "Building Dungeon Settlers Expedition Editor v2.1.4 minimal load fix (DS_B.0.4.19)..."
dotnet build $Project -c Release -p:GameRoot="$GameRoot"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE. Plugin was NOT copied."
}

if (!(Test-Path $PluginDir)) {
    New-Item -ItemType Directory -Force -Path $PluginDir | Out-Null
}

$Dll = Join-Path $OutDir "DungeonSettlers.ExpeditionEditor.dll"
if (!(Test-Path $Dll)) {
    throw "Build reported success but output DLL was not found: $Dll"
}

Copy-Item $Dll (Join-Path $PluginDir "DungeonSettlers.ExpeditionEditor.dll") -Force
Write-Host "Installed plugin: $(Join-Path $PluginDir 'DungeonSettlers.ExpeditionEditor.dll')"
Write-Host "Existing BepInEx config is untouched. If no config exists, the plugin will create it on launch."
Write-Host "Press F4 in game to open/close Expedition Editor."
