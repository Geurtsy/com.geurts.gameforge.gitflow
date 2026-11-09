[CmdletBinding()]
param([Parameter(Mandatory)][string]$Python,[Parameter(Mandatory)][string]$PackageSource,[Parameter(Mandatory)][string]$Record,[Parameter(Mandatory)][string]$Repository,[Parameter(Mandatory)][string]$HostState,[Parameter(Mandatory)][string]$Output,[ValidateSet("resume","start","request-stop")][string]$Operation="resume")
$ErrorActionPreference='Stop'
$actualSource=Get-Content -Raw -LiteralPath (Join-Path $PackageSource 'Editor/GitFlowProcess.cs')
$compiled=Add-Type -TypeDefinition ($actualSource+"`nnamespace Geurts.GameForge.GitFlow.Editor { internal static class GitFlowInspection { internal const int MaximumJsonBytes=1048576; } }") -CompilerOptions '/warn:4','/warnaserror' -PassThru
$ownerType=$compiled|Where-Object FullName -eq 'Geurts.GameForge.GitFlow.Editor.GitFlowProcess'
$method=$ownerType.GetMethod('RunExecutable',[Reflection.BindingFlags]'Static,Public,NonPublic')
$tool=Join-Path $PackageSource 'Tools~/Queue/GitFlowExistingRunner.py'
$values=@($tool,'request-host','--repository',$Repository,'--record',$Record,'--host-state',$HostState,'--operation',$Operation,'--operator')
foreach($value in $values){if($value.Contains('"')){throw 'Unexpected quote in fixture argument; preserved'}}
$start=[Diagnostics.ProcessStartInfo]::new();$start.WorkingDirectory=$PackageSource;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.Arguments=($values|ForEach-Object{'"'+$_+'"'}) -join ' '
$began=[DateTime]::UtcNow.ToString('o')
$result=$method.Invoke($null,@($start,$Python,[Threading.CancellationToken]::None))
[pscustomobject]@{driverPid=$PID;startedUtc=$began;returnedUtc=[DateTime]::UtcNow.ToString('o');transport='Actual GitFlowProcess.RunExecutable with KILL_ON_JOB_CLOSE; unchanged source compiled with max-json-constant-only fixture stub';result=($result|ConvertFrom-Json)}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $Output -Encoding utf8
