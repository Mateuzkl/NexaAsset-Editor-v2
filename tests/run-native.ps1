[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$RealAssets)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$tools=Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk=Join-Path $tools 'flex-air'
$jdk=@(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
if($jdk.Count -ne 1){throw 'Expected exactly one portable JDK in ObjectBuilder-tools/java.'}
$output=Join-Path $project ('bin-tests/native-'+[Guid]::NewGuid().ToString('N'))
$sourceAssets=(Resolve-Path -LiteralPath $RealAssets).Path
New-Item -ItemType Directory -Path $output | Out-Null
if(Get-ChildItem -LiteralPath $sourceAssets -Recurse -Force | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}){throw 'Native test inputs must not contain links or junctions.'}
$testCopy=Join-Path $output 'modern-source-copy'
Copy-Item -LiteralPath $sourceAssets -Destination $testCopy -Recurse
Write-Output "Native main output: $output"
Write-Output "Native original assets (read-only test input): $sourceAssets"
Write-Output "Native Save test copy: $testCopy"
Push-Location -LiteralPath $project
try {
    & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home='+$sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
        '-source-path=src' '-library-path+=libs' '-locale=en_US,es_ES,pt_BR' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
        ('-output='+ (Join-Path $project 'bin-tests/NativeMainTests.swf')) 'tests/NativeMainTests.mxml'
    if($LASTEXITCODE -ne 0){throw 'Native main test compilation failed.'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NativeMainTests-app.xml') -Destination (Join-Path $project 'bin-tests')
    Copy-Item -LiteralPath (Join-Path $project 'src/config') -Destination (Join-Path $project 'bin-tests') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $project 'bin-debug/modern-helper') -Destination (Join-Path $project 'bin-tests') -Recurse -Force
    & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $project 'bin-tests/NativeMainTests-app.xml') -- `
        $testCopy (Join-Path $project 'bin-debug/modern-helper/nexa-modern-helper.exe') $output
    if($LASTEXITCODE -ne 0){throw 'Native main integration failed.'}
}finally{Pop-Location}
