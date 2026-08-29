[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $InputPath,
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,
    [string] $ExpectedCommit = "4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15",
    [int] $ExpectedParserCount = 60,
    [int] $ExpectedBindingCount = 755,
    [int] $ExpectedConstantCount = 132,
    [int] $ExpectedTypeTokenCount = 94
)

$ErrorActionPreference = "Stop"
$inputDocument = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json
$parsers = [Collections.Generic.List[object]]::new()
$bindings = @{}
$constants = @{}
$typeTokens = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)

function Read-DataLine([string] $Line) {
    $match = [regex]::Match(
        $Line,
        '^Name:\s+(?<name>\S+)\s+(?:(?<modifier>var|ptr|ptre)\s+)?(?<type>\S+)(?:\s+(?<value>-?[0-9]+))?\s*$')
    if (-not $match.Success) { return $null }
    $token = (@($match.Groups['modifier'].Value, $match.Groups['type'].Value) |
        Where-Object { $_ }) -join ' '
    [ordered]@{
        name = $match.Groups['name'].Value
        type = $token
        value = if ($match.Groups['value'].Success) { [int]$match.Groups['value'].Value } else { $null }
    }
}

foreach ($log in $inputDocument.logs) {
    $header = [regex]::Match($log, "Script info for:\s+'(?<name>[^']+)'\s+in group:\s+'(?<group>[^']+)'")
    if (-not $header.Success) { continue }

    $name = $header.Groups['name'].Value
    $group = $header.Groups['group'].Value
    $outputs = [Collections.Generic.List[object]]::new()
    $inputs = [Collections.Generic.List[object]]::new()
    $outputNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $section = ''
    foreach ($line in ($log -split "`n")) {
        $trimmed = $line.TrimEnd("`r")
        if ($trimmed -eq 'Script return values:') { $section = 'outputs'; continue }
        if ($trimmed -eq 'Script data:') { $section = 'data'; continue }
        if ($trimmed -eq 'Script operations:') { $section = 'operations'; continue }
        if (-not $trimmed.StartsWith('Name:', [StringComparison]::Ordinal)) { continue }

        if ($section -eq 'operations') {
            $operation = [regex]::Match(
                $trimmed,
                '^Name:\s+(?<name>\S+)\s+Args:\s+(?<args>.*?)(?:\s+Desc:.*)?$')
            if (-not $operation.Success) {
                throw "Could not parse operation metadata for '$name': $trimmed"
            }
            $argumentTokens = @([regex]::Matches($operation.Groups['args'].Value, '\[(?<type>[^\]]+)\]') |
                ForEach-Object { $_.Groups['type'].Value })
            foreach ($typeToken in $argumentTokens) { $null = $typeTokens.Add($typeToken) }
            $key = $operation.Groups['name'].Value + [char]31 + ($argumentTokens -join [char]30)
            if (-not $bindings.ContainsKey($key)) {
                $bindings[$key] = [ordered]@{
                    name = $operation.Groups['name'].Value
                    parameters = $argumentTokens
                    parsers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                }
            }
            $null = $bindings[$key].parsers.Add($name)
            continue
        }

        $data = Read-DataLine $trimmed
        if ($null -eq $data) {
            throw "Could not parse value metadata for '$name': $trimmed"
        }
        $null = $typeTokens.Add($data.type)
        if ($section -eq 'outputs') {
            $outputs.Add([ordered]@{ name = $data.name; type = $data.type })
            $null = $outputNames.Add($data.name)
        }
        elseif ($section -eq 'data') {
            if ($null -ne $data.value -and $data.type -eq 'int') {
                $key = $data.name + [char]31 + $data.value
                if (-not $constants.ContainsKey($key)) {
                    $constants[$key] = [ordered]@{
                        name = $data.name
                        value = $data.value
                        parsers = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                    }
                }
                $null = $constants[$key].parsers.Add($name)
            }
            elseif ($data.name -notin @('__', 'null') -and -not $outputNames.Contains($data.name)) {
                $inputs.Add([ordered]@{ name = $data.name; type = $data.type })
            }
        }
    }

    $parsers.Add([ordered]@{
        name = $name
        group = $group
        supportsEvents = $log.Contains('Have global events', [StringComparison]::Ordinal)
        outputs = @($outputs)
        inputs = @($inputs)
    })
}

$bindingId = 10000
$normalizedBindings = @($bindings.GetEnumerator() | Sort-Object Key | ForEach-Object {
    [ordered]@{
        id = $bindingId++
        name = $_.Value.name
        parameters = @($_.Value.parameters)
        parsers = @($_.Value.parsers | Sort-Object)
    }
})
$normalizedConstants = @($constants.GetEnumerator() | Sort-Object Key | ForEach-Object {
    [ordered]@{
        name = $_.Value.name
        value = $_.Value.value
        parsers = @($_.Value.parsers | Sort-Object)
    }
})

$uniqueParserCount = @($parsers | ForEach-Object { $_['name'] } | Sort-Object -Unique).Count
if ($uniqueParserCount -ne $parsers.Count -or
    $parsers.Count -ne $ExpectedParserCount -or
    $normalizedBindings.Count -ne $ExpectedBindingCount -or
    $normalizedConstants.Count -ne $ExpectedConstantCount -or
    $typeTokens.Count -ne $ExpectedTypeTokenCount) {
    throw "Script API metadata drift: found $($parsers.Count) parsers ($uniqueParserCount unique), " +
        "$($normalizedBindings.Count) bindings, $($normalizedConstants.Count) constants, and $($typeTokens.Count) type tokens; " +
        "expected $ExpectedParserCount, $ExpectedBindingCount, $ExpectedConstantCount, and $ExpectedTypeTokenCount."
}

$catalog = [ordered]@{
    schemaVersion = 1
    reference = [ordered]@{
        repository = 'oxce-bai'
        commit = $ExpectedCommit
        metadata = 'ScriptParserBase::logScriptMetadata'
    }
    counts = [ordered]@{
        parsers = $parsers.Count
        bindings = $normalizedBindings.Count
        constants = $normalizedConstants.Count
        typeTokens = $typeTokens.Count
        unresolved = 0
    }
    typeTokens = @($typeTokens | Sort-Object)
    parsers = @($parsers | Sort-Object { $_.name })
    bindings = $normalizedBindings
    constants = $normalizedConstants
    unresolved = @()
}

$json = $catalog | ConvertTo-Json -Depth 12 -Compress
$destination = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
[IO.File]::WriteAllText($destination, $json + "`n", [Text.UTF8Encoding]::new($false))
Write-Output $destination
