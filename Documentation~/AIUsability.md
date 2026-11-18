# GitFlow supported automation contract

Version 0.12.0 retains bounded heartbeat reconciliation, parent-owned native session restoration and explicit repair reasoning selection. Read [Validation.md](Validation.md) for evidence and limits.

## Ownership verification recovery

Transient failures of the remote `ls-remote`/exact-object `fetch` reads restart verification once, within the shared 60-second coordination allowance. The retry re-observes the exact expected child and owner; it does not repeat a push, sleep, extend the previous ownership deadline, accept a successor as current ownership or weaken atomic fencing. A recovered read retains `verification_<child>.json` with both observations and the original error.

If both reads fail during a heartbeat, the old owner is permanently fenced and its native child tree is stopped/restored through existing containment. `serve` may retain an automatic continuation only after the heartbeat has stopped, the candidate is clean and its HEAD/index match the recorded validating request. It journals the original request/control/source/candidate, configuration and failed owner. The same service remains alive with bounded backoff; a new lease must observe the unchanged remote record for its normal recorded duration before takeover (600 seconds for new owners, or the supported older record's full 180/1800 seconds). Access failures reset observations and show `waiting_for_access`; healthy owners reset takeover observations. No remote timestamp, missing PID or exception grants early takeover.

After fresh ownership is acquired, the original candidate runs the unchanged required gates and retains its version, source, attempts and original repair budget. Successful integration returns the service to all-request processing. This is deterministic publisher recovery and invokes no repair model. Configuration, source/index drift, changed request controls, malformed/unknown owners and uncertain delivery writes remain actionable blocks. No automatic whole-process resurrection, host-reboot restart, generic source-check retry, package update or lease deletion is introduced. A service restart does not adopt an old local continuation automatically.

### Shorter ownership timing and adoption

New owners advertise a 600-second lease and renew every 10 seconds. Small coordination commands are bounded to 20 seconds; the complete verification, including its one read retry, shares 60 seconds. A delivery/object-transfer command retains its full 300-second allowance. Before a write the owner reserves both that command and verification under its current monotonic deadline. If a live owner needs more reserve, it first publishes and verifies a heartbeat before constructing the delivery child. Expired/fenced owners cannot renew. Object-transfer reserve is checked separately; deadline checks after preparation/transfer and verification remain mandatory.

Named heartbeat continuations poll with 10-second bounded backoff; ordinary idle polling stays unchanged. The takeover observation duration comes from the actual unchanged record, never the new default or a wall-clock timestamp. Existing supported 180- and 1800-second records retain their full recorded windows. New 600-second records require GitFlow 0.12.0 or newer on every participating publisher; older readers refuse that unknown timing instead of guessing. Stopped-owner adoption can immediately use new durations after the preceding owner released. It never shortens a live older lease.

The Python publisher may be installed from an exact published Git checkout independently of a game's Unity package. With the local service/native descendants stopped and shared ownership released, run that checkout's complete `Tools~/Queue/GitFlowQueue.py` with `--repository <configured-controller> upgrade-runner --operator --expected-configuration <reviewed-identity>`. The existing API journals both configurations and copies the complete immutable family into the same publisher root. Preserve the original game candidate/index, model, clocks, scopes and checks. This tool-only action neither installs a Unity package nor clears a failed game check. Editor clients and other publishers need the matching supported package before operating the new timing cohort; consumer package changes retain their separate native UPM and game-queue workflow.

## Automatic Unity folder metadata recovery

With automatic repair enabled, the controller can commit missing folder sidecars created by its owned Unity invocation before a code-repair model is called. It discovers tracked Unity projects, then missing sidecars for nonempty tracked folders under `Assets/_Project` and `Assets/_Generated`. Vendor/plugin/protected tool/cache roots, tracked metadata, ignored paths, empty/untracked folders and pre-existing untracked files are excluded. `repair.native_folder_metadata: false` opts out; other repair settings and deadlines are unchanged.

The controller records exact HEAD/index, subtree membership, configured Editor and absent-before paths in an invocation-bound plan. The paired native adapter records newly created sidecars only after its Unity child stops, even when compilation fails or times out. It retains exact native bytes/GUIDs and never generates GUIDs. The controller checks that plan/proof, raw bytes, ordinary paths, unique noncolliding GUIDs, tracked changes and complete untracked membership still match before staging only those sidecars. Unknown files, malformed/stale proofs, changed source/index, links and collisions remain actionable; they do not trigger paid repair for provenance problems. Unsupported native wrappers without this proof remain blocked.

Metadata recovery is not compiler success. Native version failures retain their diagnostics and proceed to ordinary source repair against a clean candidate. A gate-time metadata commit invalidates the old candidate result and reruns every required gate on the new commit, without another version or a model call. Original request/source/history and the original repair clock remain intact. Native proofs and recovery counts are retained for inspection. The unchanged full zero-warning/zero-error checks remain required for code and assets.

Install the complete published runner/native family, then use the stopped-owner runner-adoption procedure. Older controllers invoking the newer native adapter without the plan environment keep their prior behavior; a new controller requires matching native provenance and never invents a fallback report. No managed documentation content update or unrelated project update is implied.

## Documentation-only queue changes

Compilation gates default to code-only applicability. The processor examines the complete merged change before version assignment, independently of the submission title. Only regular, non-executable UTF-8 `.md`/`.txt` files under root `Docs/` or `Documentation~/`, and root `README.md`/`CHANGELOG.md`, qualify. Nested Unity `Docs/`/`Documentation~/` require a tracked `ProjectSettings/ProjectVersion.txt` in both revisions. Imported assets, code, packages, configuration, binary/link/mode changes, unknown paths and mixed changes run full validation. A rename is checked as its deletion and addition.

Native versioning still runs. Only the declared text version and a byte-verified `bundleVersion`-only native PlayerSettings change may accompany documentation; other changes fall back to full validation. Repaired candidates use normal gates. Receipts say `not_applicable`, never compiler passed, and retain exact base/documentation/final candidates, paths, policy and version proof. Published-receipt recovery rechecks that portable proof.

Existing final/specialist checks remain required by default. A project may explicitly mark a check `code_only: true` if it has no documentation responsibility; `code_only: false` always runs it. `documentation_only: false` disables this route for a project whose build consumes documentation. While the service and shared owner are stopped, use `set-documentation-checks --operator --expected-configuration <reviewed-identity> --check <exact-name>` once per code-only check. Omitted checks remain required for documentation too. This changes applicability only; commands, diagnostic assertions, timeouts, scope and repair budgets remain intact.

Code, mixed and unknown changes still require complete zero-warning/zero-error native compilation and qualification. Documentation checks, when configured, remain required. This route does not certify a new Unity build or reuse an old report as a newly executed check.

## Faster compiler verification

Native session originals use bounded native Git batches with the same per-path clean filters and committed blob checks. Capture rechecks raw originals, HEAD and index before any child launch; unknown or concurrent edits remain failures. For code, mixed or unknown changes, full native script rebuilds and complete default-diagnostic qualification remain required. Native results record compilation and qualification durations, with the qualifier worker count when supplied by the installed Compatibility owner. No result cache, warning exclusion or skipped assembly is introduced.

A single Windows Unity 6000.6.8f1 serial-then-parallel comparison on the same stopped candidate checked 107 assemblies and 6317 unique inputs with zero diagnostics and identical compiler arguments. Qualification took 314.12s versus 106.73s; native original capture took 81.62s versus 4.82s for 213 files with identical manifests. Filesystem warmth and host load may affect timing; this does not measure a whole Unity rebuild or guarantee every queue request duration.

## Faster failure recovery

A completed rejected heartbeat with no other publication targets receives exact-object/owner readback under the previous monotonic deadline. If the child already landed, verify it without another push; if the exact held parent remains, retry the same child once with a full write reserve. A moved owner, second rejection, timeout, late verification, release or delivery/control publication failure still fences the owner. The protocol-1 lineage and timing remain compatible; durable heartbeat receipts retain the rejection, readback and verification. This is no general Git/network retry loop.

An operator may declare exact tracked native session-output paths in each gate's `session_outputs` list using `GitFlowQueue.set_native_session_outputs(root, expected_configuration, check_name, paths)`. Call only while local owners have stopped and the shared owner is released. Declare only known native-owned session settings/cache outputs, never authored gameplay, assets or validation code. No globs or implicit discovery apply. Before launch the surviving parent saves complete originals and their hashes as flat gate artifacts; after child completion/cancellation it restores those exact known bytes. HEAD/index changes, deletion, reparse replacement or unknown source remain blocking and preserved. Existing policies without this declaration retain their previous behavior. A whole publisher crash still needs stopped-owner recovery from the retained originals; this mechanism handles child cancellation, not machine/process resurrection.

Use `set-repair-model --operator --expected-configuration <reviewed-identity> --model <configured-model> --effort high` through the installed CLI after stopping the publisher and verifying a released shared owner. The operation checks the signed-in local model catalogue, journals both configurations and preserves gates, fallback order, candidate, history and original repair clocks. Selection never silently downgrades reasoning, enables repair or changes a model. Lower effort can reduce latency but is not a fixed duration promise; independent artifact review and final gates remain required. Repair itself has no per-call shortcut timeout beyond the original overall budget.

## Choose the relevant surface

- Human workflow: open Tools > Geurts Game Forge > GitFlow. The Help button and GitFlow Help menu render the installed QueueLocalChanges.md guide inside Unity. Opening help performs no queue or model action.
- New reusable queue: GitFlowBrick.Host owns local preferences and explicit host operations. UI controls use this same service. Opening the window, model selection and module enable start no queue job, inference or package update.
- Legacy observation: GitFlowBrick.Service remains the bounded read-only GitFlowInspection. The lower legacy viewer uses refs/heads/codex/queue; it is separate from the new gameforge queue protocol. Its God operation is inspect_queue. It cannot submit or operate a legacy processor.

## Queue console and selected repository

Queue records now render as grouped read-only GUI fields. All controls use the same bounded/redacted projection as the supported result formatter. Field grouping is presentation only; identities, queue records, policies, authentication and publisher behavior are unchanged. Exact identifiers remain selectable; timestamps display their recorded offset. Empty collections and display omissions remain explicit.

Read [QueueConsole.md](QueueConsole.md) for explicit modern pages, selected-repository setup, coverage/freshness, safe evidence and legacy non-adoption. Modern inspect now defaults to 16 requests in a temporary bare store, with explicit partial/unavailable results. Opening and repainting remain offline. Validation used a disposable local queue; no production queue action was taken.

## Prerequisites and lifecycle

God is the only mandatory Forge brick. Use the exact dependency minimums in package.json, licensed Odin Inspector and Quantum Console, Git for Windows, and explicitly selected native Python 3.10 or newer. This candidate's native process containment is verified on Windows only. Model features additionally require installed local Codex and its existing sign-in. No credentials are stored in project settings and no API-key backend is provided.

GitFlowBrick.Host is null while unavailable. Load/Save use God's validated BrickSettingsStore. Preferences contain only paths, repository/remote, capture scope, model and effort. Stopping the brick cancels its own bridge requests; the explicitly started background publisher is independent. Await methods, observe failures, and never block Unity's main thread. A serial bridge refuses overlapping operations.

## Commit saved changes before submission

Default submission creates a normal local Git commit first, then queues that exact source. The current branch advances and included staged/unstaged/deleted/nonignored new files become committed. Native Git owns locks, identity, configured hooks and signing. No reset, stash, forced ignored-file addition, direct-main push or second Unity clone occurs. The publisher still owns final versioning, required checks, shared-main publication and synchronization.

`submit --title "Meaningful title" --size patch --summary "Actual change"` commits all saved nonignored changes in the Git repository by default. Optional repeated `--scope Assets/MyGame` uses the scoped capture inventory instead. `submit --committed-only` explicitly retains the previous clean-already-committed behavior. The Python API is `submit(root, remote, title, size, summary, dependencies=(), scopes=None, committed_only=False)`. Low-level `submit_source` still accepts an exact existing commit and never invents local edits.

The Unity Queue local changes action saves included dirty assets through AssetDatabase.SaveAssetIfDirty, prompts only for included modified scenes, and refuses untitled scenes, unsaved Prefab Mode work, Play Mode, imports or compilation. Its existing CaptureScope selects eligible task-owned files and Unity companions; generated/vendor/validation/tool folders and queue-owned versions/manifests remain excluded in that scoped route. Outside-scope staging and saved/unsaved work remain untouched. No CLI action saves Editor memory.

`Host.ExecuteQueueAsync(preferences, ["queue-local-changes", "--scope", "Assets/MyGame", "--title", "Meaningful title", "--summary", "Actual change"], timeoutSeconds, cancellation)` uses the same default local-commit/submission service. `preview-local-changes --scope Assets/MyGame` stays read-only. These direct APIs create no new God operation ID; inspect_queue remains the registered read-only operation.

Before committing, a private index builds a reviewed saved-source snapshot. File discovery disables rename collapsing so staged moves include both old/new paths and their metadata. Native `git add` and `git commit --only` commit the selected paths while retaining unrelated index entries. Source, branch and unrelated-staging drift or a hook changing the reviewed tree stops submission; retain the actual commit/files and inspect the journal under `.git/gameforge_queue/local_commits`. A failed native commit may leave selected work staged. Nothing is reset or discarded to hide the failure. Existing merges/rebases, dirty detached HEADs and foreign index locks remain actionable.

If commit succeeds but queue access fails, the local commit remains. An unchanged retry reuses HEAD and the deterministic request ID without creating an empty or duplicate commit. Already committed history remains included; a scope cannot isolate earlier unrelated commits or separate tasks within one file.

For explicit snapshot-only use, `queue-local-changes --snapshot-only --scope Assets/MyGame` and `queue_local_changes(..., snapshot_only=True)` retain the previous detached checkpoint behavior, preserving live HEAD/index. The low-level `GitFlowCapture.capture` remains that explicit snapshot primitive. Normal `queue_local_changes` uses `commit_local` and advances the current branch.

## Publisher defaults and complete queue display

Use the UI's Auto-fill fields and start processor button for combined confirmed setup/start, and Refresh Queue Panel for all modern pages. Live queue updates is default-off, view-owned and read-only. The shared host's `ExecuteQueueAsync(preferences, new[] { "publisher-settings", "--unity", installedUnity }, cancellation: token)` reads defaults or validates/reuses the existing policy. It performs no setup or launch. New defaults require every Edit Mode test (`.*`); existing custom policy commands remain unchanged.

`ConfigureUnityAsync` returns `configuration_identity` after successful new configuration. Existing defaults return that same digest. `StartPublisher(preferences, expectedConfiguration)` is the additive bound-start overload; the original one-argument API remains available. For an existing stopped owner, execute `resume-service --operator --expected-configuration <digest>` before the bound start. Both resume and service startup compare the reviewed identity under the existing exclusive guard; a changed policy preserves the stop marker and cannot start. No runner adoption, legacy migration, model choice or gate reduction is implied. Startup dispatch is not readiness; inspect afterward. Modern `inspect --offset <n> --limit 64` pages expose `coverage.queue_identity`; collect all pages only while that identity agrees, otherwise report partial and refresh again. UI and API use these same commands and guards.

## Publisher, ownership and recovery

An operator configures one new short root. Ownership markers bind the repository, remote, runner hash and validation policy. Configuration and explicit stopped-owner adoption copy the complete GitFlow Python module family into a new immutable `<workspace>/Runner/<identity>/` directory and relocate only the owning GitFlowUnity.py policy arguments there. The manifest and owner record bind the complete copy. Long-lived `process` and `serve` CLI entry points forward into that reviewed copy in the same PID before acquiring ownership; repair/native helpers remain available if Unity removes the original package-cache folder. Use the CLI or configured runner for production processing; direct Python imports from an ephemeral package cache do not provide this startup boundary. Failed/tampered copies are preserved and refused, never overwritten or silently rebuilt. Old copies are retained; no automatic cleanup is introduced. One detached Git worktree shares the repository's object database; its Unity Library is reused. Failed/dirty candidates are preserved. There is no skip, bypass or advisory gate.

The publisher serializes the oldest request, verifies dependencies, combines source with main, assigns one semantic version, and calls the native version adapter through PlayerSettings.bundleVersion. Protocol-1 version proof is explicitly requested and required. Legacy helper invocations without that flag retain their older behavior. Project-specific native derived identities require their own project adapter and required gate; this generic helper does not claim to implement them.

Every applicable configured check is required. Outside the proven documentation-only route above, compilation and final gates always run against the exact candidate. The native compilation gate uses CompilationPipeline.RequestScriptCompilation with CleanBuildCache and collects every assembly diagnostic; a warm Library cannot hide cached warnings. This rebuilds scripts while retaining imported-asset caches. See [Unity's supported full-compilation API](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/compilation/compilationpipeline/requestscriptcompilation). Specialist evidence can be reused only for identical declared source objects, policy, command, environment and tool bytes, with unchanged retained artifact hashes. Use inputs ["."] when complete dependencies are uncertain. Zero errors, warnings and skips are required. A final check requires a report identifying the current invocation and candidate, and must leave the source clean and unchanged. Moving main stops the candidate for explicit reviewed continuation instead of causing an automatic revalidation loop.

Required checks finish before an atomic update of main, request state and the shared lease. Portable receipts and raw gate artifacts live in the request commit, outside game source. Later states preserve them. Published recovery verifies the complete portable receipt/artifact hashes and main/source ancestry before completing required pulls, with no second version or repeated checks. Dirty original checkouts remain untouched and leave the request Published until explicitly reconciled. A non-main task branch is not silently moved.

Queue refs use refs/heads/gameforge/queue and the shared lease uses refs/heads/gameforge/integration-lease. Lease protocol 1 uses 1800-second ownership, 30-second renewal and bounded 300-second transport; supported older 180-second records are observed for their declared interval. A contender observes an unchanged exact object for the full monotonic interval. PID and wall-clock timestamps never grant ownership. All publication targets are atomically fenced. Ambiguous writes fence the owner and preserve verification evidence. Owner inspection exposes machine, owner, request/stage and recorded renewal separately from unknown process health.

Start configured background publisher starts one deterministic native program. It is not proof of readiness; inspect the shared owner and local recorded state. Graceful stop finishes the owned request before exiting. Prepare stopped publisher to resume only removes a known stop marker under OS ownership locks. Blocked requests require exact same-request continuation with reviewed request/candidate commits. Unchanged failed inputs are not retried.

After a manual Forge update, Adopt this installed runner after stopping requires stopped local owners, a released shared owner and the exact reviewed configuration identity. It preserves requests, candidates, repair history and evidence, journals both configurations and updates only the configured runner/owned adapter paths. An interrupted ownership/configuration mismatch fails closed for operator reconciliation. This action installs nothing and does not restart the processor.

Publisher ref reads batch missing exact Git objects and complete request blobs, while sensitive ref/lease checks still query the remote freshly. See [reference-read behavior and limits](QueueConsole.md#bounds-and-truthful-evidence). The source-only benchmark is `python Tools~/Queue/BenchmarkGitFlowReferences.py --output <new-task-owned-report.json>` from a Git source checkout retaining commit 2e5ab9a. It creates only disposable local repositories and requires an existing output directory; results are synthetic local evidence, not production or LFS savings forecasts.

## Native compiler prerequisite and evidence

Use the published Compatibility owner for exact dependency preparation and coherent native reference settings. God 0.44.4 requires that independent core; it is not an optional peer brick. The minimum fixture used Compatibility 0.1.1; the selected Unity 6000.6.8f1 fixture uses released Compatibility 0.2.0 and its reviewed profile. Preserve licensed originals and use the owning preparation/adoption workflow. Never patch PackageCache or suppress warnings.

`GitFlowUnity.py compile-project` requires its evidence directory outside the Unity project. It performs a native full script rebuild and scans the complete log, then invokes the installed Compatibility qualifier over every actual source-bearing compiler response with compiler/analyzer default diagnostics. Private response copies remove diagnostic filters without altering sources, references, analyzers or native policy files. A zero native exit alone is insufficient. The shared deadline includes both stages.

The gate emits flat portable `result.json` and `compiler-qualification.json` with run/candidate identities and hashes. Full input inventories, copied responses and derived assemblies remain in a private sibling `<evidence>.compiler-private` directory; its launcher log is private too. Retain those owner-local records and publish only safe summaries. Any diagnostics, ignored immutable code assets, incomplete inputs or changed native/private-policy inputs fail the gate. No success receipt is written after failure.

## Project repair and remaining safeguards

On a Git merge conflict in one Unity scene, the publisher first checks whether the submitted scene differs only by a single missing typed asset reference and already-shared additions. The configured publisher launches the existing licensed Editor in its isolated publishing workspace, saves and reopens the scene, checks that Unity wrote just the expected reference, and attaches source-bound proof to the request. This runs before model repair and still requires the existing compiler and runtime checks. Competing edits, incomplete proof, unexpected writes or a licensing failure preserve the conflict and give a native recovery diagnosis; the queue never applies a text patch to make such a scene merge pass.

Automatic repair remains separately authorized and disabled by default. Existing enabled processors require explicit stopped-owner runner adoption; publication never installs or starts a consumer. The obsolete conflict_paths field/command remains compatible configuration data, but no longer limits the project-workspace resolver.

Authorized repair can inspect and fix any relevant project file, including files outside the submitted diff, Editor scripts, shaders, packages/vendor source, tests, settings, assets and metadata. It supports creation, deletion, rename, binary changes and whole-file replacement with no fixed file/edit-count ceilings. Preserve licences, notices, asset GUIDs and existing features. Use the source/owning generator for generated outputs, and supported Unity APIs for Unity-owned serialization.

Only five safeguards remain: preserve the active checkout and originals; keep changes relevant and exclude credentials/unrelated projects; keep independent validation and publication controllers protected; retain one fenced queue publisher; and retain the original three-hour total budget. All independent gates, warning/error/skip requirements and repository protections remain in force. Necessary independent-gate corrections need a separate reviewed change.

The controller copies complete saved nonignored candidate content into a short owner-local Repair/<identity>/Original and Proposed tree without Git metadata, remotes or ignored Unity caches. Codex edits only Proposed through workspace-write file/shell tools and official-documentation search. A separate read-only model pass checks the complete changed-file manifest and actual before/after artifacts for relevance, feature preservation and unchanged validation coverage. Rejected artifacts never change the candidate. The controller verifies original HEAD/index/membership/raw hashes and reviewed artifact hashes before importing the exact reviewed bytes, rerunning the staged whitespace guard, committing, and rerunning independent checks. All artifacts, responses and hashes are retained. Before independent review, a private Git index copied from the real candidate stages the proposed changes and runs the same authored-text whitespace guard, including previously staged source outside the proposal. A rejected check records preflight.json and returns its diagnostics to the next proposal within the original budget only after proving the real HEAD/index/status stayed unchanged. Native .overlay serialization joins the existing .asset/.meta/.prefab/.unity exception; exact reviewed payload bytes are preserved. Other Git, ownership, concurrent-change, import or commit failures remain actionable blockers, never blind retries of uncertain writes.

Independent file validation, two-copy capture, hashing and candidate-byte audits use up to eight I/O workers. Every original path/reparse, membership, index, raw-byte, protected-file and review check remains; imports and Git publication stay serialized. The local state names snapshot, proposal, proposal audit, formatting check, review, review audit and application; each completed attempt retains phase seconds. These are measured stages, not inferred completion percentages.

Model repair starts at the exact diagnostic and affected symbols; review inspects every changed artifact and directly affected dependencies without repeating the controller's complete tree hash scan. Neither model phase runs final queue gates. Unchanged failed native licensing/IPC/access calls are not retried inside the sandbox; report the exact unavailable native action promptly, never hand-edit Unity serialization. The original selected model/effort, full project availability, overall deadline and independent review/gates remain unchanged. A faster file benchmark does not guarantee a fixed total repair duration.

No application-imposed byte limits apply to repair files, diagnostics/logs, model input/output, queue control records or portable gate evidence. The controller passes file paths rather than embedding project contents; the model reads/searches complete on-disk files in useful portions within its actual context window. Resource exhaustion, unavailable tooling and filesystem/platform limitations remain real failures, not a promise of infinite capacity. Viewer pagination/redaction/display limits are separate and unchanged.

The original three-hour start and all attempts survive restart, adoption and model changes. There is no three-no-progress-attempt cutoff and no ten-minute model-call ceiling; each call receives only the remaining overall budget. Previous failures/review reasons are supplied to later attempts. A reply or review after the deadline cannot be applied. Explicit unavailable access/project decisions, uncertain interrupted application, ownership loss and failed independent gates remain blockers. Known model/review failures preserve the unchanged candidate and isolated artifacts for a reviewed continuation.

Configured model/effort remains primary. An optional repair.fallbacks array of {model, effort} objects permits an ordered alternative when the primary selection is absent from the signed-in local catalogue; actual selection is recorded. No unconfigured model is silently selected. Local Codex owns sign-in. Environment API keys are not passed; apps/plugins/hooks, subagents, memories and desktop/image tools are disabled to keep work inside the project and preserve the no-visual/background boundary. No approval dialogs or desktop input are requested.

Find Python and Codex executables and DiscoverModelsAsync retain their local discovery behavior. DiagnoseAsync remains a separately authorized read-only proposal API; discovery alone does not prove inference access. Repair/source acceptance uses controlled transports, not a claim that live paid inference or a game has passed.

## Storage and measurement

Storage measurement is read-only, bounded and reports unique logical file bytes and incomplete paths. It does not report physical allocated clusters or delete anything. The fixed three-request benchmark retains one source/cache per full-clone baseline request and compares that with one shared workspace/cache plus all evidence. See Evidence/0.2.0/StorageBenchmark.json for actual samples and limits. Production Dreadvault measurements are separate read-only snapshots, not claimed savings.

All failed candidates and evidence are retained. Portable gate evidence has no application-imposed byte ceiling. There is no automatic cleanup, compression or deletion of user workspaces, old Library directories, recovery archives or queue history. Long-term archive policy and low-disk operational thresholds need an explicit owning project decision.

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
  "packageVersion": "0.13.2",
  "dependencies": {
    "com.geurts.gameforge.god": "0.44.4",
    "com.unity.nuget.newtonsoft-json": "3.2.2"
  }
}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
