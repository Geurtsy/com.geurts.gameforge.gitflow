# Queue your Unity changes

Make a change, save it, click Queue local changes, and check the confirmation. The queue is a waiting list. A background program checks and combines the waiting changes before they join the shared accepted project.

Open this guide any time with Help - Queue local changes in the GitFlow window, or Tools > Geurts Game Forge > GitFlow Help. It stays inside Unity.

TRY A SMALL CHANGE
1. Change an enemy prefab's Movement Speed from 3 to 4 in the Inspector. Save the prefab and your scene.
2. Open Tools > Geurts Game Forge > GitFlow. Expand Queue saved local changes. An optional title such as Increase enemy movement speed makes the change easier to recognize.
3. Click Queue local changes. If Unity asks you to save a scene, save it. Give an untitled scene a filename first. Cancelling stops this action and keeps your work.
4. Look for Queued. This confirms that a recorded checkpoint of your saved changes is on the waiting list. You do not need a separate Git client, terminal, manual commit or branch push for this action.
5. Click Inspect reusable queue once when you want an update. Wait for the background checks and delivery; you do not need to repeat the same tests yourself.

UNDERSTAND THE RESULT
Queued: received, waiting for checks. Queued does not mean delivered.
Preparing / Validating: the background program is combining or checking the changes.
Published: the checked changes have reached main, the shared accepted version. Required local synchronization is still pending.
Integrated: delivery and required local synchronization have completed.
Blocked / Validation failed: work stopped for a specific problem. Your work is retained. There is no skip button.

WHAT GETS INCLUDED?
Capture Scope is the project folder selected during setup. The button takes saved changes in that folder and the Unity metadata that identifies those assets. Preview saved changes shows the saved files it would include and any excluded files. It does not save unsaved work itself.

Use a narrow task folder, such as Assets/MyGame/Enemies, if you are also working on something unrelated. The tool cannot distinguish two different tasks inside the same file. Cached, generated, licensed/vendor and validation/tool files are excluded, as are queue-owned version records and package manifests. Git's ignore rules still apply.

Your current files and staging remain in place. Staging is a Git client's selection of files for its next checkpoint; GitFlow uses a separate selection without changing yours. Your branch, a separate line of work, also stays in place. Earlier committed work on that branch is part of the snapshot's history, so use an appropriate task branch if that history contains unrelated work. A commit means a recorded checkpoint.

FIRST-TIME SETUP
Enable GitFlow in God. In Queue host and local Codex, choose installed Python, this project's repository folder and its existing remote (normally origin). Choose Capture Scope, then Save project and model preferences. You can leave model selection empty for ordinary queue use.

Your queue operator configures the background program once using Operator - Configure and start publisher. It needs a short new workspace location, the installed Unity Editor and required project tests. Model discovery is only needed for model features. The queue does not migrate an existing older queue automatically. A start request needs a subsequent status check before it is considered ready.

THE BACKGROUND PROGRAM AND COMMON FIXES
The processor is the background program that checks, combines and publishes changes. Publication means those changes reach the shared accepted version. Inspect reusable queue once shows its recorded computer, owner, current work and latest recorded update. These are dated observations, not a guarantee that it is healthy. Unknown does not mean available. The advanced Details panel retains the complete results for your operator.

• No eligible changes: save your scene/assets and check Capture Scope and the preview's excluded list.
• Unsaved or untitled scene: save it first. Save unsaved Prefab Mode work before queueing. Play Mode, compilation and asset import must finish first. If a selected scene and unrelated assets both have unsaved edits, GitFlow stops so Unity cannot save that unrelated work through its scene-save flow.
• Files changed during capture: finish editing or importing, review the preview, then try again. Nothing was discarded.
• Uncertain confirmation: inspect the existing request first. The same unchanged snapshot reuses its original queue entry. Do not repeatedly submit it.
• Failed checks: ask the operator to inspect the recorded failure and continue that same request after a correction. Later requests cannot jump over it.
• Published but not Integrated: your original files may still contain local edits. GitFlow refuses to overwrite them. Ask the operator to reconcile those preserved edits and synchronize through supported Git tooling. Do not discard work merely to clear the message. Recovery does not assign another version or repeat completed checks.

Forge packages and documentation never update automatically through GitFlow. After you deliberately install an update and stop the publisher, the operator can explicitly adopt the already installed runner; that action installs nothing and retains existing work.

Automatic repair is disabled until the operator enables its separate authorization control. Selecting a model does not enable repair. When authorized, it uses that local Codex model, keeps all attempt history, and has up to three hours from its original start. Restarts and model changes never reset that clock. It stops sooner after three consecutive attempts without meaningful progress. It can make bounded corrections to eligible source files; it cannot edit protected tests, validation rules, native settings or vendor files. Required checks still apply.

OPTIONAL EXISTING-COMMIT ROUTE
If you already use a Git client, Submit committed work accepts a clean recorded checkpoint: save Unity, select only task-owned changes and necessary metadata, record a meaningful commit, then fill Title, Implementation summary and Version increment and click Submit exact source once. This is optional; basic Queue local changes creates its own checkpoint.

An ordinary Git push does not add work to the queue automatically. Direct pushes to main bypass this queue review. Follow the repository's existing branch protection policy; GitFlow does not change hosting permissions or rules.

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
