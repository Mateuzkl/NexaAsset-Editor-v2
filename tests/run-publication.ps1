[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$tools = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk = Join-Path $tools 'flex-air'
$jdk = @(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
if ($jdk.Count -ne 1) { throw 'Expected exactly one portable JDK.' }
$output = Join-Path $project 'bin-tests/publication'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location -LiteralPath $project
try {
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx1g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-debug=true' '-warnings=false' `
        ('-output=' + (Join-Path $output 'AssetPublicationTests.swf')) 'tests/AssetPublicationTests.as'
    if ($LASTEXITCODE -ne 0) { throw 'Publication test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AssetPublicationTests-app.xml') -Destination $output -Force
    $run = Join-Path $output ('run-' + [guid]::NewGuid().ToString('N'))
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $output 'AssetPublicationTests-app.xml') -- $run
    if ($LASTEXITCODE -ne 0) { throw "Publication tests failed. See $run/publication-result.txt" }
    Get-Content -LiteralPath (Join-Path $run 'publication-result.txt')
} finally { Pop-Location }
