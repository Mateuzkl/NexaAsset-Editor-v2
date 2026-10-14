[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Assets)
$ErrorActionPreference='Stop'
$project=Split-Path -Parent $PSScriptRoot
$source=(Resolve-Path -LiteralPath $Assets).Path
$output=Join-Path $project ('bin-tests/native-interruption-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
$copy=Join-Path $output 'assets-copy'
if(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}){throw 'Test input must not contain links.'}
Copy-Item -LiteralPath $source -Destination $copy -Recurse
$session=Join-Path $output 'session';New-Item -ItemType Directory -Path $session | Out-Null
$head=Join-Path $copy 'catalog-content.json';$before=(Get-FileHash -LiteralPath $head -Algorithm SHA256).Hash
$process=[Diagnostics.Process]::new()
$launched=$false
$process.StartInfo.FileName=Join-Path $project 'bin-debug/modern-helper/nexa-modern-helper.exe'
$process.StartInfo.UseShellExecute=$false;$process.StartInfo.CreateNoWindow=$true
$process.StartInfo.RedirectStandardInput=$true;$process.StartInfo.RedirectStandardOutput=$true;$process.StartInfo.RedirectStandardError=$true
foreach($argument in @('--input',$copy,'--output',$session)){$process.StartInfo.ArgumentList.Add($argument)}
function Read-HelperLine {
    $line=$process.StandardOutput.ReadLineAsync()
    if(-not $line.Wait(20000)){throw 'Helper timed out in interruption test.'}
    if($null -eq $line.Result){throw 'Helper exited before expected checkpoint.'}
    $value=$line.Result|ConvertFrom-Json
    if($value.type -eq 'error'){throw ('Native helper error: '+$value.message)}
    return $value
}
try {
    $launched=$process.Start()
    do{$message=Read-HelperLine}while($message.type -ne 'ready')
    $process.StandardInput.WriteLine('{"command":"native-save","requestId":"1","generation":1}')
    do{$message=Read-HelperLine}while($message.type -ne 'progress' -or $message.phase -ne 'Writing native definitions')
    $process.Kill($true);$process.WaitForExit()
    if((Get-FileHash -LiteralPath $head -Algorithm SHA256).Hash -ne $before){throw 'Forced interruption changed the catalog head.'}
    if(-not (Get-ChildItem -LiteralPath $copy -Directory -Force | Where-Object Name -Like '.nexa-save-*.partial')){throw 'Expected owned interrupted stage.'}
    Write-Output "PASS: helper forcibly terminated during native Save; previous catalog is unchanged. Owned incomplete staging remains unreferenced: $copy"
}finally{if($launched -and -not $process.HasExited){$process.Kill($true);$process.WaitForExit()};$process.Dispose()}
