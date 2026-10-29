# GitFlow 0.2.0 candidate validation

This candidate is not published. Required zero-warning compilation is blocked with stock Quantum Console 2.6.7 on Unity 6000.6.3f1. Passing assertions do not waive that release gate. No Dreadvault queue or consumer installation is changed.

## Tested source and environment

The current code candidate is `bea6d4ec343450989da40bf352f6d0ef0f11ee2f`. The executable-source fingerprint is `a88cb8be1790c5bc2f75b09aeedd806ca6af31ce50d79e8d00a5d098a88231fb`; later documentation-only commits do not change it. Tests use the Git-resolved package in the disposable project `C:/gqv`, Unity `6000.6.3f1` revision `45d8eee7de74`, God `0.39.4` at `b7a001234dd5b149cc767dd1ab305df4c6e876d7`, Odin `4.0.2.4`, Quantum Console `2.6.7`, Newtonsoft JSON `3.2.2`, Input System `1.20.0`, UGUI `2.6.0` and the actually resolved Test Framework `1.8.0`. Separately licensed vendor sources remain private.

## Acceptance evidence

| Check | Result and limits |
| --- | --- |
| Native Unity acceptance | 10 passed, zero failed/skipped. [Raw XML](Evidence/0.2.0/UnityAcceptance.xml) includes an actual serialized Inspector change from 3 to 4, native saving, isolated capture/submission, original branch/index/working-file preservation, excluded dirty assets, and refusal to save a selected scene when that could clear unrelated dirty asset state. The supported inspection example compiled in the fixture. |
| Real-Git queue acceptance | 17 passed in 472.952 seconds. [Raw report](Evidence/0.2.0/QueueAcceptance.log) covers idempotence/ancestry, no early publication or skipping, dirty checkout preservation, no duplicate version or retest during publication recovery, workspace/cache reuse, capture exclusions and concurrent-change refusal, three-hour repair and durable stall counting, evidence provenance, owner fencing and explicit stopped-runner adoption. |
| GitHub arbitration | [Remote report](Evidence/0.2.0/RemoteLeaseAcceptance.json): two independent clones contend for temporary refs, exactly one wins, renewal resets abandonment observation, a successor takes over after the simulated interval, and a stale owner's atomic write changes no target ref. Successor publication and release succeed. Main remains unchanged and all task-created temporary refs were removed. This tests actual GitHub writes with a simulated elapsed clock, not a 30-minute soak or separate physical machines. |
| Stock full compiler gate | Failed: 11 warnings, zero errors, from the complete log of a requested full script rebuild. [Exact diagnostics and vendor identity](Evidence/0.2.0/VendorCompatibilityAssessment.json): six mandatory core, four Suggestors, one Extras, zero Demo warnings. No suppression or vendor modification was used. Zero per-assembly diagnostic callbacks did not override the complete-log failure. |
| Native visual interaction | [Scoped report and screenshots](Evidence/0.2.0/NativeVisualAcceptance.json): physical clicks open, scroll, close and reopen Help inside Unity; queue, capture scope, model, operator and optional repair controls render. Narrow-window resizing is not verified. No publisher, model inference or update was started. The report binds both the earlier interaction screenshots and the final candidate menu, controls and updated-text recheck. |
| Local Codex metadata | Installed CLI `0.149.1` returned signed-in account state and supported model/effort metadata through its app-server API. No credential store was read and no paid inference was run. This does not establish live repair quality or model availability on another account. |
| Disk use | [Measured synthetic benchmark](Evidence/0.2.0/StorageBenchmark.json): after three submissions, retained full clones total 67,422,945 unique logical bytes; one shared worktree totals 23,293,896. Library fixtures total 50,331,648 versus 16,777,216 bytes. The fixture uses a 2 MiB source, 16 MiB cache and 64 KiB evidence per request. This is not allocated-cluster usage, a production forecast or achieved Dreadvault savings. Queue tests also verify worktree/cache identity reuse. |
| Documentation impact | [Cross-repository review](Evidence/0.2.0/DocumentationImpact.json) records 87 declared documentation/metadata files across 12 other released owners at the reviewed catalogue pins. GitFlow guides are updated here. The separately owned God automatic-update guidance is recorded as an external owner action. |

## Repair authority

Automatic repair defaults to disabled. The operator must deliberately enable its separate authorization control when configuring a publisher; choosing a model or saving preferences does not authorize source changes. The implementation allows up to 10,800 seconds from the first durable attempt and never restarts that clock after a restart, candidate correction or model/runner change.

Three consecutive unsuccessful proposals or unchanged independent validation outcomes stop the repair early. A changed independent failure resets only that stall counter. Every attempt and raw proposal is preserved; later proposals receive recent failure feedback. Tests cover empty proposals, whitespace-only changes, actual source edits with the same failure, durable restart/model changes, and progress followed by further attempts. Individual process timeouts and uncertain interrupted outcomes remain bounded and fail closed.

Repair may apply exact source fragments to complete eligible task-owned C#/Python files. It cannot change validation/tests, queue authority, protected native settings, package policy, generated or vendor files. It cannot skip checks, publish independently, take over a live owner, or reset its budget. No live repository has been configured to use automatic repair by this task.

## Release boundary

The mandatory static and native-evidence release checks must both pass before main, tag or catalogue publication. The stock vendor compiler failure is preserved as evidence. A separately approved private compatibility experiment is owned by the manual-update task; any clean result from that derivative must be identified separately and must not be called stock Quantum Console compatibility or permission to install/publish commercial sources.

Remaining scope limits include narrow-window native interaction, live paid-model repair quality, a real elapsed 30-minute ownership soak, multiple physical machines and project-specific native identity adapters. The historical 0.1.2 reports remain separate and do not establish this candidate.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{
  "dependencies": {
    "com.geurts.gameforge.god": "0.39.3",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  },
  "packageId": "com.geurts.gameforge.gitflow",
  "packageVersion": "0.2.0"
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
