param(
    [string]$FixturePath = 'C:/gfe68',
    [string]$UnityEditor = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe',
    [string]$TestedCommit = '1b8282e1f231ffa3e420244966581bd005656d56',
    [ValidateRange(60,1800)][int]$TimeoutSeconds = 1200
)
# User-invoked fallback only. Agent execution of the native test launch is currently blocked.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Hash-Bytes([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Bytes)).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Assert-PlainPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if ($resolved.Length -ge 240) { throw 'Use the existing short disposable fixture path.' }
    for ($item = $resolved; $item; $item = [IO.Path]::GetDirectoryName($item)) {
        if ((Test-Path -LiteralPath $item) -and ((Get-Item -LiteralPath $item -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse path refused: $item" }
    }
    return $resolved
}
function Same-Json($First, $Second) {
    return ($First | ConvertTo-Json -Depth 100 -Compress) -ceq ($Second | ConvertTo-Json -Depth 100 -Compress)
}
$FixturePath = Assert-PlainPath $FixturePath
$UnityEditor = Assert-PlainPath $UnityEditor
if (-not (Test-Path -LiteralPath (Join-Path $FixturePath 'ForgePrivateCompatibilityValidation.marker') -PathType Leaf)) { throw 'The selected project is not the marked disposable validation fixture.' }
if (-not (Test-Path -LiteralPath $UnityEditor -PathType Leaf)) { throw 'The selected Unity Editor is unavailable.' }
if ($TestedCommit -cnotmatch '^[0-9a-f]{40}$') { throw 'Select an exact immutable candidate commit.' }
if ((Get-Content -LiteralPath (Join-Path $FixturePath 'ProjectSettings/ProjectVersion.txt') -Raw) -notmatch 'm_EditorVersion: 6000\.6\.3f1') { throw 'This handoff qualifies Unity6000.6.3f1 only.' }
$busy = @(Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object { $_.CommandLine -and $_.CommandLine.Replace('\','/').IndexOf($FixturePath.Replace('\','/'), [StringComparison]::OrdinalIgnoreCase) -ge 0 })
if ($busy.Count) { throw 'The disposable fixture already has an Editor process. Do not interrupt it.' }
$manifestPath = Join-Path $FixturePath 'Packages/manifest.json'
$lockPath = Join-Path $FixturePath 'Packages/packages-lock.json'
$originalManifest = [IO.File]::ReadAllBytes($manifestPath)
$originalLock = [IO.File]::ReadAllBytes($lockPath)
$manifest = [Text.Encoding]::UTF8.GetString($originalManifest) | ConvertFrom-Json
$lockBefore = [Text.Encoding]::UTF8.GetString($originalLock) | ConvertFrom-Json
$id = 'com.geurts.gameforge.gitflow'
$source = "https://github.com/Geurtsy/com.geurts.gameforge.gitflow.git#$TestedCommit"
$manifest.dependencies | Add-Member -NotePropertyName $id -NotePropertyValue $source -Force
if ('testables' -notin $manifest.PSObject.Properties.Name) { $manifest | Add-Member -NotePropertyName testables -NotePropertyValue @($id) }
elseif ($id -notin $manifest.testables) { $manifest.testables = @($manifest.testables) + $id }
$prepared = [Text.UTF8Encoding]::new($false).GetBytes(($manifest | ConvertTo-Json -Depth 100) + [Environment]::NewLine)
$output = Join-Path $FixturePath ('Evidence/GitFlowAuth-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssffffZ'))
New-Item -ItemType Directory -Path $output | Out-Null
$guard = [IO.File]::Open((Join-Path $FixturePath 'Evidence/GitFlowAuth-validation.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$unity = $null
$receipt = [ordered]@{ candidate=$TestedCommit; fixture=$FixturePath; expectedTests=13; nativeAccepted=$false; restored=$false; desktopControl=$false; output=$output }
try {
    [IO.File]::WriteAllBytes((Join-Path $output 'manifest.before.json'), $originalManifest)
    [IO.File]::WriteAllBytes((Join-Path $output 'packages-lock.before.json'), $originalLock)
    if ((Hash-Bytes ([IO.File]::ReadAllBytes($manifestPath))) -cne (Hash-Bytes $originalManifest) -or (Hash-Bytes ([IO.File]::ReadAllBytes($lockPath))) -cne (Hash-Bytes $originalLock)) { throw 'Fixture changed before candidate selection; no overwrite performed.' }
    [IO.File]::WriteAllBytes($manifestPath, $prepared)
    $arguments = @('-batchmode','-nographics','-noaudio','-projectPath', ('"' + $FixturePath + '"'), '-runTests','-testPlatform','EditMode','-testFilter','Geurts.GameForge.GitFlow.Tests.GitFlowInspectionTests','-testResults',('"' + (Join-Path $output 'Results.xml') + '"'),'-logFile',('"' + (Join-Path $output 'Editor.log') + '"'))
    $unity = Start-Process -FilePath $UnityEditor -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $receipt.pid = $unity.Id
    if (-not $unity.WaitForExit($TimeoutSeconds * 1000)) { throw 'Native test budget expired. Process was not killed; retain fixture/backup and inspect the owned PID before restoration.' }
    $receipt.exitCode = $unity.ExitCode
    [IO.File]::WriteAllBytes((Join-Path $output 'manifest.tested.json'), [IO.File]::ReadAllBytes($manifestPath))
    [IO.File]::WriteAllBytes((Join-Path $output 'packages-lock.tested.json'), [IO.File]::ReadAllBytes($lockPath))
    $reportPath = Join-Path $output 'Results.xml'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw 'Native runner did not produce its expected report. Inspect Editor.log.' }
    [xml]$report = [IO.File]::ReadAllText($reportPath)
    $run = $report.'test-run'
    $receipt.nativeAccepted = $unity.ExitCode -eq 0 -and $run.result -eq 'Passed' -and [int]$run.passed -eq 13 -and [int]$run.failed -eq 0 -and [int]$run.skipped -eq 0
    $receipt.passed = [int]$run.passed; $receipt.failed = [int]$run.failed; $receipt.skipped = [int]$run.skipped
}
catch { $receipt.error = $_.Exception.Message }
finally {
    try {
        if ($null -ne $unity -and -not $unity.HasExited) { throw 'Owned Unity process remains alive; no package files restored under it.' }
        if ((Hash-Bytes ([IO.File]::ReadAllBytes($manifestPath))) -cne (Hash-Bytes $prepared)) { throw 'Manifest changed outside this test. Backups retained; concurrent edits were not overwritten.' }
        $currentLockBytes = [IO.File]::ReadAllBytes($lockPath)
        $lockAfter = [Text.Encoding]::UTF8.GetString($currentLockBytes) | ConvertFrom-Json
        foreach ($property in $lockBefore.dependencies.PSObject.Properties) {
            if ($property.Name -ne $id -and ($property.Name -notin $lockAfter.dependencies.PSObject.Properties.Name -or -not (Same-Json $property.Value $lockAfter.dependencies.($property.Name)))) { throw "Unrelated resolved dependency changed: $($property.Name). Backups retained for review." }
        }
        foreach ($name in $lockAfter.dependencies.PSObject.Properties.Name) { if ($name -ne $id -and $name -notin $lockBefore.dependencies.PSObject.Properties.Name) { throw 'Unexpected new dependency; backups retained for review.' } }
        if ((Hash-Bytes ([IO.File]::ReadAllBytes($lockPath))) -cne (Hash-Bytes $currentLockBytes)) { throw 'Lockfile changed during restoration check; no overwrite performed.' }
        [IO.File]::WriteAllBytes($manifestPath, $originalManifest)
        [IO.File]::WriteAllBytes($lockPath, $originalLock)
        $receipt.restored = (Hash-Bytes ([IO.File]::ReadAllBytes($manifestPath))) -ceq (Hash-Bytes $originalManifest) -and (Hash-Bytes ([IO.File]::ReadAllBytes($lockPath))) -ceq (Hash-Bytes $originalLock)
    }
    catch { $receipt.restorationProblem = $_.Exception.Message }
    [IO.File]::WriteAllText((Join-Path $output 'Receipt.json'), ($receipt | ConvertTo-Json -Depth 20) + [Environment]::NewLine)
    $guard.Dispose()
}
$receipt | ConvertTo-Json -Depth 20
if (-not $receipt.nativeAccepted -or -not $receipt.restored) { exit 1 }
