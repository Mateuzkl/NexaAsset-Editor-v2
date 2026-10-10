[CmdletBinding()]
param([string]$ToolsRoot, [string]$RealAssets)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools' }
$sdk = Join-Path $ToolsRoot 'flex-air'
$jdk = @(Get-ChildItem -LiteralPath (Join-Path $ToolsRoot 'java') -Directory)
if ($jdk.Count -ne 1) { throw 'Expected one portable JDK.' }
$java = Join-Path $jdk[0].FullName 'bin/java.exe'
$output = Join-Path $project 'bin-tests'
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location -LiteralPath $project
try {
    & $java -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-library-path+=libs' '-locale=en_US' '-source-path+=locale/{locale}' `
        '-debug=true' ('-output=' + (Join-Path $output 'AssetIoTests.swf')) 'tests/AssetIoTests.as'
    if ($LASTEXITCODE -ne 0) { throw 'Asset test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AssetIoTests-app.xml') -Destination $output
    $runRoot = Join-Path $output ('run-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runRoot | Out-Null
    Write-Output "Test output: $runRoot"
    $arguments = @('-nodebug', (Join-Path $output 'AssetIoTests-app.xml'), '--', $runRoot)
    if ($RealAssets) { $arguments += @((Resolve-Path -LiteralPath $RealAssets).Path, (Join-Path $project 'src/config')) }
    & (Join-Path $sdk 'bin/adl.exe') @arguments
    if ($LASTEXITCODE -ne 0) { throw "Asset tests failed. See $runRoot/result.txt" }
    Get-Content -LiteralPath (Join-Path $runRoot 'result.txt')
    & $java -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-library-path+=libs' '-locale=en_US' '-source-path+=locale/{locale}' `
        '-debug=true' ('-output=' + (Join-Path $output 'ProgressLayoutTests.swf')) 'tests/ProgressLayoutTests.mxml'
    if ($LASTEXITCODE -ne 0) { throw 'Progress layout test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'ProgressLayoutTests-app.xml') -Destination $output
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $output 'ProgressLayoutTests-app.xml')
    if ($LASTEXITCODE -ne 0) { throw 'Progress layout tests failed.' }
    & $java -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' `
        '-debug=true' ('-output=' + (Join-Path $output 'AssetWindowTests.swf')) 'tests/AssetWindowTests.mxml'
    if ($LASTEXITCODE -ne 0) { throw 'Asset window test compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AssetWindowTests-app.xml') -Destination $output
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $output 'AssetWindowTests-app.xml')
    if ($LASTEXITCODE -ne 0) { throw 'Asset window tests failed.' }
} finally { Pop-Location }
