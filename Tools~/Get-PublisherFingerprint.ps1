param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$RepositoryRoot=[IO.Path]::GetFullPath($RepositoryRoot)
$files=@(Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Editor/Publishing') -File -Filter '*.cs')
$files+=Get-Item -LiteralPath (Join-Path $RepositoryRoot 'Editor/GitFlowProcess.cs')
$files+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Runner') -File | Where-Object { $_.Extension -in @('.cs','.csproj') -or $_.Name -like '*.packages.lock.json' }
foreach($name in @('Build-Runner.ps1','Test-Publisher.ps1','Get-PublisherFingerprint.ps1')) { $files+=Get-Item -LiteralPath (Join-Path $PSScriptRoot $name) }
$entries=foreach($file in $files | Sort-Object { $_.FullName.Substring($RepositoryRoot.Length).Replace('\','/') }) {
    $relative=$file.FullName.Substring($RepositoryRoot.Length+1).Replace('\','/')
    $hasher=[Security.Cryptography.SHA256]::Create()
    try { $hash=[BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($file.FullName).Replace("`r`n","`n").Replace("`r","`n")))).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
    "${relative}:$hash"
}
$hasher=[Security.Cryptography.SHA256]::Create()
try { [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes(($entries -join "`n")))).Replace('-','').ToLowerInvariant() } finally { $hasher.Dispose() }
