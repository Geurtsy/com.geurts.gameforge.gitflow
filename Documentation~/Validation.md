# GitFlow 0.8.1 validation

[Native session acceptance](Evidence/0.8.1/SessionAcceptance.log): four real-Git/Windows cases cover clean-filter/CRLF/Unicode/batch-boundary parity, concurrent capture preservation, actual child timeout cleanup, and staged/index/deleted-file preservation. Original raw bytes, native Git filter behavior and zero-warning acceptance remain required.

[Matched performance comparison](Evidence/0.8.1/SpeedComparison.json): 213 original files have identical manifests; every one of 107 assemblies and 6317 unique compiler inputs remains included with identical compiler arguments and zero diagnostics. Original capture took 81.62s versus 4.82s; Compatibility 0.5.0 qualification took 106.73s versus 314.12s for 0.4.2. This is one nonconcurrent sample, affected by host load and filesystem warmth, not a whole-request duration guarantee.

[Native evidence comparison](Evidence/0.8.1/NativeEvidenceReuse.json) verifies C#/assembly/example behavior unchanged except the version literal. Dependencies remain unchanged. Existing native compilation/example evidence retains its original scope. No player build or visual checks.

## Retained recovery evidence

[0.8.0 recovery acceptance](Evidence/0.8.0/RecoveryAcceptance.log) retains twelve focused executions across eleven distinct real-Git/Windows cases. Lease, publication and repair policy implementations are unchanged by this performance update.

[Reasoning latency sample](Evidence/0.8.0/ReasoningLatency.json) retains the separately measured High review of 145.53s versus Max at 385.65s. No new model invocation or repair-budget reset occurred in this update.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{
  "packageId": "com.geurts.gameforge.gitflow",
  "packageVersion": "0.8.1",
  "dependencies": {
    "com.geurts.gameforge.god": "0.44.4",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  }
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
