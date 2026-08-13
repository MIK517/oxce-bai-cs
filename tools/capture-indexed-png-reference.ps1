[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\fixtures\expected\images\indexed-png.expected.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "The indexed-PNG capture currently requires Windows and Visual Studio C++ tools."
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
$probe = Join-Path $repository "fixtures\reference-probes\images\indexed_png_probe.cpp"
$fixture = Join-Path $repository "fixtures\public\images\indexed-png.hex"
$lodePng = Join-Path $reference "src\lodepng.cpp"
$referenceCommit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $referenceCommit -ne $ExpectedCommit) {
    throw "Reference checkout must be at pinned commit $ExpectedCommit; found '$referenceCommit'."
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$devCommand = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) { throw "A Visual Studio C++ x64 toolchain was not found." }

$work = Join-Path $repository "artifacts\reference-indexed-png"
[IO.Directory]::CreateDirectory($work) | Out-Null
$executable = Join-Path $work "indexed_png_probe.exe"
foreach ($value in @($devCommand, $probe, $fixture, $lodePng, $executable, $work, $reference)) {
    if ($value -match '["&|<>^]') { throw "Reference capture paths contain an unsafe character." }
}

$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{6}" && cl.exe /nologo /std:c++20 /EHsc /I"{5}\src" "{1}" "{4}" /Fe:"{3}"' -f `
    $devCommand, $probe, $fixture, $executable, $lodePng, $reference, $work
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) { throw "The C++ indexed-PNG probe failed to compile." }

$raw = & $executable $fixture
if ($LASTEXITCODE -ne 0) { throw "The C++ indexed-PNG probe failed." }
$rawPath = Join-Path $work "indexed-png.raw.json"
[IO.File]::WriteAllText($rawPath, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
& dotnet run --project (Join-Path $repository "tools\Oxce.FixtureTool") --configuration Release --no-restore -- normalize $rawPath $destination
if ($LASTEXITCODE -ne 0) { throw "The C++ indexed-PNG output could not be normalized." }
Write-Output $destination
