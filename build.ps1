[CmdletBinding()]
param(
    [string]$ProjectRoot,
    [string]$ToolsRoot,
    [string]$BundleName = 'NexaAsset Editor v2',
    [switch]$CompileOnly,
    [switch]$PackageOnly
)

$ErrorActionPreference = 'Stop'
if ($CompileOnly -and $PackageOnly) { throw 'CompileOnly and PackageOnly cannot be combined.' }
if (-not $ProjectRoot) { $ProjectRoot = $PSScriptRoot }
$ProjectRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
if (-not $ToolsRoot) { $ToolsRoot = Join-Path (Split-Path -Parent $ProjectRoot) 'ObjectBuilder-tools' }
$ToolsRoot = (Resolve-Path -LiteralPath $ToolsRoot).Path
$sdk = Join-Path $ToolsRoot 'flex-air'
$javaDirectory = @(Get-ChildItem -LiteralPath (Join-Path $ToolsRoot 'java') -Directory)
if ($javaDirectory.Count -ne 1) { throw 'Expected exactly one portable JDK in ObjectBuilder-tools/java.' }
$java = Join-Path $javaDirectory[0].FullName 'bin/java.exe'
$compiler = Join-Path $ToolsRoot 'node/node_modules/asconfigc/asconfigc'
$node = (Get-Command node.exe -ErrorAction Stop).Source
$tempDirectory = Join-Path $ToolsRoot 'tmp'
foreach ($required in @($java, $compiler, (Join-Path $sdk 'lib/adt.jar'), (Join-Path $ProjectRoot 'asconfig.json'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing build dependency: $required" }
}
New-Item -ItemType Directory -Path $tempDirectory -Force | Out-Null
$previousEnvironment = @{}
$certificate = $null
foreach ($name in @('JAVA_HOME', 'TEMP', 'TMP')) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

Push-Location -LiteralPath $ProjectRoot
try {
    # Process-local settings only: no global PATH changes or C: SDK installation.
    $env:JAVA_HOME = $javaDirectory[0].FullName
    $env:TEMP = $tempDirectory
    $env:TMP = $tempDirectory
    if (-not $PackageOnly) {
        & $node $compiler --sdk $sdk --debug=false --jvmargs=-Xmx2g
        if ($LASTEXITCODE -ne 0) { throw "ActionScript compilation failed: $LASTEXITCODE" }
    }
    if ($CompileOnly) { return }

    $stage = Join-Path $ProjectRoot 'bin-debug'
    $descriptor = Join-Path $stage 'ObjectBuilder-app.xml'
    $compiledSwf = 'NexaAsset-Editor-v2.swf'
    foreach ($required in @($descriptor, (Join-Path $stage $compiledSwf))) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing compiled output: $required" }
    }
    if ([IO.Path]::GetFileName($BundleName) -ne $BundleName -or $BundleName -in @('.', '..')) { throw 'BundleName must be a directory name, not a path.' }
    $bundle = Join-Path (Join-Path $ProjectRoot 'bin') $BundleName
    New-Item -ItemType Directory -Path (Split-Path -Parent $bundle) -Force | Out-Null
    # Native modern asset decoding runs out of process and has its own
    # portable .NET runtime. No third-party editor binary is redistributed.
    & (Join-Path $ProjectRoot 'tools/modern-helper/build.ps1') -Output (Join-Path $stage 'modern-helper') -ToolsRoot $ToolsRoot
    & (Join-Path $ProjectRoot 'tools/mcp-gateway/build.ps1') -Output (Join-Path $stage 'mcp-gateway') -ToolsRoot $ToolsRoot
    # AIR requires a package certificate. This temporary self-signed certificate
    # is for local builds, not a trusted Windows publisher or production release.
    $certificatePath = Join-Path $tempDirectory ('objectbuilder-' + [Guid]::NewGuid().ToString('N') + '.p12')
    if (Test-Path -LiteralPath $certificatePath) { throw 'Temporary certificate path already exists.' }
    $certificate = $certificatePath
    $certificatePassword = [Guid]::NewGuid().ToString('N')
    & $java "-Djava.io.tmpdir=$tempDirectory" -jar (Join-Path $sdk 'lib/adt.jar') `
        -certificate -cn 'NexaAsset Editor Local Build' -validityPeriod 1 2048-RSA $certificate $certificatePassword
    if ($LASTEXITCODE -ne 0) { throw "Local certificate creation failed: $LASTEXITCODE" }
    & $java "-Djava.io.tmpdir=$tempDirectory" -jar (Join-Path $sdk 'lib/adt.jar') `
        -package -storetype pkcs12 -keystore $certificate -storepass $certificatePassword -tsa none `
        -target bundle -arch x64 $bundle $descriptor `
        -C $stage $compiledSwf `
        -C $stage modern-helper `
        -C $stage mcp-gateway `
        -C (Join-Path $ProjectRoot 'assets') icon `
        -C (Join-Path $ProjectRoot 'assets') nexa `
        -C $ProjectRoot LICENSE `
        -C (Join-Path $ProjectRoot 'src') config
    if ($LASTEXITCODE -ne 0) { throw "AIR bundle packaging failed: $LASTEXITCODE" }
    $executable = Join-Path $bundle 'NexaAsset Editor v2.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Packaging did not produce NexaAsset Editor v2.exe.' }
    Write-Output "Portable executable: $executable"
}
finally {
    try {
        if ($certificate -and (Test-Path -LiteralPath $certificate -PathType Leaf)) { Remove-Item -LiteralPath $certificate }
    }
    finally {
        foreach ($name in $previousEnvironment.Keys) {
            if ($null -eq $previousEnvironment[$name]) {
                Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue
            }
            else { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name], 'Process') }
        }
        Pop-Location
    }
}
