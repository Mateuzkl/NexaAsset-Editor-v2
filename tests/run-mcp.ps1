#Requires -Version 7.0
[CmdletBinding()]
param([switch]$CoreOnly)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$tools=Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk=Join-Path $tools 'flex-air'
$output=Join-Path $project ('bin-tests/studio-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
Push-Location -LiteralPath $project
try {
    & (Join-Path $project 'tools/mcp-gateway/build.ps1')
    & dotnet run --project tools/mcp-gateway-tests/NexaAsset.Mcp.Tests.csproj -- $output
    if($LASTEXITCODE -ne 0){throw 'Studio core/MCP tests failed.'}
    if($CoreOnly){return}
    $jdk=@(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
    if($jdk.Count -ne 1){throw 'Expected one portable JDK for AIR integration tests.'}
    & (Join-Path $project 'tools/modern-helper/build.ps1')
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home='+$sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
        ('-output='+ (Join-Path $project 'bin-tests/StudioMainTests.swf')) 'tests/StudioMainTests.mxml'
    if($LASTEXITCODE -ne 0){throw 'Studio AIR test compilation failed.'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StudioMainTests-app.xml') -Destination (Join-Path $project 'bin-tests')
    Copy-Item -LiteralPath (Join-Path $project 'src/config') -Destination (Join-Path $project 'bin-tests') -Recurse -Force
    $startInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $sdk 'bin/adl.exe'))
    $startInfo.UseShellExecute=$false
    $startInfo.CreateNoWindow=$true
    foreach($argument in @('-nodebug',(Join-Path $project 'bin-tests/StudioMainTests-app.xml'),'--',$output,
        (Join-Path $project 'bin-debug/mcp-gateway/nexa-mcp-gateway.exe'),(Join-Path $project 'bin-debug/modern-helper/nexa-modern-helper.exe'),
        (Join-Path $project 'tools/mcp-gateway-tests/bin/Debug/net10.0/NexaAsset.Mcp.Tests.exe'))){$startInfo.ArgumentList.Add($argument)}
    $air=[Diagnostics.Process]::Start($startInfo)
    try {
        $watch=[Diagnostics.Stopwatch]::StartNew();$peakAir=0L;$peakChildren=0L;$samples=0
        while(-not $air.WaitForExit(1000)){
            if($watch.Elapsed.TotalSeconds -gt 240){$air.Kill($true);throw "Studio AIR watchdog exceeded 240s. See $output"}
            $air.Refresh();$peakAir=[Math]::Max($peakAir,$air.WorkingSet64);$samples++
            $childBytes=0L
            foreach($child in @(Get-CimInstance Win32_Process -Filter ("ParentProcessId="+$air.Id))){
                $owned=Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
                if($owned){$childBytes+=$owned.WorkingSet64}
            }
            $peakChildren=[Math]::Max($peakChildren,$childBytes)
        }
        @{samples=$samples;peakAirWorkingSet=$peakAir;peakChildWorkingSetSum=$peakChildren;elapsedSeconds=$watch.Elapsed.TotalSeconds}|ConvertTo-Json|Set-Content (Join-Path $output 'memory.json') -Encoding utf8
        if($air.ExitCode -ne 0){throw "Studio live AIR/MCP tests failed. See $output"}
    }finally{$air.Dispose()}
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home='+$sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
        ('-output='+ (Join-Path $project 'bin-tests/StudioClosingTests.swf')) 'tests/StudioClosingTests.mxml'
    if($LASTEXITCODE -ne 0){throw 'Studio closing test compilation failed.'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'StudioClosingTests-app.xml') -Destination (Join-Path $project 'bin-tests')
    foreach($format in @('modern','legacy')) { foreach($scenario in @('empty','untouched','discard','save')) {
        $closeInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $sdk 'bin/adl.exe'));$closeInfo.UseShellExecute=$false;$closeInfo.CreateNoWindow=$true
        foreach($argument in @('-nodebug',(Join-Path $project 'bin-tests/StudioClosingTests-app.xml'),'--',$output,
            (Join-Path $project 'bin-debug/mcp-gateway/nexa-mcp-gateway.exe'),(Join-Path $project 'bin-debug/modern-helper/nexa-modern-helper.exe'),$format,$scenario)){$closeInfo.ArgumentList.Add($argument)}
        $closeTest=[Diagnostics.Process]::Start($closeInfo)
        try {$closeWatch=[Diagnostics.Stopwatch]::StartNew();while(-not $closeTest.WaitForExit(1000)){if($closeWatch.Elapsed.TotalSeconds -gt 80){$closeTest.Kill($true);throw "Studio closing watchdog: $format/$scenario. See $output"}};if($closeTest.ExitCode -ne 0){throw "Studio closing failed: $format/$scenario. See $output"}}
        finally{$closeTest.Dispose()}
        $closeResult=Join-Path $output "closing-$format-$scenario.txt"
        if(-not (Test-Path -LiteralPath $closeResult) -or -not (Select-String -LiteralPath $closeResult -SimpleMatch 'actual application X accepted')){throw "Studio closing did not pass: $format/$scenario. See $output"}
    }}
    Write-Output "Studio results: $output"
}finally{Pop-Location}
