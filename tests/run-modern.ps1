[CmdletBinding()]
param([string]$RealAssets, [switch]$AllSheets, [switch]$Ui, [switch]$Stress, [switch]$Convert, [switch]$ConvertAll)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$tools = Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'
$sdk = Join-Path $tools 'flex-air'
$previous = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'TEMP', 'TMP')) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
Push-Location -LiteralPath $project
try {
    $env:DOTNET_CLI_HOME = Join-Path $tools 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $tools 'nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:TEMP = Join-Path $tools 'tmp'; $env:TMP = $env:TEMP
    $output = Join-Path $project ('bin-tests/modern-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $output | Out-Null
    Write-Output "Modern test output: $output"
    $arguments = @('run', '--project', 'tools/modern-helper-tests/NexaModern.Tests.csproj', '--', $output)
    if ($RealAssets) { $arguments += (Resolve-Path -LiteralPath $RealAssets).Path }
    if (-not $AllSheets) { $arguments += '--quick' }
    if ($Stress) { $arguments += '--stress' }
    if ($Convert -or $ConvertAll) { $arguments += '--convert' }
    if ($ConvertAll) { $arguments += '--convert-all' }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Modern helper tests failed.' }
    if ($Convert -or $ConvertAll) {
        $jdk = @(Get-ChildItem -LiteralPath (Join-Path $tools 'java') -Directory)
        if ($jdk.Count -ne 1) { throw 'Expected one portable JDK.' }
        & (Join-Path $jdk[0].FullName 'bin/java.exe') -Xmx2g ('-Dapplication.home=' + $sdk) -jar (Join-Path $sdk 'lib/mxmlc.jar') '+configname=air' `
            '-source-path=src' '-source-path+=tests' '-library-path+=libs' '-locale=en_US' '-source-path+=locale/{locale}' '-debug=true' '-warnings=false' `
            ('-output=' + (Join-Path $project 'bin-tests/LegacyConversionTests.swf')) 'tests/LegacyConversionTests.as'
        if ($LASTEXITCODE -ne 0) { throw 'Legacy conversion oracle compilation failed.' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LegacyConversionTests-app.xml') -Destination (Join-Path $project 'bin-tests')
        & (Join-Path $sdk 'bin/adl.exe') -nodebug (Join-Path $project 'bin-tests/LegacyConversionTests-app.xml') -- $output (Join-Path $project 'src/config/versions.xml')
        if ($LASTEXITCODE -ne 0) { throw 'Legacy conversion oracle failed.' }
    }
    if ($Ui) {
        if (-not $RealAssets) { throw 'Modern UI tests require the audited Crystal assets fixture.' }
        & (Join-Path $project 'tools/modern-helper/build.ps1')
        if ($LASTEXITCODE -ne 0) { throw 'Native helper build failed.' }
        & (Join-Path $PSScriptRoot 'run-native.ps1') -RealAssets $RealAssets
        if ($LASTEXITCODE -ne 0) { throw 'Native main integration failed.' }
    }
} finally {
    Pop-Location
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
