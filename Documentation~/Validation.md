# GitFlow 0.7.0 validation

Thirteen focused real-Git/Windows checks pass in [ProjectRepairAcceptance.log](Evidence/0.7.0/ProjectRepairAcceptance.log). Coverage includes broad project artifacts, files larger than the former limits, more than sixteen edits, whole-file/binary/create/delete/rename changes, protected controllers/gates, rejected independent review, concurrent candidate preservation, original repair deadlines, interrupted states, retries beyond three stalls, uncapped diagnostics/model transport, durable deployment and integration through unchanged required checks. Superseded narrow-scope/size-cutoff tests are replaced by these behavior/preservation checks; legacy conflict-configuration compatibility remains covered separately.

A real local Codex gpt-5.6-sol/max [workspace smoke test](Evidence/0.7.0/LocalCodexWorkspaceSmoke.json) edits one disposable function with file/shell tools, verifies its established contract, and passes a separate read-only review with the original preserved. Its first invocation exposed a missing per-invocation Windows sandbox setting; the corrected invocation explicitly enables the existing elevated Windows sandbox rather than inheriting read-only fallback. No global settings changed. This is not whole-game or production repair certification.

[Native evidence reuse](Evidence/0.7.0/NativeEvidenceReuse.json) compares every C#/assembly-definition file with published 0.6.1. All behavior, dependencies and supported examples are unchanged; only the version literal changes. Exact 0.5.3 compilation/qualification and four native presentation checks remain tied to their original source/environment in AIUsabilityValidation.json. New Python behavior is covered separately above. No visual checks are performed or required.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{"packageId":"com.geurts.gameforge.gitflow","packageVersion":"0.7.0","dependencies":{"com.geurts.gameforge.god":"0.44.4","com.unity.nuget.newtonsoft-json":"3.2.2"}}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
