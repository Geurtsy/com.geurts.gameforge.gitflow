# GitFlow supported automation contract

Version 0.2.0 is an unreleased candidate. Consult Validation.md before describing it as ready, installing it into a game, or publishing a catalogue release. The human tutorial is rendered inside Unity by GitFlowHelp; this document defines the separate automation contract.

## Choose the relevant surface

- Human workflow: open Tools > Geurts Game Forge > GitFlow. The Help button and GitFlow Help menu render the installed QueueLocalChanges.md guide inside Unity. Opening help performs no queue or model action.
- New reusable queue: GitFlowBrick.Host owns local preferences and explicit host operations. UI controls use this same service. Opening the window, model selection and module enable start no queue job, inference or package update.
- Legacy observation: GitFlowBrick.Service remains the bounded read-only GitFlowInspection. The lower legacy viewer uses refs/heads/codex/queue; it is separate from the new gameforge queue protocol. Its God operation is inspect_queue. It cannot submit or operate a legacy processor.

## Prerequisites and lifecycle

God is the only mandatory Forge brick. Use the exact dependency minimums in package.json, licensed Odin Inspector and Quantum Console, Git for Windows, and explicitly selected native Python 3.10 or newer. This candidate's native process containment is verified on Windows only. Model features additionally require installed local Codex and its existing sign-in. No credentials are stored in project settings and no API-key backend is provided.

GitFlowBrick.Host is null while unavailable. Load/Save use God's validated BrickSettingsStore. Preferences contain only paths, repository/remote, capture scope, model and effort. Stopping the brick cancels its own bridge requests; the explicitly started background publisher is independent. Await methods, observe failures, and never block Unity's main thread. A serial bridge refuses overlapping operations.

## Saved-change capture and submission

The Unity Queue local changes action verifies this is the active project, waits for imports/compilation/Play Mode to finish, saves included dirty assets with AssetDatabase.SaveAssetIfDirty, and uses the supported scene-save prompt only for included scenes. It refuses untitled scenes and unsaved Prefab Mode work with actionable messages. Excluded/unrelated dirty assets and scenes remain unchanged. CaptureScope is a task-owned Assets folder in the UI.

The shared API is Host.ExecuteQueueAsync(preferences, arguments, timeoutSeconds, cancellation). For already saved data, arguments ["queue-local-changes", "--scope", "Assets/MyGame", "--title", "Meaningful title", "--summary", "Actual change"] run the same capture/submit host. The API does not save Editor memory; callers must use supported Unity APIs first. preview-local-changes with --scope reads the saved capture plan. Do not create an operation ID for these direct methods: inspect_queue remains the only registered God operation in this candidate.

Capture uses a temporary GIT_INDEX_FILE initialized from the current HEAD. It selects eligible saved working bytes/deletions and nonignored new files in the requested scope, plus required Unity metadata. Generated/vendor/validation/tool folders and queue-owned versions/package manifests are excluded. Literal NUL-delimited pathspecs prevent broad staging. It compares raw bytes, path inventory, HEAD and the original index before committing. An existing unresolved merge or a concurrent source change fails without checkout mutation.

The resulting checkpoint is an ordinary commit with the original HEAD as parent. Canonical snapshot metadata makes an unchanged capture idempotent even after a response is lost. The live HEAD, branch, index and working bytes are preserved. Existing committed branch history is included; a scope cannot isolate earlier unrelated commits or distinguish two tasks in one file. No stash/reset, forced ignored-file add, direct-main push or second Unity clone occurs.

The optional submit command requires an already clean committed task branch. It accepts title, summary, patch/minor/major size and explicit dependency request IDs. Both routes verify the remote immutable source request. The request ID is derived from the full source commit. A submission response ambiguity triggers one read reconciliation, never a repeated uncertain write. Existing legacy queue refs block new-protocol submission/configuration until an explicitly reviewed migration occurs.

## Publisher, ownership and recovery

An operator configures one new short root. Ownership markers bind the repository, remote, runner hash and validation policy. One detached Git worktree shares the repository's object database; its Unity Library is reused. Failed/dirty candidates are preserved. There is no skip, bypass or advisory gate.

The publisher serializes the oldest request, verifies dependencies, combines source with main, assigns one semantic version, and calls the native version adapter through PlayerSettings.bundleVersion. Protocol-1 version proof is explicitly requested and required. Legacy helper invocations without that flag retain their older behavior. Project-specific native derived identities require their own project adapter and required gate; this generic helper does not claim to implement them.

Every configured check is required. Compilation and final gates always run against the exact candidate. The native compilation gate uses CompilationPipeline.RequestScriptCompilation with CleanBuildCache and collects every assembly diagnostic; a warm Library cannot hide cached warnings. This rebuilds scripts while retaining imported-asset caches. See [Unity's supported full-compilation API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/compilation/compilationpipeline/requestscriptcompilation). Specialist evidence can be reused only for identical declared source objects, policy, command, environment and tool bytes, with unchanged retained artifact hashes. Use inputs ["."] when complete dependencies are uncertain. Zero errors, warnings and skips are required. A final check requires a report identifying the current invocation and candidate, and must leave the source clean and unchanged. Moving main stops the candidate for explicit reviewed continuation instead of causing an automatic revalidation loop.

Required checks finish before an atomic update of main, request state and the shared lease. Portable receipts and raw gate artifacts live in the request commit, outside game source. Later states preserve them. Published recovery verifies the complete portable receipt/artifact hashes and main/source ancestry before completing required pulls, with no second version or repeated checks. Dirty original checkouts remain untouched and leave the request Published until explicitly reconciled. A non-main task branch is not silently moved.

Queue refs use refs/heads/gameforge/queue and the shared lease uses refs/heads/gameforge/integration-lease. Lease protocol 1 uses 1800-second ownership, 30-second renewal and bounded 300-second transport; supported older 180-second records are observed for their declared interval. A contender observes an unchanged exact object for the full monotonic interval. PID and wall-clock timestamps never grant ownership. All publication targets are atomically fenced. Ambiguous writes fence the owner and preserve verification evidence. Owner inspection exposes machine, owner, request/stage and recorded renewal separately from unknown process health.

Start configured background publisher starts one deterministic native program. It is not proof of readiness; inspect the shared owner and local recorded state. Graceful stop finishes the owned request before exiting. Prepare stopped publisher to resume only removes a known stop marker under OS ownership locks. Blocked requests require exact same-request continuation with reviewed request/candidate commits. Unchanged failed inputs are not retried.

After a manual Forge update, Adopt this installed runner after stopping requires stopped local owners, a released shared owner and the exact reviewed configuration identity. It preserves requests, candidates, repair history and evidence, journals both configurations and updates only the configured runner/owned adapter paths. An interrupted ownership/configuration mismatch fails closed for operator reconciliation. This action installs nothing and does not restart the processor.

## Local Codex and bounded repair

DiscoverModelsAsync uses local codex app-server initialize, account/read without token refresh, and paged model/list. It returns provider model IDs and supported reasoning efforts; it does not infer catalogue values from desktop labels. Discovery is not an inference request or entitlement proof. DiagnoseAsync requires explicitly reviewed bounded context and account usage consent; it returns one proposal without applying it.

Automatic repair is disabled by default and separately authorized at operator configuration. Selecting a model grants no editing authority. Authorized repair pins the selected local model/effort and retains its original three-hour start across retries, restart and runner/model changes. Individual inference and Git/lease budgets remain separate. Only complete bounded task-owned source files are supplied. Validation/controller/native/generated/vendor files are protected. Exact unique fragment edits require the original raw hash; whole-file replacement, partial conflicts, stale edits and protected targets are rejected. Raw replies and hashes are retained before application. Three consecutive attempts with no bounded edit, repeated/whitespace-only source, or the same independent validation failure stop further attempts early; the durable counter survives restart. A changed independent failure resets that consecutive counter but never the deadline. Rejected proposals leave source intact, and subsequent proposals receive recent failure feedback. Interrupted uncertain attempts or violated safety boundaries still require reconciliation; their outcomes are never guessed.

The repair guard is deterministically tested with fixture proposals. Live paid inference remains unverified; metadata discovery alone must never be described as successful model execution.

## Disk use and retention

Storage measurement is read-only, bounded and reports unique logical file bytes and incomplete paths. It does not report physical allocated clusters or delete anything. The fixed three-request benchmark retains one source/cache per full-clone baseline request and compares that with one shared workspace/cache plus all evidence. See Evidence/0.2.0/StorageBenchmark.json for actual samples and limits. Production Dreadvault measurements are separate read-only snapshots, not claimed savings.

All failed candidates and evidence are retained. The gate portable-artifact limit is 128 MiB per gate; oversize evidence blocks publication. There is no automatic cleanup, compression or deletion of user workspaces, old Library directories, recovery archives or queue history. Long-term archive policy and low-disk operational thresholds need an explicit owning project decision.

## Manual Forge updates only

GitFlow never installs or updates itself, another Forge package or managed Commandments on startup, refresh, module enable, model selection or queue activity. Actual package/documentation updates belong to the user's current explicit Update action and owning confirmation. Processing user-submitted game changes is the separate, explicitly authorized queue workflow. God manual-update-policy changes are owned by another task; this candidate uses existing shared settings/theme APIs and does not edit that worktree.

## Legacy read-only API example

The legacy inspector retains its 45-second / 128-request read bounds, dated snapshots, no automatic refresh, preserved checkout and temporary observation object store. Public HTTPS and absolute local remotes are its supported transports. It reads recorded legacy claims; it does not prove live integration or operate the legacy runner.

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

## Provenance and validation limits

GitFlowLease.py adapts the user-owned Dreadvault Tools/IntegrationQueue/lock_lease.py at commit 088c912769bb4de67443707a24558d6dee08e4f0, adding the reusable adapter, contained process transport and recorded owner work fields. Dreadvault's live processor, policy, queue and Unity session are outside this package's implementation scope.

The release gate binds C#, Python and assembly definitions. Validation.md names actual acceptance and remaining publication blockers. Headless tree construction is not a rendered interactive UI acceptance; model metadata is not paid inference; local bare remotes are not actual GitHub contention evidence. Never substitute historical 0.1.2 reports for this candidate.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{
  "packageId": "com.geurts.gameforge.gitflow",
  "packageVersion": "0.2.0",
  "dependencies": {
    "com.geurts.gameforge.god": "0.39.3",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  }
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
