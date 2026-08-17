[CmdletBinding()]
param(
    [string] $SdlDirectory = (Join-Path $PSScriptRoot "..\artifacts\sdl3-3.4.10\SDL3-3.4.10\lib\x64"),
    [switch] $Audio,
    [switch] $DummyAudio
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
if ($DummyAudio -and -not $Audio) {
    throw "-DummyAudio is only valid together with -Audio."
}

$previousAudioDriver = $env:SDL_AUDIO_DRIVER
$work = Join-Path ([IO.Path]::GetTempPath()) ("oxce-sdl-smoke-" + [Guid]::NewGuid().ToString("N"))
try {
    [IO.Directory]::CreateDirectory($work) | Out-Null
    & dotnet publish (Join-Path $repository "src\Oxce.App\Oxce.App.csproj") `
        --configuration Release --no-restore --output $work
    if ($LASTEXITCODE -ne 0) {
        throw "The SDL3 smoke application could not be published."
    }
    Copy-Item -LiteralPath $library -Destination (Join-Path $work "SDL3.dll")
    if ($DummyAudio) {
        $env:SDL_AUDIO_DRIVER = "dummy"
    }

    $smokeArgument = if ($Audio) { "--sdl-audio-smoke" } else { "--sdl-smoke" }
    & dotnet (Join-Path $work "Oxce.App.dll") $smokeArgument
    if ($LASTEXITCODE -ne 0) {
        $smokeName = if ($Audio) { "audio-stream" } else { "indexed-frame" }
        throw "The SDL3 $smokeName smoke test failed."
    }
}
finally {
    $env:SDL_AUDIO_DRIVER = $previousAudioDriver
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
}
