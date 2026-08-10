[CmdletBinding()]
param(
    [string] $ReferenceRoot = (Join-Path $PSScriptRoot "..\..\oxce-bai"),
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\artifacts\reference-position\position.actual.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "The bootstrap position capture currently requires Windows and Visual Studio C++ tools."
}

$reference = (Resolve-Path -LiteralPath $ReferenceRoot).Path
$repository = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$probe = Join-Path $repository "fixtures\reference-probes\position\position_probe.cpp"
$referenceCommit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $referenceCommit -ne $ExpectedCommit) {
    throw "Reference checkout must be at pinned commit $ExpectedCommit; found '$referenceCommit'."
}

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer's vswhere.exe was not found."
}

$devCommand = & $vswhere -latest -products * `
    -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    -find Common7\Tools\VsDevCmd.bat
if (-not $devCommand) {
    throw "A Visual Studio C++ x64 toolchain was not found."
}

$work = Join-Path $repository "artifacts\reference-position"
[IO.Directory]::CreateDirectory($work) | Out-Null
$executable = Join-Path $work "position_probe.exe"
$object = Join-Path $work "position_probe.obj"
$includeSource = Join-Path $reference "src"
$includeSdl = Join-Path $reference "deps\include\SDL"
$includeYaml = Join-Path $reference "libs\rapidyaml"

foreach ($value in @($devCommand, $probe, $executable, $object, $includeSource, $includeSdl, $includeYaml)) {
    if ($value -match '["&|<>^]') {
        throw "Reference capture paths contain a character that is unsafe for cmd.exe."
    }
}

$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cl.exe /nologo /std:c++20 /EHsc /I"{1}" /I"{2}" /I"{3}" "{4}" /Fe:"{5}" /Fo:"{6}"' -f `
    $devCommand, $includeSource, $includeSdl, $includeYaml, $probe, $executable, $object
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) {
    throw "The C++ position reference probe failed to compile."
}

$raw = & $executable
if ($LASTEXITCODE -ne 0) {
    throw "The C++ position reference probe failed."
}

$rawPath = Join-Path $work "position.raw.json"
[IO.File]::WriteAllText($rawPath, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$destination = [IO.Path]::GetFullPath($OutputPath)
& dotnet run --project (Join-Path $repository "tools\Oxce.FixtureTool") --configuration Release --no-restore -- normalize $rawPath $destination
if ($LASTEXITCODE -ne 0) {
    throw "The C++ position output could not be normalized."
}

Write-Output $destination
