param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent), [string]$EvidenceDirectory, [string]$RunnerDirectory)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$fingerprint=& (Join-Path $PSScriptRoot 'Get-PublisherFingerprint.ps1') -RepositoryRoot $RepositoryRoot
if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) { $EvidenceDirectory=Join-Path ([IO.Path]::GetTempPath()) ('gf_evidence_'+[guid]::NewGuid().ToString('N')) }
$buildRoot=Join-Path ([IO.Path]::GetTempPath()) ('gf_build_'+[guid]::NewGuid().ToString('N'))
foreach($path in @($buildRoot,$EvidenceDirectory)) { if ([IO.Path]::GetFullPath($path).Length+140 -ge 260) { throw "Acceptance path reserve exceeds 259 characters: $path plus 140 characters." } }
New-Item -ItemType Directory -Path $buildRoot,$EvidenceDirectory -Force | Out-Null
$env:DOTNET_DISABLE_GUI_ERRORS='1'; $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'
if ([string]::IsNullOrWhiteSpace($RunnerDirectory)) {
    $RunnerDirectory=Join-Path $buildRoot 'runner'
    & (Join-Path $PSScriptRoot 'Build-Runner.ps1') -RepositoryRoot $RepositoryRoot -OutputDirectory $RunnerDirectory -IntermediateDirectory (Join-Path $buildRoot 'runner-obj')
}
& dotnet build (Join-Path $PSScriptRoot 'Runner/GitFlow.Acceptance.csproj') --configuration Release --output (Join-Path $buildRoot 'acceptance') "-p:BaseIntermediateOutputPath=$(Join-Path $buildRoot 'acceptance-obj')\" -p:RestoreLockedMode=true --nologo
if($LASTEXITCODE -ne 0) { throw 'Publisher acceptance compilation failed.' }
$env:GEURTS_GITFLOW_FIXTURE_RUNNER_DIR=[IO.Path]::GetFullPath($RunnerDirectory)
$report=Join-Path $EvidenceDirectory ('PublisherAcceptance-'+[guid]::NewGuid().ToString('N')+'.json')
& (Join-Path $buildRoot 'acceptance/GeurtsGitFlow.Acceptance.exe') launch $report
if($LASTEXITCODE -ne 0) { throw 'Detached acceptance launcher failed.' }
$deadline=[DateTime]::UtcNow.AddMinutes(10)
while(-not(Test-Path -LiteralPath $report)) { if([DateTime]::UtcNow -ge $deadline) { throw "Acceptance observation timed out; preserve the same operation/root, do not launch a duplicate. Expected report: $report" }; Start-Sleep -Seconds 5 }
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
if($result.failed -ne 0 -or $result.passed -ne 10 -or -not $result.detached_parent_no_console) { throw "Publisher acceptance failed. Preserve report $report" }
if($fingerprint -cne (& (Join-Path $PSScriptRoot 'Get-PublisherFingerprint.ps1') -RepositoryRoot $RepositoryRoot)) { throw "Publisher source/recipe changed during acceptance. Original report retained without rebinding: $report" }
$binding=[ordered]@{schema=1;packageVersion='0.2.0';coreFingerprint=$fingerprint;report=$report;reportSha256=(Get-FileHash -LiteralPath $report -Algorithm SHA256).Hash.ToLowerInvariant();passed=$result.passed;failed=$result.failed;sdk=(& dotnet --version);detachedParentNoConsole=$result.detached_parent_no_console}
$bindingPath=Join-Path $EvidenceDirectory ('PublisherValidation-'+[guid]::NewGuid().ToString('N')+'.json')
[IO.File]::WriteAllText($bindingPath,($binding | ConvertTo-Json -Depth 10).Replace("`r`n","`n").Replace("`n","`r`n"),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Report=$report;Binding=$bindingPath;Passed=$result.passed;Failed=$result.failed;BuildRoot=$buildRoot} | ConvertTo-Json -Compress
