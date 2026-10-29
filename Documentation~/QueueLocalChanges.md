YOUR SAVED UNITY CHANGE → QUEUED → INTEGRATED

Open this guide again with Help - Queue local changes in the GitFlow window, or Tools > Geurts Game Forge > GitFlow Help. Everything on this page is available inside Unity.

ONE-TIME SETUP
Enable GitFlow in God. Open Tools > Geurts Game Forge > GitFlow. Expand Queue host and local Codex. Select installed Python, this project's repository and its existing remote. Set Capture Scope to your task-owned folder, such as Assets/MyGame. Save project preferences. Codex model discovery is only needed for model features.

Your queue operator must configure and start the publisher once, with a short new workspace root, the installed Unity Editor and required project tests. Existing legacy queues require a reviewed migration; this button does not migrate them. A start request alone does not prove the publisher is running. Inspect reusable queue once shows the recorded owner and state.

EXAMPLE: CHANGE AN INSPECTOR VALUE
1. Change a saved enemy prefab's Movement Speed from 3 to 4. Keep its prefab and required .meta files in your Capture Scope.
2. In Queue saved local changes, optionally enter a useful title such as Increase enemy movement speed. Preview saved changes shows eligible saved paths and exclusions; it does not save unsaved Inspector or scene changes.
3. Select Queue local changes. GitFlow saves assets through Unity and prompts to save modified scenes. Give untitled scenes a path first. Cancelling saving stops capture. Close Play Mode and wait for imports or compilation first.
4. Read Last exact result. A verified state of queued and a request ID mean the exact source snapshot is in the shared queue. Keep that ID. Your live branch, staged files and working edits stay as they were; the snapshot uses a separate Git index and does not require a Git client or branch push.
5. Use Inspect reusable queue once for progress. Preparing and validating mean work is underway. Published means main changed. Integrated means the recorded publication and required checkout synchronization completed. Queued does not mean delivered.

WHAT THE BUTTON INCLUDES
It captures saved tracked changes, deletions and eligible untracked files in Capture Scope, plus their required Unity asset/folder metadata. Git ignores are respected. Caches, generated folders, licensed/vendor folders, validation/tool folders, queue-owned version records and package manifests are excluded. The result lists actual included files and exclusions. No ignored files are forced into Git.

Choose a narrow scope when unrelated edits are present. GitFlow cannot infer which of two edits in the same file belongs to your task. The snapshot includes the current branch's committed history; use an appropriate task branch when its earlier commits are unrelated. Basic capture does not switch branches, stash, reset, remove edits, copy a Unity Library, or push main.

VALIDATION AND DELIVERY
The publisher runs required validation on the combined candidate before publication. Do not repeat unchanged checks just to watch progress. Failed required checks block the queue; there is no skip or bypass. Automatic repair is separately authorized and uses the selected local Codex model for up to three hours from the original start, stopping earlier when it makes no meaningful source progress.

The owner observation shows machine, owner identity, request, stage and last recorded renewal. These are dated observations; a recorded process ID or timestamp alone does not prove health or expiry. Unknown is not available. Ask your queue operator to reconcile a blocked or uncertain owner rather than starting competing processors.

COMMON FIXES
• No eligible changes: save your assets/scenes and check Capture Scope and the exclusions. The button reads saved files, not unsaved memory.
• Checkout changed during capture: finish the edit/import or Git operation, review the refreshed paths, then capture again. Work is preserved.
• Submission response uncertain: inspect the request ID before trying again. An unchanged snapshot has the same source identity and reuses its queue entry, even if the first response was lost. Do not repeatedly resubmit.
• Validation failed: read the request's failure and retained evidence. Keep your work. The operator repairs and explicitly continues that same request; a second request cannot jump over it.
• Published while your original checkout is dirty: main contains the delivery, but GitFlow refuses to overwrite your working files. Use your Git client with the operator to reconcile the preserved edits and cleanly pull. Do not discard work merely to clear this state. The publisher resumes synchronization without a new version or duplicate validation.
• A change was outside scope or excluded: review it separately with the operator. Do not widen scope to include unrelated work or disable validation.

OPTIONAL: SUBMIT AN EXISTING COMMIT
Submit committed work accepts a clean committed task branch. Save Unity, review task-owned files and necessary metadata in your Git client, commit with a meaningful message, then fill Title, Implementation summary and Version increment and select Submit exact source once. This route preserves the existing commit's identity. An ordinary Git push does not automatically enqueue. Direct pushes to main bypass the queue's integration review; use the repository's existing branch protection policy. GitFlow does not change hosting permissions or branch rules.

This is the human workflow guide. Documentation~/AIUsability.md separately defines supported automation APIs and evidence requirements.
