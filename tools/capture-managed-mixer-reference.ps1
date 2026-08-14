[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\fixtures\expected\audio\managed-mixer.expected.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "The managed mixer capture currently requires Windows and Visual Studio C++ tools."
}

$referenceCandidates = @(
    $ReferenceRoot,
    $env:OXCE_REFERENCE_ROOT,
    (Join-Path $PSScriptRoot "..\..\oxce-bai"),
    (Join-Path $PSScriptRoot "..\..\..\CPP\oxce-bai")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$referencePath = $referenceCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $referencePath) { throw "Could not find the C++ reference checkout." }

$reference = (Resolve-Path -LiteralPath $referencePath).Path
$repository = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$probe = Join-Path $repository "fixtures\reference-probes\audio\managed_mixer_probe.cpp"
$fixture = Join-Path $repository "fixtures\public\audio\managed-mixer-stereo.hex"
$dependencies = Join-Path $reference "deps"
$referenceCommit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $referenceCommit -ne $ExpectedCommit) {
    throw "Reference checkout must be at pinned commit $ExpectedCommit; found '$referenceCommit'."
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$devCommand = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) { throw "A Visual Studio C++ x64 toolchain was not found." }

$work = Join-Path $repository "artifacts\reference-managed-mixer"
[IO.Directory]::CreateDirectory($work) | Out-Null
$executable = Join-Path $work "managed_mixer_probe.exe"
foreach ($value in @($devCommand, $probe, $fixture, $dependencies, $executable, $work)) {
    if ($value -match '["&|<>^]') { throw "Reference capture paths contain an unsafe character." }
}

$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{4}" && cl.exe /nologo /std:c++20 /EHsc /I"{3}\include\SDL" "{1}" /link /LIBPATH:"{3}\lib\x64" SDL.lib SDL_mixer.lib /OUT:"{2}"' -f `
    $devCommand, $probe, $executable, $dependencies, $work
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) { throw "The C++ managed mixer probe failed to compile." }
Copy-Item -LiteralPath (Join-Path $dependencies "lib\x64\SDL.dll") -Destination $work -Force
Copy-Item -LiteralPath (Join-Path $dependencies "lib\x64\SDL_mixer.dll") -Destination $work -Force

$raw = & $executable $fixture
if ($LASTEXITCODE -ne 0) { throw "The C++ managed mixer probe failed." }
$rawPath = Join-Path $work "managed-mixer.raw.json"
[IO.File]::WriteAllText($rawPath, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
& dotnet run --project (Join-Path $repository "tools\Oxce.FixtureTool") --configuration Release --no-restore -- normalize $rawPath $destination
if ($LASTEXITCODE -ne 0) { throw "The C++ managed mixer output could not be normalized." }
Write-Output $destination
