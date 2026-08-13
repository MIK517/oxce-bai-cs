[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\fixtures\expected\images\indexed-bmp.expected.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$fixture = Join-Path $PSScriptRoot "..\fixtures\public\images\indexed-bmp.hex"
& (Join-Path $PSScriptRoot "capture-indexed-gif-reference.ps1") `
    -ReferenceRoot $ReferenceRoot `
    -FixturePath $fixture `
    -OutputPath $OutputPath `
    -ExpectedCommit $ExpectedCommit
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
