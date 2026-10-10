[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$SourceIndex, [string]$BaselineRef = '159c907', [string]$ToolsRoot)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$source = (Resolve-Path -LiteralPath $SourceIndex).Path
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools' }
$sdk = Join-Path $ToolsRoot 'flex-air'
$jdk = @(Get-ChildItem -LiteralPath (Join-Path $ToolsRoot 'java') -Directory)
if ($jdk.Count -ne 1) { throw 'Expected one portable JDK.' }
$java = Join-Path $jdk[0].FullName 'bin/java.exe'
$output = Join-Path $project ('bin-tests/benchmark-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location -LiteralPath $project
try {
    $archive = Join-Path $output 'baseline.zip'
    $snapshot = Join-Path $output 'baseline-source'
    & git archive -o $archive $BaselineRef src libs locale
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create baseline source snapshot.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $snapshot
    foreach ($label in @('baseline','current')) {
        $sourceRoot = if ($label -eq 'baseline') { $snapshot } else { $project }
        $bundle = Join-Path $output $label
        New-Item -ItemType Directory -Path $bundle | Out-Null
        & $java -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
            ('-source-path=' + (Join-Path $sourceRoot 'src')) ('-library-path+=' + (Join-Path $sourceRoot 'libs')) `
            '-locale=en_US' ('-source-path+=' + (Join-Path $sourceRoot 'locale/{locale}')) '-debug=false' `
            ('-output=' + (Join-Path $bundle 'CompileBenchmark.swf')) 'tests/CompileBenchmark.as'
        if ($LASTEXITCODE -ne 0) { throw "$label benchmark compilation failed." }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CompileBenchmark-app.xml') -Destination $bundle
        & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $bundle 'CompileBenchmark-app.xml') -- $source $bundle
        if ($LASTEXITCODE -ne 0) { throw "$label benchmark failed." }
    }
    $baselineHash = (Get-FileHash -LiteralPath (Join-Path $output 'baseline/Tibia.spr')).Hash
    $currentHash = (Get-FileHash -LiteralPath (Join-Path $output 'current/Tibia.spr')).Hash
    if ($baselineHash -ne $currentHash) { throw 'Baseline and current output bytes differ.' }
    $baseline = Get-Content -LiteralPath (Join-Path $output 'baseline/benchmark.json') -Raw | ConvertFrom-Json
    $current = Get-Content -LiteralPath (Join-Path $output 'current/benchmark.json') -Raw | ConvertFrom-Json
    Write-Output "PASS: byte-identical outputs. Baseline: $($baseline.milliseconds) ms; current: $($current.milliseconds) ms; sprites: $($current.sprites)."
    Write-Output "Benchmark output: $output"
} finally { Pop-Location }
