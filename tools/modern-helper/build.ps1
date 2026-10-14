[CmdletBinding()]
param([string]$Output, [string]$ToolsRoot, [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tools = if ($ToolsRoot) { (Resolve-Path -LiteralPath $ToolsRoot).Path } else { Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools' }
if (-not $Output) { $Output = Join-Path $project 'bin-debug/modern-helper' }
$previous = @{}
foreach ($name in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_SKIP_FIRST_TIME_EXPERIENCE', 'TEMP', 'TMP')) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
try {
    $env:DOTNET_CLI_HOME = Join-Path $tools 'dotnet-home'
    $env:NUGET_PACKAGES = Join-Path $tools 'nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:TEMP = Join-Path $tools 'tmp'
    $env:TMP = $env:TEMP
    if ($BuildOnly) {
        & dotnet build (Join-Path $PSScriptRoot 'NexaModern.csproj') --nologo -p:RestoreLockedMode=true
        if ($LASTEXITCODE -ne 0) { throw 'Modern helper build failed.' }
    } else {
        # Portable runtime included: users do not need a .NET SDK or runtime.
        & dotnet publish (Join-Path $PSScriptRoot 'NexaModern.csproj') -c Release -r win-x64 `
            --self-contained true -p:PublishSingleFile=false -p:RestoreLockedMode=true -o $Output --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Modern helper publication failed.' }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $Output 'NEXA-THIRD-PARTY-NOTICES.txt')
        $deps = Get-Content -LiteralPath (Join-Path $Output 'nexa-modern-helper.deps.json') -Raw | ConvertFrom-Json
        $runtime = @($deps.libraries.PSObject.Properties | Where-Object Name -Like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*')
        if ($runtime.Count -ne 1) { throw 'Cannot resolve portable runtime license version.' }
        $version = $runtime[0].Name.Split('/')[-1]
        $runtimePackage = Join-Path $env:NUGET_PACKAGES ("microsoft.netcore.app.runtime.win-x64/$version")
        foreach ($notice in @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT')) {
            Copy-Item -LiteralPath (Join-Path $runtimePackage $notice) -Destination (Join-Path $Output ("DOTNET-$notice"))
        }
    }
} finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
