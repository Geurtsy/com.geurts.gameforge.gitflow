# GitFlow queue console and selected-repository setup

Version 0.3.0 is a source-only, unreleased candidate. No game installation, queue adoption, processor invocation or Unity acceptance was performed for this update. Use the Catalogue for released installation targets, not this candidate branch.

## Editor workflow

Open Tools > Geurts Game Forge > GitFlow, standalone or embedded in God. Opening, restoring, expanding sections and repainting stay offline. The existing Odin repository/tool controls retain project preferences through God's validated settings store. God is the only required Forge brick; package dependency minimums remain in package.json. Licensed Odin Inspector and Quantum Console, Git for Windows and an explicitly selected native Python 3.10+ are required. Codex is needed only for its optional model/repair functions.

1. Select the existing Git repository and existing remote name in Queue host and local Codex. Select installed Python; save preferences if wanted. Saving starts no queue or model request.
2. Select Inspect reusable queue once. This explicitly reads one page of the modern gameforge queue. The Queue console displays the exact observed repository/remote, start/completion UTC, coverage, setup, owner/lease, recorded processor and grouped request records. Fields freeze during a panel action, and every result retains its original scope.
3. Use Next observed page or Previous observed page for that same target. Each click is a separate observation. Changing targets requires a first-page inspection. Pages do not form an atomic whole-queue snapshot; changing refs can move membership. Ref-name order is not execution order. Refresh explicitly to update a dated result.
4. Expand a request to read its source/base/dependencies, reported phase, candidate, version, attempts, recorded required-check results, receipt and synchronization outcomes where supplied. Missing fields remain absent/unknown. Queued is not delivered; Published still needs synchronization. Integrated is a reported outcome, not independent verification of remote ancestry or artifacts by this viewer.

The console is a selectable read-only panel, not a shell, runtime Quantum Console replacement or new queue service. It shows at most 64 local action events; these report actual dispatch/result/cancellation, not streamed publisher progress. Clearing history changes only the display. The underlying publisher remains independent.

## Setup for the selected repository

Inspect first. A recorded configuration shows its workspace, policy hash, required-check descriptors, repair enablement and whether the installed runner matches. These are recorded facts only: workspace ownership, tools, Commandments readiness, active service ownership and eligibility are not certified by this read. A damaged configuration is retained and reported; no defaults overwrite it.

For a new Unity repository, the existing Operator controls create a missing initial semantic-version record only for the active Unity project, then configure one new short workspace, the installed Unity executable and a required project test filter. Commit the initial version record to the repository's initial main before configuration. Every configured check remains required. Configuration does not start the publisher. Starting is a separate explicit confirmed action. See [the queue contract](AIUsability.md#publisher-ownership-and-recovery) and [saved-change workflow](QueueLocalChanges.md).

For a selected repository requiring its own native checks/version owner, choose an existing repository-owned required-policy JSON file and use Configure selected repository from reviewed policy. Review the complete file before confirmation: its commands run under the local account only when the publisher is later explicitly started; enabled repair may send project context and consume account allowance. This is an advanced owner-authored policy boundary, not a policy generator or general certification. Generic native adapters, derived identities and project-specific gates remain the repository owner's responsibility. The shared API is GitFlowHost.ConfigurePolicyAsync(preferences, workspace, policyPath, expectedPolicyFingerprint, cancellation); it reuses configure and its validation, binding confirmation to the selected file SHA-256 and refusing changed bytes. There is no skip, advisory conversion or automatic owner recovery.

Existing reservations are refused, never adopted or overwritten. Configuration can be interrupted between writing a workspace ownership marker and the repository reservation. Preserve that root and exact records for owner reconciliation; do not delete it and repeatedly create fresh roots. Installed-runner changes retain the existing explicit stopped-owner adoption procedure.

## Legacy boundary

Legacy IntegrationQueue uses refs/heads/codex/queue and codex/integration-lock. The modern adapter only counts its advertised refs and displays Unsupported existing queue. It does not parse legacy requests, superseded statuses or receipts, migrate records, adopt the processor or install a second queue beside it. Existing configure/submission guards continue refusing legacy coexistence. Use the existing queue owner and submission route until a separately authorized migration is designed. Original records remain untouched.

The lower legacy viewer and GitFlowBrick.Service retain their separate 45-second / 128-request adapter. More than 128 requests is an unavailable/partial legacy observation, never an empty queue. Unknown legacy schemas/statuses are not certified. New modern pagination does not increase that old adapter's compatibility. Its UI now withholds raw control/receipt JSON and failures; its API still retains original evidence for authorized callers.

## Bounds and truthful evidence

Publisher queue reads advertise the request refs once, verify cached commit identities/types, fetch all absent exact commits in one transport and read control blobs in batches of sixteen after checking each size. Ref names and request identities still must agree; malformed or unavailable records remain blockers. Individual sensitive ref/lease observations still query the remote freshly. This reuses immutable objects, never cached ref values, and leaves FETCH_HEAD, local refs, HEAD, index and working edits unchanged. It does not change a live configured runner: stopped-owner adoption remains explicit. Modern viewer reads already batch their page fetch into an isolated temporary store; this publisher improvement neither adopts nor accelerates an existing legacy processor automatically.

Modern inspect defaults to 16 requests; the direct CLI accepts --offset and --limit 1 through 64. Coverage reports advertised modern refs, returned records, failed records, records outside this page and a next offset. A nonzero omission, unsupported record or independent finding produces Partial. An interrupted remote read produces Unavailable, retaining independent setup findings. Empty readable results are not proof that an unsupported/failed queue is empty.

The adapter uses a temporary bare object store and fixed Git reads, with hooks, credential helpers, external/SSH transports and caller GIT_* overrides disabled. It supports public HTTPS without embedded credentials and absolute local remotes. Selected checkout HEAD, index, working files, FETCH_HEAD and object store are not changed by observation. It performs no PID probe, ownership lock acquisition, lease renewal or takeover. Process health and lease expiry remain unknown. A single lease object does not prove continued ownership.

There is one 45-second observation deadline. Declared control blobs are preflighted to one MiB before reading. Displayed request details are limited to 24 KiB each, nested lists to the latest 16 entries, string values to 512 characters and the aggregate JSON response to 768 KiB. The UI applies an independent 49,152-character projection bound and visible omission markers. These are display/control limits, not network pack/memory quotas: subprocess output is buffered before its post-read one-MiB check, and a remote can send a large Git pack before the deadline. Large or untrusted remote-resource acceptance remains unverified.

Commands, credentials, unknown fields and raw diagnostics/proposals are not dumped into the UI. Known diagnostic counts are numeric only. Failure presentation retains the reported phase, a fixed safe explanation, request/control identity and owner next step; recorded check folders remain selectable where supplied. Raw failure details must be read through the owning evidence workflow. Credential-like text and remote URLs are additionally redacted in displayed ordinary fields; this is not a promise to detect every possible secret placed in an authored title.

Cancel current panel request cancels only that view's owned host request and descendants. Timeouts/cancellation retain prior dated observations. A dispatched configuration/submission write may already have completed: inspect the original identity/configuration before retrying. Cancellation does not undo writes or stop the independent publisher. Closing the view cancels its bridge request; disabling the brick cancels lifecycle-owned requests. Repeated clicks are serialized and no automatic retry or refresh is introduced.

## Validation and extension boundary

### Assistant-readable failure records

For a failed modern request, read its structured diagnostic first: schema, actual failed stage, stable cause code/plain cause, outer/root exception types, check name/exit code or JSON location when known, UTC, relative owner evidence paths and safe next action. Unknown causes stay explicit; the record never guesses success or turns a timeout into stopped-owner proof. It is copied into the request/local state and shown through the existing bounded console projection. No diagnostic collector or repeated scan is started. Resolved synchronization diagnostics are marked resolved after the original receipt/pulls complete.

The owner also retains `Evidence/<request-id>/diagnostic.json` in the configured workspace and the named unique failure record. Read those exact paths and the listed check evidence only as needed. Relative gate paths start from that request evidence directory. Raw exception text/output remains in owner-local evidence and can contain private data; it is not copied into new request failure summaries or default CLI failure output. Historical records are preserved rather than rewritten. Preserve the exact request/candidate/version; uncertain publication or ownership requires reconciliation, never a duplicate request or bypass. Preflight failures may have no retained artifact; their cause remains unknown when the safe record cannot establish it.

Observe.py is called by the existing GitFlowQueue.py inspect command through GitFlowHost.ExecuteQueueAsync. GitFlowQueueConsole is presentation only; GitFlowPresentation applies the same safe output policy to modern and legacy UI. Supported God operation inspect_queue remains the legacy read-only operation; no new generic operation ID is invented.

Focused disposable regressions cover a 140-request queue, page navigation, repeated-read byte preservation, unknown/future/legacy metadata, malformed configuration/state/request shapes, timeout interruption and display bounds. See [current validation](Validation.md) for actual results. Unity minimum-version compilation, rendered normal/narrow embedded/standalone controls, native Cancel/Close/reopen and licensed-dependency checks remain required before publication. Historical 0.2.0 evidence cannot certify these changed sources.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{"packageId":"com.geurts.gameforge.gitflow","packageVersion":"0.3.0","dependencies":{"com.geurts.gameforge.god":"0.39.3","com.unity.nuget.newtonsoft-json":"3.2.2"}}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->
