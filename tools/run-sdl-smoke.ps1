[CmdletBinding()]
param(
    [string] $SdlDirectory = (Join-Path $PSScriptRoot "..\artifacts\sdl3-3.4.10\SDL3-3.4.10\lib\x64")
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "This helper currently validates the Windows x64 SDL3 runtime only."
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$nativeDirectory = (Resolve-Path -LiteralPath $SdlDirectory).Path
$library = Join-Path $nativeDirectory "SDL3.dll"
if (-not (Test-Path -LiteralPath $library -PathType Leaf)) {
    throw "SDL3.dll was not found in '$nativeDirectory'."
}

$env:PATH = $nativeDirectory + [IO.Path]::PathSeparator + $env:PATH
& dotnet run --project (Join-Path $repository "src\Oxce.App\Oxce.App.csproj") `
    --configuration Release -- --sdl-smoke
if ($LASTEXITCODE -ne 0) {
    throw "The SDL3 indexed-frame smoke test failed."
}
