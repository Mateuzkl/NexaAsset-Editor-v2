[CmdletBinding()]
param([string]$ToolsRoot, [string]$RealAssets)
$ErrorActionPreference = 'Stop'
$taskProject = Split-Path -Parent $PSScriptRoot
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $taskProject) 'ObjectBuilder-tools' }
$taskSdk = Join-Path $ToolsRoot 'flex-air'
$taskJdk = @(Get-ChildItem -LiteralPath (Join-Path $ToolsRoot 'java') -Directory)
if ($taskJdk.Count -ne 1) { throw 'Expected one portable JDK.' }
$taskJava = Join-Path $taskJdk[0].FullName 'bin/java.exe'
$taskOutput = Join-Path $taskProject 'bin-tests'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location -LiteralPath $taskProject
try {
    & $taskJava -Xmx2g ('-Dapplication.home=' + $taskSdk) -jar (Join-Path $taskSdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' `
        '-debug=true' '-warnings=false' ('-output=' + (Join-Path $taskOutput 'NexaLayoutTests.swf')) 'tests/NexaLayoutTests.mxml'
    if ($LASTEXITCODE -ne 0) { throw 'Nexa layout compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NexaLayoutTests-app.xml') -Destination $taskOutput
    # The real application reads these resources relative to its application directory.
    Copy-Item -LiteralPath (Join-Path $taskProject 'src/config') -Destination $taskOutput -Recurse -Force
    $taskArgs = @('-nodebug', (Join-Path $taskOutput 'NexaLayoutTests-app.xml'))
    if ($RealAssets) { $taskArgs += @('--', (Resolve-Path -LiteralPath $RealAssets).Path) }
    & (Join-Path $taskSdk 'bin/adl.exe') @taskArgs
    if ($LASTEXITCODE -ne 0) { throw 'Nexa layout tests failed.' }
} finally { Pop-Location }
