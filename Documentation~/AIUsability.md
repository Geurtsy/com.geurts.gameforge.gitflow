# GitFlow Help and public API

## Read only for the task

After the current Commandments entry and manifest, select the row matching the task. Follow additional links only when that work needs them. Release evidence and history are optional during game integration.

| Task | Minimum package reading |
|---|---|
| Observe an existing queue | [Read this section](#help) |
| Extend read-only observation | [Read this section](#api-and-extension-boundary) |
| Interpret limits and failures | [Read this section](#observation-contract-and-common-problems) |

## Help

Prerequisites: an enabled GitFlow module under God, Unity 6000.6.3f1 or newer, Git for Windows, separately acquired licensed Odin Inspector and Quantum Console. The package is Editor-only. Ordinary use needs no code, JSON editing, processor setup or configuration saves.

1. Open Tools > Geurts Game Forge > GitFlow, or its installed-brick view in God.
2. Choose an existing local checkout. Enter the name of its existing remote, normally origin.
3. Select Refresh queue once. Inspect the completed timestamp, queue/processor/lease summaries and visible findings before expanding request/evidence sections.
4. Refresh explicitly when another observation is wanted. Requests within two seconds are refused. Concurrent identical API calls share the current task; another target is refused while it runs. Closing a view detaches that view; God owns the shared service and stopping the brick cancels its operation.

No observation runs on opening, restoration, repaint, section expansion or operation discovery. Scope fields are transient, not saved module configuration. Prior results keep their original timestamp while a refresh runs or fails; the selected scope of the result is shown inside its observation. A queued request is not delivery. Failed results include the queue's blocker and a route to its owning operator; the viewer cannot retry, release a lease, submit or publish.

## API and extension boundary

GitFlowBrick.Service is the shared lifecycle-owned GitFlowInspection, or null when disabled/unavailable. RefreshAsync(GitFlowTarget) runs one bounded request and returns an immutable GitFlowSnapshot. LastSnapshot exposes the last completed result without I/O. A caller may create and dispose an independent GitFlowInspection for explicitly isolated tooling; never create a publisher or acquire locks through an inspection adapter.

The God operation ID is inspect_queue, accepting a GitFlowTarget payload. Its Inspect callback is admission only, with no network work. ExecuteAsync refreshes through the same service. Verification refers only to completeness of this read, never processor readiness, current-main ancestry, validation execution or delivery correctness. Unsupported/malformed/partial evidence remains visible. Discovering the operation has no side effects.

The following exact example is compile-checked with the package:

<!-- GEURTS-EXAMPLE: Tools~/Examples/InspectionExample.cs -->
```csharp
using System.Threading.Tasks;
using Geurts.GameForge.GitFlow.Editor;

namespace Geurts.GameForge.GitFlow.Examples
{
    /// <summary>Explicit code path for the same read-only refresh used by the Editor controls.</summary>
    public static class InspectionExample
    {
        /// <summary>Reads once through the active lifecycle owner; the returned snapshot retains unavailable evidence.</summary>
        /// <param name="existingRepository">Existing local Git checkout to observe.</param>
        /// <returns>The shared immutable snapshot, or null if the module is disabled.</returns>
        public static async Task<GitFlowSnapshot> InspectOnce(string existingRepository)
        {
            GitFlowInspection service = GitFlowBrick.Service;
            return service == null ? null : await service.RefreshAsync(new GitFlowTarget(existingRepository));
        }
    }
}
```

## Observation contract and common problems

The adapter reads IntegrationRequest.json at immutable objects advertised by refs/heads/codex/queue/* and refs/heads/codex/integration-lock. Request schema is integer 1, ref and ID must agree, and source is an exact SHA-1 commit. Supported statuses are queued, processing, validating, integrated/delivered and blocked/failed. Other statuses stay Unknown with a finding, including superseded records whose delivery ancestry is outside this adapter. Original JSON preserves validation inputs/results/unverified claims and additional fields.

Delivered records require an exact main_commit and a schema-1 receipt at Tools/IntegrationQueue/Receipts/<original-id>.json. Receipt ID/source/version must match. Inspection reports the recorded state separately from receipt availability. It never reruns checks, opens evidence links, imports source, executes downloaded code or infers current remote-main ancestry from a receipt.

Local state is read from the common Git directory's integration_processor/processor-state.json. The directory is never created by inspection. A known PID is checked read-only through the OS. Presence cannot distinguish PID reuse, a healthy owner or a hung process. Missing PID evidence and reports older than two minutes remain Unknown/Stale; explicit blocked/failed reports retain failure details. No file-lock probe or processor command is used. Healthy/validating/busy phases in JSON remain reported observations, not verified liveness.

Protocol-1 leases accept only recorded 180/1800-second policies, known owner fields and owned/released states. A released ref is Released, not Busy. One inspection cannot establish monotonic renewal/abandonment evidence. Legacy/future/malformed records require their owner; timestamps never authorize takeover.

Unavailable HTTPS access: this release deliberately disables credential helpers and interactive prompts. Use a public HTTPS remote or a trusted absolute local remote for inspection; private/SSH transport needs a later supported read-only adapter. It does not change stored remotes. Unknown processor ownership: consult the configured owner through its supported operator surface. Malformed records: preserve evidence and ask that owner to repair them. Timeout: the current observation failed; no automatic retry is scheduled. Cleanup failures expose the exact owned temporary path.

Git fetches use only a unique temporary bare object store and no FETCH_HEAD. Target branches, index, uncommitted files, settings and remote refs are never written. Configuration commands only read the existing remote URL. No repository Python, PowerShell, validation, publisher or runner is launched. Temporary object stores are removed after completion/failure. API use and UI callbacks must be awaited/observed; do not block Unity's main thread.

## Planned capabilities

Idempotent submission and independent publishing; queue-owned versions, fenced leases and receipts; same-ID recovery, safe whitespace correction, native identities and stopped-owner runner upgrades; optional separately authorized AI repair. None of these operations exists in this release. The Automation Technique remains the workflow owner. Dreadvault's processor, runner, policy and configuration are unchanged.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{
  "dependencies": {
    "com.geurts.gameforge.god": "0.39.3",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  },
  "packageId": "com.geurts.gameforge.gitflow",
  "packageVersion": "0.1.2"
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
