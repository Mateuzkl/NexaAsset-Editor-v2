[CmdletBinding()]
param([switch]$CoreOnly)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$tools=Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk=Join-Path $tools 'flex-air'
$jdk=@(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
$output=Join-Path $project ('bin-tests/studio-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location -LiteralPath $project
try {
    & (Join-Path $project 'tools/mcp-gateway/build.ps1')
    & dotnet run --project tools/mcp-gateway-tests/NexaAsset.Mcp.Tests.csproj -- $output
    if($LASTEXITCODE -ne 0){throw 'Studio core/MCP tests failed.'}
    if($CoreOnly){return}
    & (Join-Path $project 'tools/modern-helper/build.ps1')
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home='+$sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
        ('-output='+ (Join-Path $project 'bin-tests/StudioMainTests.swf')) 'tests/StudioMainTests.mxml'
    if($LASTEXITCODE -ne 0){throw 'Studio AIR test compilation failed.'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StudioMainTests-app.xml') -Destination (Join-Path $project 'bin-tests')
    Copy-Item -LiteralPath (Join-Path $project 'src/config') -Destination (Join-Path $project 'bin-tests') -Recurse -Force
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $project 'bin-tests/StudioMainTests-app.xml') -- $output `
        (Join-Path $project 'bin-debug/mcp-gateway/nexa-mcp-gateway.exe') (Join-Path $project 'bin-debug/modern-helper/nexa-modern-helper.exe') `
        (Join-Path $project 'tools/mcp-gateway-tests/bin/Debug/net10.0/NexaAsset.Mcp.Tests.exe')
    if($LASTEXITCODE -ne 0){throw "Studio live AIR/MCP tests failed. See $output"}
    Write-Output "Studio results: $output"
}finally{Pop-Location}
