[CmdletBinding()]
param([string]$ToolsRoot)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools' }
$ToolsRoot = (Resolve-Path -LiteralPath $ToolsRoot).Path
$sdk = Join-Path $ToolsRoot 'flex-air'
$jdk = @(Get-ChildItem -LiteralPath (Join-Path $ToolsRoot 'java') -Directory)
if ($jdk.Count -ne 1) { throw 'Expected one portable JDK.' }
Push-Location -LiteralPath $project
try {
    New-Item -ItemType Directory -Path bin-tests,workerswfs -Force | Out-Null
    & (Join-Path $project 'tools/mcp-gateway/build.ps1') -ToolsRoot $ToolsRoot
    & dotnet build tools/mcp-gateway-tests/NexaAsset.Mcp.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Recolor MCP client compilation failed.' }
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' `
        '-debug=true' '-warnings=false' '-output=workerswfs/ObjectBuilderWorker.swf' 'src/ObjectBuilderWorker.as'
    if ($LASTEXITCODE -ne 0) { throw 'Recolor worker compilation failed.' }
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' `
        '-debug=true' '-warnings=false' '-output=bin-tests/RecolorMainTests.swf' 'tests/RecolorMainTests.mxml'
    if ($LASTEXITCODE -ne 0) { throw 'Recolor test compilation failed.' }
    Copy-Item tests/RecolorMainTests-app.xml bin-tests/
    Copy-Item src/config bin-tests/ -Recurse -Force
    $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $sdk 'bin/adl.exe'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.ArgumentList.Add('-nodebug')
    $start.ArgumentList.Add((Join-Path $project 'bin-tests/RecolorMainTests-app.xml'))
    $air = [Diagnostics.Process]::Start($start)
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        while (-not $air.WaitForExit(1000)) {
            if ($watch.Elapsed.TotalSeconds -gt 120) {
                $air.Kill($true)
                throw 'Recolor watchdog: AIR stopped responding. See bin-tests/recolor-*.'
            }
        }
        if ($air.ExitCode -ne 0) { throw 'Recolor tests failed.' }
    }
    finally { $air.Dispose() }
}
finally { Pop-Location }
