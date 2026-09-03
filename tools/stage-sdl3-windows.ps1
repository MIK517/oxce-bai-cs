param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [Parameter(Mandatory = $true)]
    [string]$WorkDirectory
)

$ErrorActionPreference = "Stop"
$sdlVersion = "3.4.10"
$archiveSha256 = "E2B336B10B037934AF98308027410732EF7B22F2C6697D58092AA1C209FAE7D7"
$work = [IO.Path]::GetFullPath($WorkDirectory)
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
$archive = Join-Path $work "SDL3-devel-$sdlVersion-VC.zip"
$expanded = Join-Path $work "expanded"
New-Item -ItemType Directory -Force -Path $work, $destination, $expanded | Out-Null

if (-not [IO.File]::Exists($archive)) {
    Invoke-WebRequest `
        -Uri "https://github.com/libsdl-org/SDL/releases/download/release-$sdlVersion/SDL3-devel-$sdlVersion-VC.zip" `
        -OutFile $archive
}
$actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
if ($actualHash -ne $archiveSha256) {
    throw "SDL archive checksum mismatch: expected $archiveSha256, found $actualHash."
}

Expand-Archive -LiteralPath $archive -DestinationPath $expanded -Force
$library = Join-Path $expanded "SDL3-$sdlVersion/lib/x64/SDL3.dll"
if (-not [IO.File]::Exists($library)) {
    throw "The pinned SDL archive does not contain the expected x64 SDL3.dll."
}
Copy-Item -LiteralPath $library -Destination (Join-Path $destination "SDL3.dll")
Write-Output "SDL $sdlVersion staged in $destination"
