[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RealAssets, [switch]$FullCompile)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$tools = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk = Join-Path $tools 'flex-air'
$jdk = @(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
if ($jdk.Count -ne 1) { throw 'Expected exactly one portable JDK.' }
$output = Join-Path $project 'bin-tests/wot64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location -LiteralPath $project
try {
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx1g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
        ('-output=' + (Join-Path $output 'WotAssetsTests.swf')) 'tests/WotAssetsTests.as'
    if ($LASTEXITCODE -ne 0) { throw 'Wot test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WotAssetsTests-app.xml') -Destination $output -Force
    $mode = if ($FullCompile) { 'full' } else { 'read' }
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $output 'WotAssetsTests-app.xml') -- (Resolve-Path -LiteralPath $RealAssets).Path (Join-Path $project 'src/config') $output $mode
    if ($LASTEXITCODE -ne 0) { throw "Wot tests failed. See $output/wot-result.txt" }
    Get-Content -LiteralPath (Join-Path $output 'wot-result.txt')
} finally { Pop-Location }
