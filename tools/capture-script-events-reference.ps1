[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\artifacts\reference-script-events\script-events.actual.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$arguments = @{
    OutputPath = $OutputPath
    ProbeStem = "script_events_probe"
    ExpectedCommit = $ExpectedCommit
}
if (-not [string]::IsNullOrWhiteSpace($ReferenceRoot)) {
    $arguments.ReferenceRoot = $ReferenceRoot
}
& (Join-Path $PSScriptRoot "capture-script-core-reference.ps1") @arguments
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
