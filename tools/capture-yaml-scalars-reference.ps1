[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\artifacts\reference-yaml-scalars\yaml-scalars.actual.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "The YAML scalar capture currently requires Windows and Visual Studio C++ tools."
}

$referenceCandidates = @(
    $ReferenceRoot,
    $env:OXCE_REFERENCE_ROOT,
    (Join-Path $PSScriptRoot "..\..\oxce-bai"),
    (Join-Path $PSScriptRoot "..\..\..\CPP\oxce-bai")
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$referencePath = $referenceCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $referencePath) {
    throw "Could not find the C++ reference checkout. Pass -ReferenceRoot or set OXCE_REFERENCE_ROOT."
}

$reference = (Resolve-Path -LiteralPath $referencePath).Path
$repository = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$probe = Join-Path $repository "fixtures\reference-probes\yaml\yaml_scalar_probe.cpp"
$fixture = Join-Path $repository "fixtures\public\yaml\scalar-conversions.yml"
$referenceCommit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $referenceCommit -ne $ExpectedCommit) {
    throw "Reference checkout must be at pinned commit $ExpectedCommit; found '$referenceCommit'."
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$devCommand = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) {
    throw "A Visual Studio C++ x64 toolchain was not found."
}

$work = Join-Path $repository "artifacts\reference-yaml-scalars"
[IO.Directory]::CreateDirectory($work) | Out-Null
$executable = Join-Path $work "yaml_scalar_probe.exe"
$includeRoot = Join-Path $reference "libs\rapidyaml"
$sources = Get-ChildItem (Join-Path $reference "libs\rapidyaml\c4") -Recurse -Filter *.cpp |
    Sort-Object FullName |
    ForEach-Object { '"' + $_.FullName + '"' }

foreach ($value in @($devCommand, $probe, $fixture, $executable, $includeRoot, $work)) {
    if ($value -match '["&|<>^]') {
        throw "Reference capture paths contain a character that is unsafe for cmd.exe."
    }
}

$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{5}" && cl.exe /nologo /std:c++20 /EHsc /D_CRT_SECURE_NO_WARNINGS /I"{1}" "{2}" {3} /Fe:"{4}"' -f `
    $devCommand, $includeRoot, $probe, ($sources -join ' '), $executable, $work
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) {
    throw "The C++ YAML scalar probe failed to compile."
}

$raw = & $executable $fixture
if ($LASTEXITCODE -ne 0) {
    throw "The C++ YAML scalar probe failed."
}

$rawPath = Join-Path $work "yaml-scalars.raw.json"
[IO.File]::WriteAllText($rawPath, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$destination = [IO.Path]::GetFullPath($OutputPath)
& dotnet run --project (Join-Path $repository "tools\Oxce.FixtureTool") --configuration Release --no-restore -- normalize $rawPath $destination
if ($LASTEXITCODE -ne 0) {
    throw "The C++ YAML scalar output could not be normalized."
}

Write-Output $destination
