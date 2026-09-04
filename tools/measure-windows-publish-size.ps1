[CmdletBinding()]
param(
    [string] $RuntimeIdentifier = "win-x64",
    [string] $OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\publish-size\win-x64"),
    [string] $SdlDirectory = (Join-Path $PSScriptRoot "..\artifacts\sdl3-3.4.10\SDL3-3.4.10\lib\x64"),
    [switch] $NoRestore
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "Windows publish-size measurement must run on Windows."
}
if ($RuntimeIdentifier -ne "win-x64") {
    throw "This measurement currently defines only the win-x64 distribution profile."
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$artifacts = [IO.Path]::GetFullPath((Join-Path $repository "artifacts"))
$output = [IO.Path]::GetFullPath($OutputRoot)
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputRoot must be a child of the repository artifacts directory."
}

$project = Join-Path $repository "src\Oxce.App\Oxce.App.csproj"
$frameworkDependent = Join-Path $output "framework-dependent"
$selfContained = Join-Path $output "self-contained"
$staged = Join-Path $output "staged-self-contained"
$archives = Join-Path $output "archives"
$reportPath = Join-Path $output "publish-size.json"
$ownedPaths = @($frameworkDependent, $selfContained, $staged, $archives, $reportPath)
foreach ($path in $ownedPaths) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
[IO.Directory]::CreateDirectory($output) | Out-Null

if (-not $NoRestore) {
    & dotnet restore $project --runtime $RuntimeIdentifier
    if ($LASTEXITCODE -ne 0) {
        throw "The win-x64 publish restore failed."
    }
}

function Invoke-Publish([string] $destination, [bool] $selfContainedProfile) {
    & dotnet publish $project `
        --configuration Release `
        --runtime $RuntimeIdentifier `
        --self-contained $selfContainedProfile.ToString().ToLowerInvariant() `
        --no-restore `
        --output $destination `
        -p:PublishTrimmed=false `
        -p:PublishSingleFile=false `
        -p:PublishAot=false
    if ($LASTEXITCODE -ne 0) {
        throw "The $RuntimeIdentifier publish failed for '$destination'."
    }
}

Invoke-Publish $frameworkDependent $false
Invoke-Publish $selfContained $true

[IO.Directory]::CreateDirectory($staged) | Out-Null
Get-ChildItem -LiteralPath $selfContained | Copy-Item -Destination $staged -Recurse
$sdl = Join-Path ([IO.Path]::GetFullPath($SdlDirectory)) "SDL3.dll"
if (-not [IO.File]::Exists($sdl)) {
    throw "SDL3.dll was not found in '$SdlDirectory'."
}
$expectedSdlHash = "C39FBDA24ECA1009B06A4D4E340D12511E3C8B0D44C4898D29E336E2CC7A25F0"
$actualSdlHash = (Get-FileHash -LiteralPath $sdl -Algorithm SHA256).Hash
if ($actualSdlHash -ne $expectedSdlHash) {
    throw "SDL3.dll checksum mismatch: expected $expectedSdlHash, found $actualSdlHash."
}
Copy-Item -LiteralPath $sdl -Destination (Join-Path $staged "SDL3.dll")

[IO.Directory]::CreateDirectory($archives) | Out-Null
function Measure-Profile([string] $name, [string] $directory) {
    $files = @(Get-ChildItem -LiteralPath $directory -File -Recurse)
    $archive = Join-Path $archives "$name.zip"
    [IO.Compression.ZipFile]::CreateFromDirectory(
        $directory,
        $archive,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)
    [pscustomobject]@{
        name = $name
        fileCount = $files.Count
        uncompressedBytes = [long](($files | Measure-Object -Property Length -Sum).Sum)
        zipBytes = (Get-Item -LiteralPath $archive).Length
    }
}

$report = [ordered]@{
    schemaVersion = 1
    runtimeIdentifier = $RuntimeIdentifier
    dotnetSdk = (& dotnet --version).Trim()
    osDescription = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    publishProperties = [ordered]@{
        configuration = "Release"
        trimmed = $false
        singleFile = $false
        nativeAot = $false
    }
    sdl = [ordered]@{
        version = "3.4.10"
        sha256 = $actualSdlHash
        bytes = (Get-Item -LiteralPath $sdl).Length
    }
    profiles = @(
        Measure-Profile "framework-dependent" $frameworkDependent
        Measure-Profile "self-contained" $selfContained
        Measure-Profile "staged-self-contained" $staged
    )
}
$json = $report | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText($reportPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
$json
