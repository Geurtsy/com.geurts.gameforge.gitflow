param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [switch]$RequireUnityEvidence)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$RepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
function Read-Json([string]$Relative) { return [IO.File]::ReadAllText((Join-Path $RepositoryRoot $Relative)) | ConvertFrom-Json }
function Require([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Test-UnityMinimum([string]$Current, [string]$Minimum) {
    $values = @()
    foreach ($text in @($Current, $Minimum)) {
        $match = [regex]::Match($text, '\A([0-9]+)\.([0-9]+)\.([0-9]+)([abcfp])([0-9]+)\z')
        $core = $null; $revision = 0
        if (-not $match.Success -or -not [version]::TryParse(($match.Groups[1].Value + '.' + $match.Groups[2].Value + '.' + $match.Groups[3].Value), [ref]$core) -or
            -not [int]::TryParse($match.Groups[5].Value, [ref]$revision)) { return $false }
        $values += @{Core=$core; Channel='abcfp'.IndexOf($match.Groups[4].Value); Revision=$revision}
    }
    if ($values[0].Core -ne $values[1].Core) { return $values[0].Core -gt $values[1].Core }
    if ($values[0].Channel -ne $values[1].Channel) { return $values[0].Channel -gt $values[1].Channel }
    return $values[0].Revision -ge $values[1].Revision
}
function Resolve-Owned([string]$Relative) {
    Require (-not [IO.Path]::IsPathRooted($Relative) -and $Relative -notmatch '(^|[\\/])\.\.([\\/]|$)') "Unsafe release path: $Relative"
    $path = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot $Relative))
    Require ($path.StartsWith($RepositoryRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) "Path escapes owner: $Relative"
    return $path
}
$package = Read-Json 'package.json'
$capability = Read-Json 'ForgeCapabilities.json'
$documents = Read-Json 'ReleaseDocumentation.json'
Require ($capability.schemaVersion -eq '1.0.0' -and $documents.schemaVersion -eq '1.0.0') 'Unsupported release metadata schema.'
Require ($capability.packageId -eq $package.name -and $documents.packageId -eq $package.name) 'Release package identity mismatch.'
Require ($capability.packageVersion -eq $package.version -and $documents.packageVersion -eq $package.version) 'Stale release capability/documentation version.'
Require ([version]$package.version -gt [version]$documents.previousVersion) 'The release must exceed its previous published version.'
$ids = @{}
foreach ($operation in $capability.operations) {
    Require ($operation.id -cmatch '^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$' -and -not $ids.ContainsKey($operation.id)) "Invalid/duplicate operation: $($operation.id)"
    $ids[$operation.id] = $true
    $source = Resolve-Owned $operation.source
    Require (Test-Path -LiteralPath $source -PathType Leaf) "Missing operation source: $($operation.source)"
    $code = [IO.File]::ReadAllText($source)
    Require ($code.Contains($operation.symbol)) "Unsupported operation symbol: $($operation.symbol)"
    if ($operation.id -ne 'inspect_sources') { Require ($code.Contains('"' + $operation.id + '"')) "Operation ID absent from its owner source: $($operation.id)" }
}
$features = @{}
foreach ($feature in $capability.features) {
    Require ($feature.id -cmatch '^[a-z][a-z0-9]*(?:_[a-z0-9]+)*$' -and -not $features.ContainsKey($feature.id)) "Invalid/duplicate feature: $($feature.id)"
    $features[$feature.id] = $true
    Require ($feature.provider -in @('editor','runtime','none') -and -not [string]::IsNullOrWhiteSpace($feature.maturity)) "Invalid feature provider/maturity: $($feature.id)"
    Require ($feature.implemented -or @($feature.operations).Count -eq 0) "Planned feature advertises operations: $($feature.id)"
    Require (@($feature.operations | Select-Object -Unique).Count -eq @($feature.operations).Count) "Repeated feature operation reference: $($feature.id)"
    Require (-not [string]::IsNullOrWhiteSpace($feature.description)) "Feature description missing: $($feature.id)"
    foreach ($id in $feature.operations) { Require ($ids.ContainsKey($id)) "Unknown supported operation: $id" }
    Require (@($feature.validation).Count -gt 0) "Feature validation scope missing: $($feature.id)"
}
$expectedDependencies = @{}
foreach ($property in $package.dependencies.PSObject.Properties) { $expectedDependencies[$property.Name] = [string]$property.Value }
foreach ($dependency in $capability.prerequisites | Where-Object { $_.kind -eq 'package' -and $_.required }) {
    Require ($expectedDependencies.ContainsKey($dependency.id) -and $expectedDependencies[$dependency.id] -eq $dependency.minimumVersion) "Dependency declaration mismatch: $($dependency.id)"
}
foreach ($id in $expectedDependencies.Keys) {
    Require (@($capability.prerequisites | Where-Object { $_.id -eq $id -and $_.kind -eq 'package' -and $_.required -and $_.minimumVersion -eq $expectedDependencies[$id] }).Count -eq 1) "Dependency omitted from capabilities: $id"
}
if ($package.name -notin @('com.geurts.gameforge.god','com.geurts.gameforge.bigbang','com.geurts.gameforge.documentation')) {
    Require ($expectedDependencies.ContainsKey('com.geurts.gameforge.god')) 'God dependency missing.'
    Require (@($expectedDependencies.Keys | Where-Object { $_ -like 'com.geurts.*' -and $_ -ne 'com.geurts.gameforge.god' }).Count -eq 0) 'A peer integration became mandatory.'
}
if ($package.name -in @('com.geurts.gameforge.bigbang','com.geurts.gameforge.documentation')) { Require (-not $expectedDependencies.ContainsKey('com.geurts.gameforge.god')) 'Independent adapter/installer acquired a God dependency.' }
$current = @($documents.currentDocuments)
$history = @($documents.historicalDocuments)
Require (@($current | Where-Object { $_ -in $history }).Count -eq 0) 'Current and historical documentation overlap.'
foreach ($relative in $current) {
    $path = Resolve-Owned $relative
    Require (Test-Path -LiteralPath $path -PathType Leaf) "Missing current guide: $relative"
    $text = [IO.File]::ReadAllText($path)
    Require ($text -match '\A\s*#\s+\S[^\r\n]*') "Current guide lacks a readable top-level heading: $relative"
    $blocks = [regex]::Matches($text, '(?s)<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->\s*```json\s*(?<json>.*?)\s*```\s*<!-- GEURTS-RELEASE-CONTEXT:END -->')
    Require ($blocks.Count -eq 1) "Current guide lacks one release context: $relative"
    $context = $blocks[0].Groups['json'].Value | ConvertFrom-Json
    Require ($context.packageId -eq $package.name -and $context.packageVersion -eq $package.version) "Stale current guide: $relative"
    $claims = @{}; foreach ($property in $context.dependencies.PSObject.Properties) { $claims[$property.Name] = [string]$property.Value }
    Require ($claims.Count -eq $expectedDependencies.Count) "Current guide dependency count mismatch: $relative"
    foreach ($id in $expectedDependencies.Keys) { Require ($claims.ContainsKey($id) -and $claims[$id] -eq $expectedDependencies[$id]) "Current guide dependency mismatch: $relative / $id" }
    # Current prose may state dependency minimums, but old introductions and explicitly historical sections retain their original identities.
    $prose = [regex]::Split($text, '(?im)^#{1,6}\s+(?:change\s*log|release history|version history)\b')[0]
    Require ($prose -notmatch 'https://github\.com/Geurtsy/(?:com\.geurts\.gameforge\.[a-z]+|GeurtsGameForgeBigBang)\.git#v\d+\.\d+\.\d+') "Current installation source must come from the catalogue: $relative"
    foreach ($claim in [regex]::Matches($prose, '(?m)^\*\*Version:?\*\*\s+(?<version>\d+\.\d+\.\d+)')) { Require ($claim.Groups['version'].Value -eq $package.version) "Stale current release banner: $relative" }
    foreach ($line in $prose -split "`r?`n") {
        if ($line -match '(?i)\b(?:historical|introduced|original|provenance|migration|minimum compatibility profile)\b' -or $line -notmatch '(?i)\b(?:requires?|depend(?:s|ency)?|install|use|manifest|minimum|mandatory)\b') { continue }
        foreach ($id in $expectedDependencies.Keys) {
            $alias = if ($id -eq 'com.geurts.gameforge.god') { '(?:God|com\.geurts\.gameforge\.god)' } else { [regex]::Escape($id) }
            foreach ($claim in [regex]::Matches($line, '(?i)' + $alias + '\*{0,2}\s+\*{0,2}(?<version>\d+\.\d+\.\d+)')) {
                Require ($claim.Groups['version'].Value -eq $expectedDependencies[$id]) "Stale prose dependency: $relative / $id / $($claim.Groups['version'].Value)"
            }
        }
    }
    foreach ($claim in [regex]::Matches($text, '(?m)^Release\s+(?<version>\d+\.\d+\.\d+)(?:\s|[.,:]|$)')) { Require ($claim.Groups['version'].Value -eq $package.version) "Stale current release prose: $relative" }
    foreach ($match in [regex]::Matches($text, '(?m)^# [^\r\n]*?(?<version>\d+\.\d+\.\d+)\s*$')) { Require ($match.Groups['version'].Value -eq $package.version) "Stale current guide heading: $relative" }
    foreach ($match in [regex]::Matches($text, '\]\((?<path>[^)]+)\)')) {
        $link = [Uri]::UnescapeDataString($match.Groups['path'].Value.Trim('<','>').Split('#')[0])
        if ($link -eq '' -or $link -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $link.StartsWith('/')) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path (Split-Path $path -Parent) $link))
        if ($resolved.StartsWith($RepositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Require (Test-Path -LiteralPath $resolved) "Broken current guide link: $relative -> $link" }
    }
}
Require ([IO.File]::ReadAllText((Resolve-Owned 'README.md')).Contains('(Documentation~/AIUsability.md)')) 'The package entry point must link supported AI usage.'
foreach ($relative in $history) { Require (Test-Path -LiteralPath (Resolve-Owned $relative) -PathType Leaf) "Missing explicitly classified historical document: $relative" }
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'Documentation~') -File -Filter '*.md') {
    $relative = 'Documentation~/' + $file.Name
    Require ($relative -in $current -or $relative -in $history) "Unclassified documentation: $relative"
}
foreach ($example in $documents.examples) {
    $path = Resolve-Owned $example.source
    Require ((Test-Path -LiteralPath $path -PathType Leaf) -and [IO.File]::ReadAllText($path).Contains($example.symbol)) "Missing supported example: $($example.source)"
    $sourceText = [IO.File]::ReadAllText($path).Replace("`r`n", "`n").TrimEnd()
    $found = $false
    foreach ($relative in $current) {
        $text = [IO.File]::ReadAllText((Resolve-Owned $relative)).Replace("`r`n", "`n")
        $sample = [regex]::Match($text, '(?s)<!-- GEURTS-EXAMPLE: ' + [regex]::Escape($example.source) + ' -->\s*```csharp\n(?<code>.*?)\n```')
        if ($sample.Success) { Require ($sample.Groups['code'].Value.TrimEnd() -ceq $sourceText) "Guide example differs from compile-checked source: $relative"; $found = $true }
    }
    Require $found "Supported example is absent from current guides: $($example.source)"
}
# Practical recipe declarations bind supported source to actual executable owner tests.
foreach ($recipe in @($documents.integrationRecipes)) {
    if ($null -eq $recipe) { continue }
    Require ($recipe.source -in @($documents.examples | ForEach-Object { $_.source })) "Integration recipe is not a declared checked example: $($recipe.source)"
    Require (Test-Path -LiteralPath (Resolve-Owned $recipe.testSource) -PathType Leaf) "Missing integration test source: $($recipe.testSource)"
    Require (@($recipe.tests).Count -gt 0) "Integration recipe lacks executable tests: $($recipe.source)"
    $testSource = [IO.File]::ReadAllText((Resolve-Owned $recipe.testSource))
    foreach ($testName in $recipe.tests) { Require ($testSource.Contains(($testName -split '\.')[-1])) "Unknown declared integration test: $testName" }
}
# Bind every executable C#/Python source and assembly definition; documentation-only commits do not invalidate tested code.
$sourceFiles = @(Get-ChildItem -LiteralPath $RepositoryRoot -Recurse -File | Where-Object { $_.Extension -in @('.cs', '.py', '.asmdef') -and $_.FullName -notmatch '[\\/](\.git|__pycache__)[\\/]' } | Sort-Object { $_.FullName.Substring($RepositoryRoot.Length).Replace('\','/') })
$entries = foreach ($file in $sourceFiles) {
    $relative = $file.FullName.Substring($RepositoryRoot.Length + 1).Replace('\','/')
    $content = [IO.File]::ReadAllText($file.FullName).Replace("`r`n", "`n").Replace("`r", "`n")
    $contentSha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($contentSha.ComputeHash([Text.Encoding]::UTF8.GetBytes($content))).Replace('-','').ToLowerInvariant() } finally { $contentSha.Dispose() }
    "$relative`:$hash"
}
$sha = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($entries -join "`n")))).Replace('-','').ToLowerInvariant() } finally { $sha.Dispose() }
if ($RequireUnityEvidence) {
    $evidence = Read-Json 'Documentation~/AIUsabilityValidation.json'
    Require ($evidence.packageVersion -eq $package.version -and $evidence.sourceFingerprint -eq $fingerprint -and
        (Test-UnityMinimum $evidence.unityVersion ($package.unity + '.' + $package.unityRelease))) 'Unity evidence does not match the current source/version or minimum Editor requirement.'
    Require ($evidence.failed -eq 0 -and $evidence.passed -gt 0 -and $evidence.examplesCompiled) 'Unity validation/examples did not pass.'
    $reportPath = Resolve-Owned $evidence.testResult
    Require (Test-Path -LiteralPath $reportPath -PathType Leaf) 'The source-bound acceptance report is unavailable.'
    Require ((Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $evidence.testResultSha256) 'The acceptance report checksum does not match its evidence.'
    [xml]$report = [IO.File]::ReadAllText($reportPath)
    $run = $report.'test-run'
    Require ($null -ne $run -and $run.result -eq 'Passed' -and [int]$run.failed -eq 0 -and [int]$run.skipped -eq 0 -and [int]$run.passed -eq $evidence.passed) 'Actual acceptance results disagree with the evidence or contain skipped/failed checks.'
    Require ($evidence.queueAcceptance.failed -eq 0 -and $evidence.queueAcceptance.passed -gt 0) 'Required real-Git queue acceptance is incomplete.'
    $queueReport = Resolve-Owned $evidence.queueAcceptance.report
    Require ((Get-FileHash -LiteralPath $queueReport -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $evidence.queueAcceptance.sha256) 'Queue acceptance report bytes differ.'
    Require ([IO.File]::ReadAllText($queueReport) -match '(?m)^OK\s*$') 'Queue acceptance did not complete successfully.'
    Require ($evidence.compilerGate.status -eq 'passed' -and $evidence.compilerGate.warnings -eq 0 -and $evidence.compilerGate.errors -eq 0) 'Required full compilation is blocked by compiler diagnostics; passing assertions do not waive this gate.'
    $compilerReport = Resolve-Owned $evidence.compilerGate.report
    Require ((Get-FileHash -LiteralPath $compilerReport -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $evidence.compilerGate.sha256) 'Compiler evidence bytes differ.'
    $compiler = [IO.File]::ReadAllText($compilerReport) | ConvertFrom-Json
    Require ($compiler.status -eq 'passed' -and $compiler.warnings -eq 0 -and $compiler.errors -eq 0 -and $compiler.full_script_rebuild) 'Native compilation evidence is not an accepted full rebuild.'
    Require ($evidence.nativeVisualAcceptance -eq 'verified') 'Rendered native controls and interaction remain unverified; explicit desktop-testing authorization is required.'
    foreach ($recipe in @($documents.integrationRecipes)) {
        if ($null -eq $recipe) { continue }
        $record = @($evidence.integrationRecipes | Where-Object { $_.source -eq $recipe.source })
        Require ($record.Count -eq 1 -and $record[0].compiled -and $record[0].failed -eq 0 -and $record[0].skipped -eq 0) "Integration recipe was not compiled and exercised successfully: $($recipe.source)"
        foreach ($testName in $recipe.tests) { Require ($testName -in @($record[0].passedTests)) "Integration recipe test was not executed: $testName" }
    }
    if ($evidence.PSObject.Properties.Name -contains 'publicationReady') { Require ([bool]$evidence.publicationReady) 'Required acceptance is still pending. See the candidate validation record before publishing.' }
}
[pscustomobject]@{ Package=$package.name; Version=$package.version; SourceFingerprint=$fingerprint; CurrentGuides=$current.Count; Operations=@($capability.operations).Count; UnityEvidenceRequired=[bool]$RequireUnityEvidence } | ConvertTo-Json -Compress
