param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$IntermediateDirectory,
    [switch]$WriteReleaseManifest
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
foreach ($path in @($OutputDirectory,$IntermediateDirectory)) {
    $full = [IO.Path]::GetFullPath($path)
    if ($full.Length + 130 -ge 260) { throw "Runner output/intermediate reserve exceeds 259 characters: $full plus 130 characters." }
    if ($full.StartsWith($RepositoryRoot + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Build outside the owning source tree to preserve its inventory/fingerprint.' }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'; $env:DOTNET_NOLOGO='1'
& dotnet publish (Join-Path $PSScriptRoot 'Runner/GitFlow.Runner.csproj') --configuration Release --no-self-contained --output $OutputDirectory "-p:BaseIntermediateOutputPath=$IntermediateDirectory\" "-p:BaseOutputPath=$IntermediateDirectory\bin\" "-p:PathMap=$RepositoryRoot=/_/" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Independent Windows runner publication failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runner/THIRD-PARTY-NOTICES.txt') -Destination $OutputDirectory
$files = [ordered]@{}
foreach ($file in Get-ChildItem -LiteralPath $OutputDirectory -File | Where-Object { $_.Name -ne 'RunnerRelease.json' } | Sort-Object Name) { $files[$file.Name] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
foreach ($name in @('GeurtsGitFlow.exe','GeurtsGitFlow.dll','GeurtsGitFlow.deps.json','GeurtsGitFlow.runtimeconfig.json','Newtonsoft.Json.dll','THIRD-PARTY-NOTICES.txt')) { if (-not $files.Contains($name)) { throw "Runner artifact missing: $name" } }
$sourceEntries = foreach($file in Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File -Filter '*.cs' | Where-Object { $_.FullName -notmatch '[\\/]\.git[\\/]' } | Sort-Object { $_.FullName.Substring($RepositoryRoot.Length).Replace('\','/') }) {
    $relative=$file.FullName.Substring($RepositoryRoot.Length+1).Replace('\','/')
    $bytes=[Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($file.FullName).Replace("`r`n","`n").Replace("`r","`n"))
    $hasher=[Security.Cryptography.SHA256]::Create()
    try { $hash=[BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
    "${relative}:$hash"
}
$hasher=[Security.Cryptography.SHA256]::Create()
try { $fingerprint=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($sourceEntries -join "`n")))).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
$manifest = [ordered]@{schema=1;version='0.2.0';protocol=1;target='win-x64';framework='net10.0';sdk=(& dotnet --version);sourceFingerprint=$fingerprint;files=$files}
$json = ($manifest | ConvertTo-Json -Depth 15).Replace("`r`n","`n").Replace("`n","`r`n") + "`r`n"
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'RunnerRelease.json'),$json,[Text.UTF8Encoding]::new($false))
if ($WriteReleaseManifest) { [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'RunnerRelease.json'),$json,[Text.UTF8Encoding]::new($false)) }
[pscustomobject]@{Output=$OutputDirectory;Version=$manifest.version;Files=$files.Count;ManifestWritten=[bool]$WriteReleaseManifest} | ConvertTo-Json -Compress
