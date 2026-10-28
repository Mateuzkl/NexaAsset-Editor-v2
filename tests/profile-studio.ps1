#Requires -Version 7.0
[CmdletBinding()]
param([string]$Baseline='bd0ab04847a9fabb8ff4403ee7f8ebd9e31ca88d')
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$tools=Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk=Join-Path $tools 'flex-air'
$jdk=@(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
Push-Location -LiteralPath $project
try {
    $reference=(& git show ($Baseline+':src/ob/studio/PixelCanvas.as')) -join "`n"
    if($LASTEXITCODE -ne 0){throw 'Unable to load the requested benchmark baseline.'}
    $reference=$reference.Replace('class PixelCanvas','class BaselinePixelCanvas').Replace('function PixelCanvas','function BaselinePixelCanvas')
    $baselineFolder=Join-Path $project 'bin-tests/pr6-baseline/ob/studio'
    New-Item -ItemType Directory -Path $baselineFolder -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $baselineFolder 'BaselinePixelCanvas.as'),$reference,[Text.UTF8Encoding]::new($false))
    $descriptor=(Get-Content (Join-Path $PSScriptRoot 'StudioMainTests-app.xml') -Raw).Replace('StudioMainTests','StudioCanvasTests')
    Set-Content (Join-Path $project 'bin-tests/StudioCanvasTests-app.xml') $descriptor -Encoding utf8
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home='+$sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-source-path+=bin-tests/pr6-baseline' '-source-path+=tests' '-library-path+=libs' '-locale=en_US' '-source-path+=locale/{locale}' '-warnings=false' `
        '-output=bin-tests/StudioCanvasTests.swf' tests/StudioCanvasTests.mxml
    if($LASTEXITCODE -ne 0){throw 'Canvas benchmark compilation failed.'}
    $startInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $sdk 'bin/adl.exe'))
    $startInfo.UseShellExecute=$false;$startInfo.CreateNoWindow=$true
    foreach($argument in @('-nodebug',(Join-Path $project 'bin-tests/StudioCanvasTests-app.xml'))){$startInfo.ArgumentList.Add($argument)}
    $air=[Diagnostics.Process]::Start($startInfo)
    try {if(-not $air.WaitForExit(120000)){$air.Kill($true);throw 'Canvas benchmark exceeded 120s.'};if($air.ExitCode -ne 0){throw 'Canvas benchmark failed.'}}finally{$air.Dispose()}
    $result=Get-Content (Join-Path $project 'bin-tests/canvas-benchmark.json') -Raw | ConvertFrom-Json
    if(-not $result.passed){throw $result.error}
    $result.results | Format-Table kind,size,layers,redraws,ms,memoryDelta
}finally{Pop-Location}
