[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\artifacts\reference-script-core\script-core.actual.json"),
    [string] $ProbeStem = "script_core_probe",
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$ErrorActionPreference = "Stop"
if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
    throw "The script-core capture currently requires Windows and Visual Studio C++ tools."
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

$work = Join-Path $repository "artifacts\reference-script-core"
[IO.Directory]::CreateDirectory($work) | Out-Null
$probe = Join-Path $repository "fixtures\reference-probes\scripting\$ProbeStem.cpp"
$scriptSource = Join-Path $reference "src\Engine\Script.cpp"
$yamlSource = Join-Path $reference "src\Engine\Yaml.cpp"
$executable = Join-Path $work "$ProbeStem.exe"
$includeSource = Join-Path $reference "src"
$includeSdl = Join-Path $reference "deps\include\SDL"
$includeYaml = Join-Path $reference "libs\rapidyaml"
$yamlSources = Get-ChildItem (Join-Path $includeYaml "c4") -Recurse -Filter *.cpp |
    Sort-Object FullName |
    ForEach-Object { '"' + $_.FullName + '"' }

foreach ($value in @(
    $devCommand, $probe, $scriptSource, $yamlSource, $executable,
    $includeSource, $includeSdl, $includeYaml
)) {
    if ($value -match '["&|<>^]') {
        throw "Reference capture paths contain a character that is unsafe for cmd.exe."
    }
}

$compile = 'call "{0}" -no_logo -arch=x64 -host_arch=x64 && cd /d "{9}" && cl.exe /nologo /std:c++20 /EHsc /O2 /Gy /DNDEBUG /D_CRT_SECURE_NO_WARNINGS /I"{1}" /I"{2}" /I"{3}" "{4}" "{5}" "{6}" {7} /Fe:"{8}" /link /OPT:REF' -f `
    $devCommand, $includeSource, $includeSdl, $includeYaml, $probe, $scriptSource,
    $yamlSource, ($yamlSources -join ' '), $executable, $work
& $env:ComSpec /d /c $compile
if ($LASTEXITCODE -ne 0) {
    throw "The C++ scripting reference probe '$ProbeStem' failed to compile."
}

$raw = & $executable
if ($LASTEXITCODE -ne 0) {
    throw "The C++ scripting reference probe '$ProbeStem' failed."
}

$rawPath = Join-Path $work "$ProbeStem.raw.json"
[IO.File]::WriteAllText($rawPath, ($raw -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$destination = [IO.Path]::GetFullPath($OutputPath)
& dotnet run --project (Join-Path $repository "tools\Oxce.FixtureTool") `
    --configuration Release --no-restore -- normalize $rawPath $destination
if ($LASTEXITCODE -ne 0) {
    throw "The C++ scripting output from '$ProbeStem' could not be normalized."
}

Write-Output $destination
