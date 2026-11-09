# Existing IntegrationQueue owner adapter

Version 0.4.0 source candidate. This bridge reuses a configured game-owned processor, existing codex/queue control refs, locks, leases, required checks, native identity/version/shader/runtime gates, repair routing and publication. It never installs a second modern gameforge queue or copies the processor into a competing implementation.

## Inspect first

```powershell
python '<GitFlowPackage>/Tools~/Queue/GitFlowExistingRunner.py' inspect --repository '<existing game checkout>'
```

Inspection reads the existing configuration and invokes only its supported processor-status. The result includes exact non-secret source/configuration hashes and real local owner observation; it does not prove remote lease expiry or acquire publisher ownership. The runner and integration workspace must belong to the same Git common repository. Changed/untracked Python cohort files, links, unknown APIs, or a competing local modern reservation fail closed. No queue setup, model inference or adoption starts from inspection.

## Explicit stopped-owner adoption

Keep the existing publisher running while preparing the adapter. At its next approved handoff use the existing graceful stop-after-current command, wait for actual stopped ownership and native Unity completion, then inspect again. Do not kill Unity, remove leases/locks, reset/discard retained work or change the integration root.

```powershell
python '<GitFlowPackage>/Tools~/Queue/GitFlowExistingRunner.py' adopt --repository '<same checkout>' --expected-fingerprint '<exact inspected scopeFingerprint>' --destination '<fresh private machine-local record.json>' --operator
python '<GitFlowPackage>/Tools~/Queue/GitFlowExistingRunner.py' status --record '<record.json>'
```

Adoption refuses a live owner or uncertain native Unity lock. It writes only a fresh private adapter record binding the exact existing configuration, committed Python cohort, root and interpreter, with a modification-detection checksum. It changes no queue/configuration/policy/model/global settings or Git refs. Changing any bound source/configuration/interpreter requires explicit reviewed re-adoption; unrelated committed game changes do not invalidate an unchanged runner cohort.

An independently hosted explicit caller may use start, resume or request-stop with --record and --operator. These delegate respectively to processor-start, processor-start --resume --operator and processor-stop. Already running owners are reused. No skip, forced reset, root replacement, lock deletion, task reordering or model override is exposed. The original processor remains authoritative and can reject actual failures. Graceful stop is a request, not stopped-owner proof. The adapter itself contains no supervisor, schedule or daemon.

## Host and takeover boundary

Do not start a persistent publisher through GitFlow's existing bounded Editor inspection job: that job intentionally kills its descendants on completion/cancel. The bridge refuses start/resume inside a Windows process job rather than allowing an apparently successful publisher to vanish. Use request-host below to delegate through the existing verified independent boot supervisor. No new task/service is installed by this bridge. The source integration has actual isolated Windows lifecycle proof; it is not deployed to Dreadvault.

Full takeover still requires the new validated package to be installed manually, explicit stopped-owner adoption, supported supervisor integration, native package/behavior/zero-warning qualification and one real queue cycle. Current Dreadvault installed GitFlow0.1.2 does not gain this capability because source files exist on a remote branch. Existing Archangels boot supervision continues to call the actual game runner until that separate integration is validated and explicitly adopted. Do not configure modern GitFlow beside it.

## Verification

Six real-Git/protocol-fixture regressions cover read-only preservation, active-owner refusal, changed scope/config/source/record/interpreter boundaries, exact resume flags, duplicate-owner reuse, graceful stop routing, job-contained start refusal and unrelated committed game changes. The owned test runner is a protocol fixture, not real game integration proof. Actual Dreadvault read-only inspection observed the genuine active owner and preserved its source/control state; no bridge adoption or live lifecycle action was performed. Modern host and native UI tests retain separate acceptance.

<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->
```json
{"packageId": "com.geurts.gameforge.gitflow", "packageVersion": "0.4.0", "dependencies": {"com.geurts.gameforge.god": "0.44.0", "com.unity.nuget.newtonsoft-json": "3.2.2"}}
```
<!-- GEURTS-RELEASE-CONTEXT:END -->

## Supported independent-host delegation

The installed Archangels host must advertise existing-runner-host-v1. A package version alone does not establish that its separately deployed worker was explicitly updated. The host must already be enabled/running, GitFlow must actually be resolved, and the host repository/runner hashes must match the reviewed adoption record. The client verifies the exact task action/executable/working directory, startup trigger, deployment hash and same Windows account SID. Missing or mismatched context fails without creating an alternate service.

```powershell
python '<GitFlowPackage>/Tools~/Queue/GitFlowExistingRunner.py' request-host --repository '<same selected checkout>' --record '<record.json>' --host-state '<existing Archangels machine-state folder>' --operation resume --operator
```

Operations are start, resume and request-stop. The API is GitFlowHost.RequestExistingHostAsync(preferences, adoptionRecord, hostStateDirectory, operation, operatorConfirmed, cancellation). Its existing bounded Editor transport only writes a narrow scoped request; the independent host executes the original owner command. A durable claim precedes execution. Unknown/interrupted outcomes produce needs_reconciliation and never replay that request automatically. Read the exact returned receiptPath and current owner state before retrying. Dispatched means the owner command returned, not that a game request was delivered. Cancellation or closing a client cannot undo an already written request. No arbitrary executable, command, model setting or queue policy is accepted in the protocol.

Actual Windows proof: the unmodified GitFlowProcess kill-on-close transport dispatched a request and its client exited; the separate S4U startup host later launched a harmless publisher. Duplicate resume kept one launch. Disabling/exiting that supervisor also preserved the publisher. The original fixture stop command then ended it, and the temporary task was removed. The fixture used simulated resolver metadata and no game, model or real queue operations. See Evidence/0.4.0/HostDelegation.json. This establishes lifecycle interoperability, not released-package installation or game acceptance.

The selected repository is separately required on hosted requests and must match the adoption record before any host lookup/write. C# passes its currently selected preferences.Repository. Current provenance reads batch the Python-only dirty/tracked checks while still hashing every member, refusing untracked/changed files and preserving the original exact scope identity. Final affected regressions cover both changes.
