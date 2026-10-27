[CmdletBinding()]
param([string]$Output,[string]$ToolsRoot,[switch]$BuildOnly)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$tools=if($ToolsRoot){(Resolve-Path -LiteralPath $ToolsRoot).Path}else{Join-Path (Split-Path -Parent $project) 'ObjectBuilder-tools'}
if(-not $Output){$Output=Join-Path $project 'bin-debug/mcp-gateway'}
$previous=@{}
foreach($name in @('DOTNET_CLI_HOME','NUGET_PACKAGES','DOTNET_CLI_TELEMETRY_OPTOUT','TEMP','TMP')){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
foreach($folder in @('dotnet-home','nuget','tmp')){New-Item -ItemType Directory -Path (Join-Path $tools $folder) -Force | Out-Null}
$env:DOTNET_CLI_HOME=Join-Path $tools 'dotnet-home'
$env:NUGET_PACKAGES=Join-Path $tools 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:TEMP=Join-Path $tools 'tmp';$env:TMP=$env:TEMP
if($BuildOnly){
    & dotnet build (Join-Path $PSScriptRoot 'NexaAsset.Mcp.csproj') --nologo -p:RestoreLockedMode=true
}else{
    & dotnet publish (Join-Path $PSScriptRoot 'NexaAsset.Mcp.csproj') -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o $Output --nologo
}
} finally {
    foreach($name in $previous.Keys){
        if([string]::IsNullOrEmpty($previous[$name])){Remove-Item -LiteralPath ('Env:'+$name) -ErrorAction SilentlyContinue}
        else{[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    }
}
if($LASTEXITCODE -ne 0){throw 'MCP gateway build failed.'}
if(-not $BuildOnly){
    foreach($notice in @('MCP-SDK-LICENSE.txt','DOTNET-EXTENSIONS-LICENSE.txt','THIRD-PARTY-NOTICES.txt')){
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $notice) -Destination (Join-Path $Output $notice)
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../modern-helper/THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $Output 'MODERN-HELPER-NOTICES.txt')
    $packages=Join-Path $tools 'nuget'
    $deps=Get-Content -LiteralPath (Join-Path $Output 'nexa-mcp-gateway.deps.json') -Raw | ConvertFrom-Json
    foreach($runtime in @($deps.libraries.PSObject.Properties | Where-Object Name -Like 'runtimepack.*')){
        $parts=$runtime.Name.Split('/')
        $package=$parts[0].Substring('runtimepack.'.Length).ToLowerInvariant()
        foreach($notice in @('LICENSE.TXT','THIRD-PARTY-NOTICES.TXT')){
            $source=Join-Path $packages ($package+'/'+$parts[1]+'/'+$notice)
            if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination (Join-Path $Output ($package+'-'+$notice))}
        }
    }
}
