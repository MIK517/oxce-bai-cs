[CmdletBinding()]
param(
    [string]$InstallationRoot = (Join-Path $PSScriptRoot "..\artifacts\private-install"),

    [string]$OutputPath = (Join-Path $PSScriptRoot "..\artifacts\baselines\private-content-baseline.json"),

    [ValidateRange(1, 20)]
    [int]$Runs = 3,

    [string]$MasterId = "40k",

    [string]$AddOnId = "40k_ROSIGMA_edits"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$installation = [IO.Path]::GetFullPath($InstallationRoot)
$output = [IO.Path]::GetFullPath($OutputPath)
$tool = Join-Path $repositoryRoot "tools\Oxce.FixtureTool\bin\Release\net10.0\Oxce.FixtureTool.dll"

if (-not [IO.Directory]::Exists($installation)) {
    throw "Staged installation does not exist: '$installation'."
}
if (-not [IO.File]::Exists((Join-Path $installation ".oxce-private-install-manifest.json"))) {
    throw "The installation is not a managed private corpus. Run tools/stage-private-install.ps1 first."
}
if (-not [IO.File]::Exists($tool)) {
    throw "The Release fixture tool has not been built: '$tool'."
}

function Get-Median([double[]]$Values) {
    $ordered = @($Values | Sort-Object)
    $middle = [Math]::Floor($ordered.Count / 2)
    if (($ordered.Count % 2) -eq 1) {
        return $ordered[$middle]
    }
    return ($ordered[$middle - 1] + $ordered[$middle]) / 2
}

$runDirectory = Join-Path ([IO.Path]::GetDirectoryName($output)) "private-content-baseline-runs"
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$results = [Collections.Generic.List[object]]::new()

for ($index = 1; $index -le $Runs; $index++) {
    $manifest = Join-Path $runDirectory ("content-{0:D2}.json" -f $index)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $raw = & dotnet $tool audit-content-install $installation $MasterId $AddOnId $manifest
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    if ($exitCode -ne 0) {
        throw "Content audit run $index failed with exit code $exitCode. Output: $($raw -join [Environment]::NewLine)"
    }

    $summary = ($raw | Select-Object -Last 1) | ConvertFrom-Json
    $memoryRaw = & dotnet $tool measure-content-install $installation $MasterId $AddOnId
    $memoryExitCode = $LASTEXITCODE
    if ($memoryExitCode -ne 0) {
        throw "Runtime measurement run $index failed with exit code $memoryExitCode. Output: $($memoryRaw -join [Environment]::NewLine)"
    }
    $memory = ($memoryRaw | Select-Object -Last 1) | ConvertFrom-Json
    $results.Add([ordered]@{
        run = $index
        stage = [string]$summary.stage
        cacheState = if ($index -eq 1) { "first-process" } else { "warm-process" }
        processElapsedMilliseconds = $timer.Elapsed.TotalMilliseconds
        buildElapsedMilliseconds = [double]$summary.buildElapsedMilliseconds
        normalizationElapsedMilliseconds = [double]$summary.normalizationElapsedMilliseconds
        parseElapsedMilliseconds = [double]$memory.parseElapsedMilliseconds
        parseAllocatedBytes = [long]$memory.parseAllocatedBytes
        composeElapsedMilliseconds = [double]$memory.composeElapsedMilliseconds
        composeAllocatedBytes = [long]$memory.composeAllocatedBytes
        typeAndLinkElapsedMilliseconds = [double]$memory.typeAndLinkElapsedMilliseconds
        typeAndLinkAllocatedBytes = [long]$memory.typeAndLinkAllocatedBytes
        resourceResolutionElapsedMilliseconds = [double]$memory.resourceResolutionElapsedMilliseconds
        resourceResolutionAllocatedBytes = [long]$memory.resourceResolutionAllocatedBytes
        scriptCompilationElapsedMilliseconds = [double]$memory.scriptCompilationElapsedMilliseconds
        scriptCompilationAllocatedBytes = [long]$memory.scriptCompilationAllocatedBytes
        runtimeRuleLinkingElapsedMilliseconds = [double]$memory.runtimeRuleLinkingElapsedMilliseconds
        runtimeRuleLinkingAllocatedBytes = [long]$memory.runtimeRuleLinkingAllocatedBytes
        sourceScopeCount = [int]$memory.sourceScopeCount
        apiScopeCount = [int]$memory.apiScopeCount
        tagCatalogBuildCount = [int]$memory.tagCatalogBuildCount
        allocatedBytesDuringBuild = [long]$memory.allocatedBytesDuringBuild
        managedBytesBeforeBuild = [long]$memory.managedBytesBeforeBuild
        managedBytesAfterBuild = [long]$memory.managedBytesAfterBuild
        managedBytesRetainedByBuild = [long]$summary.managedBytesRetainedByBuild
        managedBytesAfterAuditRelease = [long]$summary.managedBytesAfterAuditRelease
        managedBytesRetainedRuntime = [long]$memory.managedBytesRetainedRuntime
        workingSetBytes = [long]$memory.workingSetBytes
        peakWorkingSetBytes = [long]$memory.peakWorkingSetBytes
        parsedFiles = [int]$summary.parsedFiles
        attemptedScripts = [int]$summary.attemptedScripts
        scriptArtifacts = [int]$summary.scriptArtifacts
        resourceDescriptors = [int]$summary.resourceDescriptors
        runtimeRuleCount = [int]$summary.runtimeRuleCount
        diagnostics = [int]$summary.diagnostics
        errors = [int]$summary.errors
        manifestBytes = [long]$summary.manifestBytes
        manifestSha256 = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
    })
}

$warmResults = @($results | Where-Object cacheState -eq "warm-process")
if ($warmResults.Count -eq 0) {
    $warmResults = @($results)
}
$semanticManifestsIdentical = @($results.manifestSha256 | Sort-Object -Unique).Count -eq 1
if (-not $semanticManifestsIdentical) {
    throw "Content audit runs produced different semantic manifests."
}
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Could not determine the repository commit."
}

$baseline = [ordered]@{
    schemaVersion = 1
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    repositoryCommit = $commit
    workload = [ordered]@{
        installationRoot = $installation
        masterId = $MasterId
        addOnId = $AddOnId
        runs = $Runs
        note = "The first run is not a controlled cold-cache measurement; staging validation reads every file."
    }
    environment = [ordered]@{
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processor = $env:PROCESSOR_IDENTIFIER
        logicalProcessors = [Environment]::ProcessorCount
        dotnetSdk = (& dotnet --version).Trim()
    }
    aggregate = [ordered]@{
        warmProcessElapsedMedianMilliseconds = Get-Median @($warmResults.processElapsedMilliseconds)
        warmBuildElapsedMedianMilliseconds = Get-Median @($warmResults.buildElapsedMilliseconds)
        warmAllocatedMedianBytes = Get-Median @($warmResults.allocatedBytesDuringBuild)
        warmManagedRetainedMedianBytes = Get-Median @($warmResults.managedBytesRetainedRuntime)
        warmManagedRetainedWithAuditMedianBytes = Get-Median @($warmResults.managedBytesRetainedByBuild)
        warmPeakWorkingSetMedianBytes = Get-Median @($warmResults.peakWorkingSetBytes)
        warmRuntimeRuleLinkingElapsedMedianMilliseconds = Get-Median @($warmResults.runtimeRuleLinkingElapsedMilliseconds)
        warmRuntimeRuleLinkingAllocatedMedianBytes = Get-Median @($warmResults.runtimeRuleLinkingAllocatedBytes)
        semanticManifestsIdentical = $semanticManifestsIdentical
    }
    results = $results
}

[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output)) | Out-Null
[IO.File]::WriteAllText($output, (($baseline | ConvertTo-Json -Depth 8) + "`n"), [Text.UTF8Encoding]::new($false))
$baseline | ConvertTo-Json -Depth 8
