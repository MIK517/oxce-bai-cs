[CmdletBinding(DefaultParameterSetName = "Stage")]
param(
    [Parameter(Mandatory, ParameterSetName = "Stage")]
    [Parameter(ParameterSetName = "Validate")]
    [string]$SourceRoot,

    [string]$DestinationRoot = (Join-Path $PSScriptRoot "..\artifacts\private-install"),

    [string]$MasterId = "40k",

    [string]$AddOnId = "40k_ROSIGMA_edits",

    [Parameter(ParameterSetName = "Stage")]
    [switch]$Refresh,

    [Parameter(Mandatory, ParameterSetName = "Validate")]
    [switch]$ValidateOnly
)

$ErrorActionPreference = "Stop"
$manifestName = ".oxce-private-install-manifest.json"
$copiedRoots = @("common", "standard", "UFO", "TFTD", "user/mods")
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$source = if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $null } else { [IO.Path]::GetFullPath($SourceRoot) }
$destination = [IO.Path]::GetFullPath($DestinationRoot)

function Assert-DescendantPath([string]$Candidate, [string]$Parent, [string]$Description) {
    $relative = [IO.Path]::GetRelativePath($Parent, $Candidate)
    if ($relative -eq "." -or [IO.Path]::IsPathRooted($relative) -or
        $relative -eq ".." -or $relative.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal)) {
        throw "$Description must be a child of '$Parent'; found '$Candidate'."
    }
}

function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $hash = [Security.Cryptography.SHA256]::HashData($stream)
        return [Convert]::ToHexString($hash)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-StagedFiles([string]$Root) {
    return @(
        [IO.Directory]::EnumerateFiles($Root, "*", [IO.SearchOption]::AllDirectories) |
            Where-Object { [IO.Path]::GetRelativePath($Root, $_) -ne $manifestName } |
            Sort-Object { [IO.Path]::GetRelativePath($Root, $_).Replace('\', '/') }
    )
}

function Test-StagedInstall([string]$Root) {
    $manifestPath = Join-Path $Root $manifestName
    if (-not [IO.File]::Exists($manifestPath)) {
        throw "Staging manifest is missing: '$manifestPath'."
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1) {
        throw "Unsupported staging manifest schema '$($manifest.schemaVersion)'."
    }

    $expected = @{}
    foreach ($entry in $manifest.files) {
        $relative = [string]$entry.path
        $candidate = [IO.Path]::GetFullPath((Join-Path $Root $relative))
        Assert-DescendantPath $candidate $Root "Manifest file"
        if ($expected.ContainsKey($relative)) {
            throw "Duplicate manifest path '$relative'."
        }

        $expected[$relative] = $entry
        if (-not [IO.File]::Exists($candidate)) {
            throw "Staged file is missing: '$relative'."
        }

        $item = [IO.FileInfo]::new($candidate)
        if ($item.Length -ne [long]$entry.bytes) {
            throw "Staged file size differs for '$relative'."
        }
        if ((Get-Sha256 $candidate) -ne [string]$entry.sha256) {
            throw "Staged file hash differs for '$relative'."
        }
    }

    $actual = Get-StagedFiles $Root
    if ($actual.Count -ne $expected.Count) {
        throw "Staged file count differs: manifest=$($expected.Count), actual=$($actual.Count)."
    }
    foreach ($path in $actual) {
        $relative = [IO.Path]::GetRelativePath($Root, $path).Replace('\', '/')
        if (-not $expected.ContainsKey($relative)) {
            throw "Unexpected staged file '$relative'."
        }
    }

    [pscustomobject]@{
        destination = $Root
        files = $actual.Count
        bytes = [long]$manifest.totals.bytes
        masterId = [string]$manifest.selection.masterId
        addOnId = [string]$manifest.selection.addOnId
        manifest = $manifestPath
        status = "valid"
    }
}

Assert-DescendantPath $destination $artifactsRoot "Destination"
if ($ValidateOnly) {
    if (-not [IO.Directory]::Exists($destination)) {
        throw "Staged installation does not exist: '$destination'."
    }
    Test-StagedInstall $destination | ConvertTo-Json -Compress
    exit 0
}

if (-not [IO.Directory]::Exists($source)) {
    throw "Source installation does not exist: '$source'."
}
if ($source.Equals($destination, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Source and destination must differ."
}

foreach ($relativeRoot in $copiedRoots) {
    $required = Join-Path $source $relativeRoot
    if (-not [IO.Directory]::Exists($required)) {
        throw "Required data tree is missing: '$required'."
    }
    if (([IO.File]::GetAttributes($required) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Required data trees cannot be reparse points: '$required'."
    }
}

if ([IO.Directory]::Exists($destination) -and -not $Refresh) {
    throw "Destination already exists. Use -ValidateOnly to check it or -Refresh to replace it."
}

[IO.Directory]::CreateDirectory($artifactsRoot) | Out-Null
$staging = "$destination.staging-$([Guid]::NewGuid().ToString('N'))"
Assert-DescendantPath $staging $artifactsRoot "Temporary staging directory"
$backup = $null

try {
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    foreach ($relativeRoot in $copiedRoots) {
        $sourceTree = Join-Path $source $relativeRoot
        $destinationTree = Join-Path $staging $relativeRoot
        [IO.Directory]::CreateDirectory($destinationTree) | Out-Null

        foreach ($directory in [IO.Directory]::EnumerateDirectories($sourceTree, "*", [IO.SearchOption]::AllDirectories)) {
            $attributes = [IO.File]::GetAttributes($directory)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not supported in the private corpus: '$directory'."
            }
            $relative = [IO.Path]::GetRelativePath($sourceTree, $directory)
            [IO.Directory]::CreateDirectory((Join-Path $destinationTree $relative)) | Out-Null
        }

        foreach ($file in [IO.Directory]::EnumerateFiles($sourceTree, "*", [IO.SearchOption]::AllDirectories)) {
            $attributes = [IO.File]::GetAttributes($file)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points are not supported in the private corpus: '$file'."
            }
            $relative = [IO.Path]::GetRelativePath($sourceTree, $file)
            $copyPath = Join-Path $destinationTree $relative
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copyPath)) | Out-Null
            [IO.File]::Copy($file, $copyPath, $false)
            [IO.File]::SetLastWriteTimeUtc($copyPath, [IO.File]::GetLastWriteTimeUtc($file))
        }
    }

    $entries = [Collections.Generic.List[object]]::new()
    [long]$totalBytes = 0
    foreach ($file in Get-StagedFiles $staging) {
        $info = [IO.FileInfo]::new($file)
        $totalBytes += $info.Length
        $entries.Add([ordered]@{
            path = [IO.Path]::GetRelativePath($staging, $file).Replace('\', '/')
            bytes = $info.Length
            sha256 = Get-Sha256 $file
        })
    }

    $enginePath = Join-Path $source "OpenXcom.exe"
    $engine = $null
    if ([IO.File]::Exists($enginePath)) {
        $engineInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($enginePath)
        $engine = [ordered]@{
            fileName = "OpenXcom.exe"
            bytes = [IO.FileInfo]::new($enginePath).Length
            sha256 = Get-Sha256 $enginePath
            fileVersion = $engineInfo.FileVersion
            productVersion = $engineInfo.ProductVersion
        }
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        stagedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        sourceRoot = $source
        copiedRoots = $copiedRoots
        selection = [ordered]@{ masterId = $MasterId; addOnId = $AddOnId }
        engine = $engine
        exclusions = @(
            "installation-root executables and DLLs",
            "logs and user configuration",
            "user saves and screenshots"
        )
        totals = [ordered]@{ files = $entries.Count; bytes = $totalBytes }
        files = $entries
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $staging $manifestName), $manifestJson + "`n", [Text.UTF8Encoding]::new($false))
    Test-StagedInstall $staging | Out-Null

    if ([IO.Directory]::Exists($destination)) {
        $backup = "$destination.backup-$([Guid]::NewGuid().ToString('N'))"
        Assert-DescendantPath $backup $artifactsRoot "Backup directory"
        [IO.Directory]::Move($destination, $backup)
    }
    [IO.Directory]::Move($staging, $destination)
    if ($null -ne $backup) {
        [IO.Directory]::Delete($backup, $true)
        $backup = $null
    }

    Test-StagedInstall $destination | ConvertTo-Json -Compress
}
catch {
    if ([IO.Directory]::Exists($staging)) {
        [IO.Directory]::Delete($staging, $true)
    }
    if ($null -ne $backup -and [IO.Directory]::Exists($backup) -and -not [IO.Directory]::Exists($destination)) {
        [IO.Directory]::Move($backup, $destination)
    }
    throw
}
