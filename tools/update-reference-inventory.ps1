[CmdletBinding()]
param(
    [string] $ReferenceRoot = (Join-Path $PSScriptRoot "..\..\oxce-bai"),
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\docs\compatibility\reference-inventory.json")
)

$ErrorActionPreference = "Stop"
$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$source = Join-Path $reference "src"
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw "Reference checkout does not contain src/: $reference"
}

function Convert-ToReferencePath([string] $Path) {
    $rootUri = [Uri]::new($reference.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar)
    $pathUri = [Uri]::new($Path)
    [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function Get-RelativeFiles([string] $Directory, [string] $Filter) {
    Get-ChildItem -LiteralPath $Directory -Filter $Filter -File |
        ForEach-Object { Convert-ToReferencePath $_.FullName } |
        Sort-Object
}

$modSource = Join-Path $source "Mod\Mod.cpp"
$ruleKeys = [regex]::Matches(
    [IO.File]::ReadAllText($modSource),
    '\["(?<key>[A-Za-z][A-Za-z0-9_]*)"\]') |
    ForEach-Object { $_.Groups["key"].Value } |
    Sort-Object -Unique

$scriptBindingFiles = Get-ChildItem -LiteralPath $source -Recurse -File -Include *.cpp,*.h |
    Select-String -Pattern "ScriptRegister" -List |
    ForEach-Object { Convert-ToReferencePath $_.Path } |
    Sort-Object -Unique

$gameplayAreas = [ordered]@{}
foreach ($area in @("Basescape", "Battlescape", "Geoscape", "Menu")) {
    $areaPath = Join-Path $source $area
    $gameplayAreas[$area] = (Get-ChildItem -LiteralPath $areaPath -File -Include *.cpp,*.h).Count
}

$headOutput = & git -c "safe.directory=$reference" -C $reference rev-parse HEAD
if ($LASTEXITCODE -ne 0) {
    throw "Could not read the reference Git commit."
}

$head = $headOutput.Trim()
$status = @(& git -c "safe.directory=$reference" -C $reference status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw "Could not read the reference Git status."
}
$inventory = [ordered]@{
    schemaVersion = 1
    reference = [ordered]@{
        commit = $head
        clean = $status.Count -eq 0
    }
    discovery = [ordered]@{
        ruleNodeCandidates = @($ruleKeys)
        ruleSourceFiles = @(Get-RelativeFiles (Join-Path $source "Mod") "Rule*.cpp")
        saveSourceFiles = @(Get-RelativeFiles (Join-Path $source "Savegame") "*.cpp")
        scriptBindingFiles = @($scriptBindingFiles)
        optionSourceFiles = @("src/Engine/Options.cpp", "src/Engine/Options.h")
        assetSourceFiles = @(
            "src/Engine/CatFile.cpp",
            "src/Engine/FlcPlayer.cpp",
            "src/Engine/Font.cpp",
            "src/Engine/GMCat.cpp",
            "src/Engine/Palette.cpp",
            "src/Engine/Sound.cpp",
            "src/Engine/Surface.cpp",
            "src/Engine/SurfaceSet.cpp",
            "src/Mod/MapDataSet.cpp"
        )
        gameplaySourceFileCounts = $gameplayAreas
    }
}

$json = $inventory | ConvertTo-Json -Depth 8
$json = $json.Replace("`r`n", "`n") + "`n"
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, $json, [Text.UTF8Encoding]::new($false))
Write-Output $destination
