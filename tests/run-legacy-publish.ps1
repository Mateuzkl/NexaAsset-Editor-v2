[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WotRoot)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$tools = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk = Join-Path $tools 'flex-air'
$jdk = @(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
if ($jdk.Count -ne 1) { throw 'Expected exactly one portable JDK.' }
$output = Join-Path $project 'bin-tests/legacy-publish'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location -LiteralPath $project
try {
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
        ('-output=' + (Join-Path $output 'LegacyPublishTests.swf')) 'tests/LegacyPublishTests.mxml'
    if ($LASTEXITCODE -ne 0) { throw 'Legacy integration compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyPublishTests-app.xml') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $project 'src/config') -Destination $output -Recurse -Force
    $run = Join-Path $output ('run-' + [guid]::NewGuid().ToString('N'))
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $output 'LegacyPublishTests-app.xml') -- $run (Resolve-Path -LiteralPath $WotRoot).Path
    if ($LASTEXITCODE -ne 0) { throw "Legacy integration failed. See $run/legacy-publish-result.txt" }
    Get-Content -LiteralPath (Join-Path $run 'legacy-publish-result.txt')
} finally { Pop-Location }
