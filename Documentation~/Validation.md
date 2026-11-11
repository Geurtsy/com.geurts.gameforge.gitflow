# GitFlow 0.5.0 validation

The changed publisher setup and Queue Panel are under candidate validation. This release uses the published 0.3.0 main as its base; the separate 0.4.0 candidate is preserved. No live game, installed package or production publisher is changed by development checks.

## Changed behavior and checks

Automatic defaults are read-only until the user's combined configure/start confirmation. Existing policy identity, required gates, repair budget, workspace ownership and legacy refusal stay with the existing host. New defaults select the exact installed project Editor and every Edit Mode test. Queue collection visits all advertised pages; each row has a visible status, while failed or partial inspection cannot claim an empty queue. Local Windows process creation times distinguish running and stale/reused PIDs. Live reads are explicitly opted in and view-owned.

Focused disposable checks cover exact Editor/default selection, policy preservation/stale-runner refusal, real Windows process lifetime, all-item rendering and complete-empty versus partial states. Native compilation and source-bound acceptance reports will be recorded after the final candidate is committed. Previous release reports remain [historical](History/Validation-0.3.0-release.md).

## Limits and delivery

Native visual/physical interaction, light/dark skins, narrow docking and display scaling remain unverified for this change. The 0.3.0 visual waiver was specific to that release and is not a new visual pass. No computer control, live project changes, model inference, legacy migration or live publisher start was performed. Consumer updates remain explicit and separate from source publication.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{"packageId":"com.geurts.gameforge.gitflow","packageVersion":"0.5.0","dependencies":{"com.geurts.gameforge.god":"0.44.4","com.unity.nuget.newtonsoft-json":"3.2.2"}}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
