[CmdletBinding()]
param([string]$ToolsRoot)
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
    & $taskJava ('-Dapplication.home=' + $taskSdk) -jar (Join-Path $taskSdk 'lib/mxmlc.jar') '+configname=air' '-debug=true' ('-output=' + (Join-Path $taskOutput 'GenerateNexaIcons.swf')) 'scripts/GenerateNexaIcons.as'
    if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'GenerateNexaIcons-app.xml') -Destination $taskOutput
    & (Join-Path $taskSdk 'bin/adl.exe') -nodebug (Join-Path $taskOutput 'GenerateNexaIcons-app.xml') -- (Join-Path $taskProject 'assets/nexa/app')
    if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
} finally { Pop-Location }
