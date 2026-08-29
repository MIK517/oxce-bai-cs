[CmdletBinding()]
param(
    [string] $ReferenceRoot,
    [string] $OutputPath = (Join-Path $PSScriptRoot "..\docs\compatibility\script-inventory.json"),
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15"
)

$ErrorActionPreference = "Stop"
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
$source = Join-Path $reference "src"
$referenceCommit = (& git -c "safe.directory=$reference" -C $reference rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $referenceCommit -ne $ExpectedCommit) {
    throw "Reference checkout must be at pinned commit $ExpectedCommit; found '$referenceCommit'."
}

function Convert-ToReferencePath([string] $Path) {
    $relative = [IO.Path]::GetRelativePath($reference, $Path).Replace('\', '/')
    if ($relative -eq '..' -or $relative.StartsWith('../', [StringComparison]::Ordinal)) {
        throw "Inventory path escapes the reference checkout: $Path"
    }
    $relative
}

function Get-LineNumber([string] $Text, [int] $Offset) {
    if ($Offset -le 0) { return 1 }
    ([regex]::Matches($Text.Substring(0, $Offset), "`n")).Count + 1
}

function Get-Matches([string] $Pattern, [string] $Category) {
    $results = [Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Include *.cpp,*.h | Sort-Object FullName) {
        $text = [IO.File]::ReadAllText($file.FullName)
        foreach ($match in [regex]::Matches($text, $Pattern, [Text.RegularExpressions.RegexOptions]::Multiline)) {
            $results.Add([ordered]@{
                category = $Category
                name = $match.Groups['name'].Value
                owner = $match.Groups['owner'].Value
                member = $match.Groups['member'].Value
                source = Convert-ToReferencePath $file.FullName
                line = Get-LineNumber $text $match.Index
            })
        }
    }
    @($results | Sort-Object source, line, category, owner, name)
}

function Get-ScriptRegisterCallMatches([string] $Pattern, [string] $Category) {
    $results = [Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Include *.cpp | Sort-Object FullName) {
        $lines = [IO.File]::ReadAllLines($file.FullName)
        $owner = $null
        $started = $false
        $depth = 0
        for ($index = 0; $index -lt $lines.Length; ++$index) {
            $line = $lines[$index]
            if ($null -eq $owner) {
                $definition = [regex]::Match(
                    $line, '^\s*void\s+(?<owner>[A-Za-z_][A-Za-z0-9_:<>]*)::ScriptRegister\s*\(')
                if (-not $definition.Success) { continue }
                $owner = $definition.Groups['owner'].Value
            }

            $openCount = ([regex]::Matches($line, '\{')).Count
            $closeCount = ([regex]::Matches($line, '\}')).Count
            if ($openCount -gt 0) { $started = $true }
            if ($started) {
                foreach ($match in [regex]::Matches($line, $Pattern)) {
                    $results.Add([ordered]@{
                        category = $Category
                        name = $match.Groups['name'].Value
                        owner = $owner
                        member = $match.Groups['member'].Value
                        source = Convert-ToReferencePath $file.FullName
                        line = $index + 1
                    })
                }
                $depth += $openCount - $closeCount
                if ($depth -le 0) {
                    $owner = $null
                    $started = $false
                    $depth = 0
                }
            }
        }
    }
    @($results | Sort-Object source, line, category, owner, name)
}

$scriptPath = Join-Path $source "Engine\Script.cpp"
$scriptHeaderPath = Join-Path $source "Engine\Script.h"
$scriptText = [IO.File]::ReadAllText($scriptPath)
$scriptHeaderText = [IO.File]::ReadAllText($scriptHeaderPath)

$macroOperations = [regex]::Matches($scriptText, '(?m)^\s*IMPL\((?<name>[A-Za-z_][A-Za-z0-9_]*)\s*,') |
    ForEach-Object { $_.Groups['name'].Value }
$directOperations = [regex]::Matches($scriptText, '(?m)\b(?:buildin|addParser(?:<[^\r\n;]+>)?)\("(?<name>[A-Za-z_][A-Za-z0-9_]*)"') |
    ForEach-Object { $_.Groups['name'].Value }
$primitiveTypes = [regex]::Matches($scriptText, '(?m)\baddType<[^>]+>\("(?<name>[A-Za-z_][A-Za-z0-9_]*)"\)') |
    ForEach-Object { $_.Groups['name'].Value }

$registerDefinitions = Get-Matches `
    '(?m)^\s*void\s+(?<owner>[A-Za-z_][A-Za-z0-9_:<>]*)::ScriptRegister\s*\([^)]*\)' `
    'script-register'
$bindingNames = Get-ScriptRegisterCallMatches `
    '\b[A-Za-z_][A-Za-z0-9_]*\.(?<member>add[A-Za-z0-9_]*)(?:<[^;\r\n]+?>)?\s*\(\s*"(?<name>[A-Za-z_][A-Za-z0-9_.]*)"' `
    'binding-name'
$constantNames = Get-ScriptRegisterCallMatches `
    '\b[A-Za-z_][A-Za-z0-9_]*\.(?<member>addCustomConst)\s*\(\s*"(?<name>[A-Za-z_][A-Za-z0-9_.]*)"' `
    'constant-name'
$globalConstantNames = Get-Matches `
    '(?m)\b(?<member>addConst)\s*\(\s*"(?<name>[A-Za-z_][A-Za-z0-9_.]*)"' `
    'global-constant-name'
$tagValueTypes = Get-Matches `
    '(?m)\b(?<member>addTagValueType)(?:<[^;\r\n]+?>)?\s*\(\s*"(?<name>[A-Za-z_][A-Za-z0-9_.]*)"' `
    'tag-value-type'
$parserTypes = Get-Matches `
    '(?m)^\s*struct\s+(?<name>[A-Za-z_][A-Za-z0-9_]*Parser)\s*:\s*(?<owner>ScriptParser(?:Events)?)[^\r\n{]*' `
    'parser-type'
$scriptValueOwners = Get-Matches `
    '(?m)\bScriptValues<(?<name>[A-Za-z_][A-Za-z0-9_:]*)>' `
    'script-values-owner'

$limits = [ordered]@{}
foreach ($name in @('ScriptMaxOut', 'ScriptMaxArg')) {
    $match = [regex]::Match($scriptHeaderText, "(?m)^constexpr size_t $name = (?<value>[0-9]+);")
    if (-not $match.Success) { throw "Could not find $name in src/Engine/Script.h." }
    $limits[$name] = [int]$match.Groups['value'].Value
}
$registerMatch = [regex]::Match(
    $scriptHeaderText, '(?m)^constexpr size_t ScriptMaxReg = (?<factor>[0-9]+)\*sizeof\(void\*\);')
if (-not $registerMatch.Success) { throw "Could not find ScriptMaxReg in src/Engine/Script.h." }
$limits['ScriptMaxRegPointerFactor'] = [int]$registerMatch.Groups['factor'].Value
$eventsMaxMatch = [regex]::Match($scriptHeaderText, '(?m)^\s*constexpr static size_t EventsMax = (?<value>[0-9]+);')
$offsetScaleMatch = [regex]::Match($scriptHeaderText, '(?m)^\s*constexpr static size_t OffsetScale = (?<value>[0-9]+);')
$offsetMaxMatch = [regex]::Match($scriptHeaderText, '(?m)^\s*constexpr static size_t OffsetMax = (?<factor>[0-9]+) \* OffsetScale;')
if (-not $eventsMaxMatch.Success -or -not $offsetScaleMatch.Success -or -not $offsetMaxMatch.Success) {
    throw "Could not find event limits in src/Engine/Script.h."
}
$limits['EventsMax'] = [int]$eventsMaxMatch.Groups['value'].Value
$limits['EventOffsetScale'] = [int]$offsetScaleMatch.Groups['value'].Value
$limits['EventOffsetMax'] = [int]$offsetMaxMatch.Groups['factor'].Value * [int]$offsetScaleMatch.Groups['value'].Value

$typeEncoding = [ordered]@{
    baseStep = 16
    invalid = 0
    null = 16
    int = 32
    label = 48
    text = 64
    separator = 80
    firstCustom = 96
    modifiers = [ordered]@{
        register = 1
        writableRegister = 3
        pointer = 4
        editablePointer = 12
    }
}

$inventory = [ordered]@{
    schemaVersion = 2
    reference = [ordered]@{
        repository = 'oxce-bai'
        commit = $referenceCommit
    }
    core = [ordered]@{
        limits = $limits
        typeEncoding = $typeEncoding
        primitiveTypes = @($primitiveTypes + @('label', 'null') | Sort-Object -Unique)
        builtInOperations = @($macroOperations + $directOperations | Sort-Object -Unique)
        macroOperations = @($macroOperations | Sort-Object -Unique)
        directRegistrations = @($directOperations | Sort-Object -Unique)
    }
    registrations = [ordered]@{
        scriptRegisterDefinitions = @($registerDefinitions)
        bindingNameCandidates = @($bindingNames)
        constantNameCandidates = @($constantNames + $globalConstantNames |
            Sort-Object source, line, category, owner, name)
        tagValueTypes = @($tagValueTypes)
        parserTypes = @($parserTypes)
        scriptValueOwners = @($scriptValueOwners)
    }
    caveats = @(
        'Binding and constant entries are source candidates; overload signatures and owning parser groups require semantic refinement.',
        'Gameplay bindings are inventoried here but remain implemented with their owning gameplay slices.'
    )
}

$json = $inventory | ConvertTo-Json -Depth 10
$json = $json.Replace("`r`n", "`n") + "`n"
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, $json, [Text.UTF8Encoding]::new($false))
Write-Output $destination
