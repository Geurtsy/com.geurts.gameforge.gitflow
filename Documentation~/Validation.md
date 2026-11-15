# GitFlow 0.11.0 validation

[Ownership acceptance](Evidence/0.11.0/QueueAcceptance.log): five new real-Git cases and four existing fencing controls passed. They cover failed fetch readback without duplicate push, changed owners, the original verification deadline, same-service fresh-lease recovery after two failed reads/access loss, original request/version retention, changed-index preservation, uncertain writes, deadline fencing and legacy/unknown-owner refusal. Lease observation time is advanced only in disposable tests; production timing remains unchanged. Native C#/examples and dependencies are unchanged except the version literal; [comparison](Evidence/0.11.0/NativeEvidenceReuse.json) retains prior native proof. No production fault injection or physical multi-machine soak is claimed.


[Folder metadata acceptance](Evidence/0.10.0/QueueAcceptance.log): nine focused host/real-Git cases cover native-failure recovery, provenance tampering/collisions/unknown files, opt-out, version/gate failures, model-free end-to-end revalidation, safe reporting and preserved documentation/session behavior. The [real Unity failure smoke](Evidence/0.10.0/NativeFailureSmoke.json) recovered fourteen Unity-created folder sidecars while retaining the genuine CS1029 compiler failure and unchanged version. It does not claim successful compilation of the deliberately failing fixture. [Native implementation comparison](Evidence/0.10.0/NativeEvidenceReuse.json) binds unchanged C#/examples to their prior proof.


[Documentation queue acceptance](Evidence/0.9.0/QueueAcceptance.log) covers truthful non-applicability, mixed warning failure, native version-only metadata, conservative paths/modes, portable tamper/recovery and existing publication/failure/reuse behaviour. The [actual GDD classification](Evidence/0.9.0/RealGddClassification.json) recognizes all 46 changed documentation paths plus bundle version in 3.146 seconds on the recorded candidate; this is classification time, not total publication time.


[Native session acceptance](Evidence/0.8.1/SessionAcceptance.log): four real-Git/Windows cases cover clean-filter/CRLF/Unicode/batch-boundary parity, concurrent capture preservation, actual child timeout cleanup, and staged/index/deleted-file preservation. Original raw bytes, native Git filter behavior and zero-warning acceptance remain required.

[Matched performance comparison](Evidence/0.8.1/SpeedComparison.json): 213 original files have identical manifests; every one of 107 assemblies and 6317 unique compiler inputs remains included with identical compiler arguments and zero diagnostics. Original capture took 81.62s versus 4.82s; Compatibility 0.5.0 qualification took 106.73s versus 314.12s for 0.4.2. This is one nonconcurrent sample, affected by host load and filesystem warmth, not a whole-request duration guarantee.

[Native evidence comparison](Evidence/0.9.0/NativeEvidenceReuse.json) verifies C#/assembly/example behavior unchanged except the version literal. Dependencies remain unchanged. Existing native compilation/example evidence retains its original scope. No player build or visual checks.

## Retained recovery evidence

[0.8.0 recovery acceptance](Evidence/0.8.0/RecoveryAcceptance.log) retains twelve focused executions across eleven distinct real-Git/Windows cases. Lease, publication and repair policy implementations are unchanged by this performance update.

[Reasoning latency sample](Evidence/0.8.0/ReasoningLatency.json) retains the separately measured High review of 145.53s versus Max at 385.65s. No new model invocation or repair-budget reset occurred in this update.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{
  "packageId": "com.geurts.gameforge.gitflow",
  "packageVersion": "0.11.0",
  "dependencies": {
    "com.geurts.gameforge.god": "0.44.4",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  }
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
