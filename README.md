# Geurts Game Forge GitFlow

Version: 0.2.0. Editor-only queue inspection, submit-once handoff and a separately released independent Windows publisher for generic Git repositories. The project declares a plain SemVer file, protected controllers and required validation commands. Consumer installation and runner adoption remain manual.

Open **Tools > Geurts Game Forge > GitFlow**, or the embedded tool in God. Inspect is deliberately read-only. Submit returns the original queue ID without starting or waiting for a processor. Publisher provides no-code policy review/save, dedicated-root configuration, trusted runner launch, graceful stop and finite original-request continuation. All paths use the same [supported services](Documentation~/AIUsability.md).

God 0.39.3 is the only mandatory Geurts brick. Import licensed Odin Inspector and Quantum Console separately. Newtonsoft JSON 3.2.2 supplies Editor parsing. Unity 6000.6.3f1 is the minimum. The separately extracted Windows runner requires .NET 10 runtime and includes Newtonsoft.Json 13.0.4 under its retained MIT notice. Use the authoritative Commandments catalogue for the immutable package target; source publication does not update any consumer or running runner.

[Publisher Help and contracts](Documentation~/Publisher.md) describe setup, strict gates, lease timings, versions, receipts, recovery and limitations. This adapter requires an atomic non-force direct-main Git workflow. Projects requiring PR approval, immutable game release assets, additional version stamps or native derived identities need their own adapter; those requirements cannot be silently waived.

Inspection reads legacy codex/queue and native geurts/gitflow records without locks or remote writes. Refresh is bounded to 128 requests, one MiB per object/output, 45 seconds and a two-second cooldown. Results retain timestamps, incomplete information and actionable failures. PID presence and lease audit dates never establish current ownership or abandonment. Native receipt identity/tree/retained-stream hashes are checked, while required check semantics and current-main ancestry remain outside inspection.

See [validation evidence and unverified surfaces](Documentation~/Validation.md). Tests use disposable repositories and a silent isolated Editor. No player build or Dreadvault processor/runner change is included.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{"packageId":"com.geurts.gameforge.gitflow","packageVersion":"0.2.0","dependencies":{"com.geurts.gameforge.god":"0.39.3","com.unity.nuget.newtonsoft-json":"3.2.2"}}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
