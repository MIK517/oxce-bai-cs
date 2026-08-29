[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\artifacts\reference-script-api\script-api.actual.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$referenceSources = @(
    "src/Mod/Armor.cpp",
    "src/Savegame/Tile.cpp",
    "src/Savegame/Soldier.cpp",
    "src/Savegame/SavedGame.cpp",
    "src/Savegame/SavedBattleGame.cpp",
    "src/Savegame/Craft.cpp",
    "src/Savegame/Country.cpp",
    "src/Savegame/BattleUnit.cpp",
    "src/Savegame/BattleItem.cpp",
    "src/Mod/Unit.cpp",
    "src/Mod/RuleUfo.cpp",
    "src/Mod/RuleSoldierBonus.cpp",
    "src/Mod/RuleSoldier.cpp",
    "src/Mod/RuleSkill.cpp",
    "src/Mod/RuleResearch.cpp",
    "src/Mod/RuleItem.cpp",
    "src/Mod/RuleInventory.cpp",
    "src/Mod/RuleCraft.cpp",
    "src/Mod/RuleCountry.cpp",
    "src/Mod/Mod.cpp",
    "src/Savegame/Ufo.cpp",
    "src/Ufopaedia/StatsForNerdsState.cpp",
    "src/Mod/RuleStatBonus.cpp"
)

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = [IO.Path]::GetDirectoryName($outputFullPath)
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$metadataPath = Join-Path $outputDirectory "script-api.metadata.json"
$arguments = @{
    OutputPath = $metadataPath
    ProbeStem = "script_api_catalog_probe"
    ExpectedCommit = $ExpectedCommit
    AllowUnresolvedProviderSymbols = $true
    ExtraReferenceSources = $referenceSources
}
if (-not [string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $arguments.ReferenceRoot = $ReferenceRoot
}

& (Join-Path $PSScriptRoot "capture-script-core-reference.ps1") @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& (Join-Path $PSScriptRoot "convert-script-api-metadata.ps1") -InputPath $metadataPath -OutputPath $outputFullPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
