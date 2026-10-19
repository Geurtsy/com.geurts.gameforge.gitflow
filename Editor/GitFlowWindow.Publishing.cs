// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Geurts.GameForge.God;
using Geurts.GameForge.God.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Editor
{
    public sealed partial class GitFlowWindow
    {
        private TextField _publisherRoot, _runner, _publisherRemote, _versionFile, _protectedPaths, _resumeId, _resumeReason;
        private VisualElement _gateList;
        private Toggle _authorize, _headless;
        private TextField _policyPreview;
        private Button _applyPolicy;
        private GitFlowPolicyReview _policyReview;
        private readonly List<GateFields> _gateFields = new List<GateFields>();
        private bool _workflowBusy;
        private Foldout _policySection;
        private HelpBox _policyFinding;

        /// <summary>Holds only live control references, with no persisted execution state or automatic commands.</summary>
        private sealed class GateFields
        {
            internal TextField Id, Executable, Arguments, ToolFiles;
            internal IntegerField Timeout;
        }

        /// <summary>Builds local drafts, Help, acknowledgement and deliberate controls; loading God settings never starts work.</summary>
        private void BuildPublishingTabs(TabView tabs)
        {
            _gateFields.Clear(); _policyReview = null;
            _authorize = new Toggle("Authorize one explicit action using the values shown") { value = false };
            _authorize.tooltip = "Required for each save, submit or publisher action. It resets when used; refresh and review stay read-only.";
            rootVisualElement.Add(_authorize);
            var submit = new Tab("Submit"); tabs.Add(submit); var submitScroll = new ScrollView(); submit.Add(submitScroll);
            var handoff = Section(submitScroll, "Exact source handoff", true);
            handoff.Add(new HelpBox("Uses the repository and existing remote selected in Inspect. Submit an exact committed source and its integrated base once. The worker returns its queue ID and stops; submission never starts or waits for a publisher.", HelpBoxMessageType.Info));
            var source = Field(handoff, "Source commit (40 characters)", "");
            var integratedBase = Field(handoff, "Integrated base commit (40 characters)", "");
            var increment = new DropdownField("Version increment", new List<string> { "patch", "minor", "major" }, 0); handoff.Add(increment);
            var title = Field(handoff, "Request title", "");
            var detail = Field(handoff, "Scope/reason, dependencies, evidence/environment and unverified checks with next steps", "", true);
            handoff.Add(new Button(() => RunWorkflow(() => Execute("submit_request", new GitFlowSubmission(_repository.value, source.value, integratedBase.value, increment.value, title.value, detail.value)), "Submit")) { text = "Submit exact source once" });
            var submitHelp = Section(submitScroll, "Help", false);
            submitHelp.Add(new HelpBox("Prepare/commit the task through its owning Git workflow. Leave final version assignment to the publisher. The committed main policy must already declare the plain version file and required checks. Source edits to policy, controllers, version metadata and queue internals are rejected. Repeated exact snapshots reuse the original ID, receipt and failure. A changed base/increment cannot silently replace an existing request. Do not keep refreshing or waiting after the confirmed handoff. API: GitFlowBrick.QueueService.SubmitAsync(GitFlowSubmission).", HelpBoxMessageType.Info));
            StyleRows(handoff); StyleRows(submitHelp);

            var publisher = new Tab("Publisher"); tabs.Add(publisher); var scroll = new ScrollView(); publisher.Add(scroll);
            var policy = Section(scroll, "Committed project policy", true);
            _policySection = policy;
            policy.Add(new HelpBox("This generic adapter supports direct atomic main publication only. Projects requiring PR review, immutable release assets, derived native identities or additional version metadata need their owning adapter. Declare all validation/controller paths as protected. Commit/review the saved policy through that repository's normal workflow before using the queue.", HelpBoxMessageType.Warning));
            _versionFile = Field(policy, "Plain SemVer file (repository relative)", "VERSION");
            _protectedPaths = Field(policy, "Protected controller/gate paths (one per line)", "Tools/Validation", true);
            _headless = new Toggle("Every required command and helper is silent and supports unattended operation"); policy.Add(_headless);
            _gateList = new VisualElement(); policy.Add(_gateList);
            policy.Add(new Button(() => AddGate(new GitFlowGatePreference())) { text = "Add required validation command" });
            policy.Add(new Button(() => RunWorkflow(async () =>
            {
                _policyReview = await ReviewPolicyControlsAsync();
                if (!_closed) { _policyPreview.value = _policyReview.PolicyJson; _applyPolicy.SetEnabled(true); _status.text = "Read-only policy review ready. Check the exact path and commands, then authorize Save reviewed policy file. " + _policyReview.Path; }
            }, "Policy review")) { text = "Review policy without saving" });
            _policyPreview = Field(policy, "Exact reviewed policy (read-only)", "Not reviewed", true); _policyPreview.isReadOnly = true;
            _policyFinding = new HelpBox("Not checked — review the current version path, protected controls and required commands.", HelpBoxMessageType.Info); policy.Add(_policyFinding);
            _applyPolicy = new Button(() => RunWorkflow(() => Execute("apply_policy", _policyReview), "Save policy")) { text = "Save reviewed policy file" }; _applyPolicy.SetEnabled(false); policy.Add(_applyPolicy);
            _versionFile.RegisterValueChangedCallback(change => InvalidatePolicy()); _protectedPaths.RegisterValueChangedCallback(change => InvalidatePolicy()); _headless.RegisterValueChangedCallback(change => InvalidatePolicy());
            _repository.RegisterValueChangedCallback(change => InvalidatePolicy());

            var host = Section(scroll, "Dedicated independent host", true);
            _publisherRoot = Field(host, "Dedicated empty publisher root", ""); _publisherRemote = Field(host, "Reviewed remote URL or absolute local path", ""); _runner = Field(host, "Matching released GeurtsGitFlow.exe", "");
            host.Add(new Button(() => { string path = EditorUtility.OpenFilePanel("Choose the extracted matching release runner", "", "exe"); if (!string.IsNullOrEmpty(path)) _runner.value = path; }) { text = "Choose released runner…" });
            host.Add(new Button(() => RunWorkflow(() => Execute("configure_publisher", new GitFlowPublisherOptions { root = _publisherRoot.value, remote = _publisherRemote.value }), "Configure host")) { text = "Configure stopped dedicated root" });
            host.Add(new Button(() => RunWorkflow(() => Execute("start_publisher", StartPayload()), "Start host")) { text = "Start independent publisher" });
            host.Add(new Button(() => RunWorkflow(() => Execute("stop_publisher", new GitFlowHostStop(_publisherRoot.value)), "Stop host")) { text = "Request graceful stop between requests" });
            var recovery = Section(scroll, "Original-request continuation", false);
            recovery.Add(new HelpBox("Stop and prove the configured owner/children have exited before maintenance. This release resumes only the exact clean recorded candidate on its unchanged integrated main/policy, or finishes an already published receipt/pull. Conflicted/dirty or changed candidates stay blocked; their bytes/index are preserved. It never resets budgets, replaces runners or assigns another version.", HelpBoxMessageType.Warning));
            _resumeId = Field(recovery, "Original failed/published request ID", ""); _resumeReason = Field(recovery, "Reviewed changed inputs or continuation reason", "", true);
            recovery.Add(new Button(() => RunWorkflow(() => Execute("start_publisher", StartPayload(_resumeId.value, _resumeReason.value)), "Same-request continuation")) { text = "Resume original request once" });
            var preferences = Section(scroll, "Local form drafts", false);
            preferences.Add(new Button(SaveDrafts) { text = "Save form drafts through God" });
            preferences.Add(new HelpBox("Only the local form values are saved here. No configuration, remote check, runner start or auto-update occurs when this view opens. Malformed/future saved settings remain unchanged and require their owner.", HelpBoxMessageType.Info));
            var help = Section(scroll, "Help", false);
            help.Add(new HelpBox("1. In Inspect, choose the owning generic repository. In Committed project policy, select its plain version file, protect the controlling scripts, add each required native console executable and literal arguments (one argument per line), and set its timeout. Review, authorize and save; commit/review through the owning project's Git workflow.\n2. Extract the matching immutable Windows runner release. Choose its GeurtsGitFlow.exe and a short empty dedicated root; enter the reviewed remote. Configure, then explicitly start. The process runs without this window, Editor or chat. Saved runner hashes must match this package; upgrades are deliberate stopped-owner operations.\n3. Workers submit once from Submit. Refresh Inspect deliberately for dated queue/health/lease/receipt evidence. Stop is graceful and does not prove stopped ownership.\n\nRequired commands have no waiver or AI fallback. Native console executables are hash-pinned; GUI/audio helpers are unsupported. Optional private HTTPS access uses an externally supplied GEURTS_GITFLOW_TOKEN, never a browser/credential dialog or saved form secret. Missing access remains a failure. Local remotes need no credential. No Dreadvault runner or consumer is selected automatically.\n\nAPI: GitFlowConfiguration.ReviewPolicyAsync / ApplyPolicy / ConfigurePublisher / StartPublisher / RequestStop; the no-code controls use these same services through God operation guards. Detailed contracts and limits: local Documentation~/Publisher.md.", HelpBoxMessageType.Info));
            foreach (VisualElement section in new VisualElement[] { policy, host, recovery, preferences, help }) StyleRows(section);
            LoadDrafts();
        }

        /// <summary>Returns the enabled shared configuration service without manufacturing a second owner.</summary>
        private static GitFlowConfiguration Configuration() => GitFlowBrick.ConfigurationService ?? throw new InvalidOperationException("Enable GitFlow in God first.");

        /// <summary>Reviews the exact currently authored no-code fields through the shared API, refusing changes while asynchronous hashing runs.</summary>
        public async Task<GitFlowPolicyReview> ReviewPolicyControlsAsync()
        {
            if (!_headless.value) throw new InvalidOperationException("Confirm the commands' silent unattended behavior before reviewing policy. GUI/audio commands cannot be launched safely in the background.");
            string repository = _repository.value, version = _versionFile.value, paths = _protectedPaths.value;
            GitFlowGate[] gates = _gateFields.Select(row => new GitFlowGate { Id = row.Id.value, Executable = row.Executable.value, AdditionalToolFiles = Lines(row.ToolFiles.value), Arguments = Lines(row.Arguments.value), TimeoutSeconds = row.Timeout.value }).ToArray();
            string key = PolicyControlKey(); GitFlowPolicyReview review = await Configuration().ReviewPolicyAsync(repository, version, Lines(paths), gates);
            if (_closed || key != PolicyControlKey()) throw new InvalidOperationException("Policy controls changed during review. Review the current values again.");
            return review;
        }

        /// <summary>Captures complete local authoring values for stale-review comparison without any file or network operation.</summary>
        private string PolicyControlKey() => _repository.value + "\n" + _versionFile.value + "\n" + _protectedPaths.value + "\n" + _headless.value + "\n" +
            string.Join("\n", _gateFields.Select(row => row.Id.value + "\0" + row.Executable.value + "\0" + row.Arguments.value + "\0" + row.ToolFiles.value + "\0" + row.Timeout.value));

        /// <summary>Builds a complete literal command row, preserving authored values while editing.</summary>
        private void AddGate(GitFlowGatePreference value)
        {
            if (_gateFields.Count >= 8) { _status.text = "Warning — at most eight required commands are supported."; return; }
            var foldout = Section(_gateList, "Required validation command", true);
            var row = new GateFields { Id = Field(foldout, "Unique command ID", value.id), Executable = Field(foldout, "Native console executable", value.executable), Arguments = Field(foldout, "Literal arguments (one per line)", value.arguments, true), Timeout = new IntegerField("Timeout seconds (1–3600)") { value = value.timeoutSeconds } };
            row.ToolFiles = Field(foldout, "Additional external tool dependencies (absolute paths, one per line)", value.additionalToolFiles, true);
            foldout.Add(row.Timeout); foldout.Add(new Button(() => { string path = EditorUtility.OpenFilePanel("Choose a silent native console tool", "", "exe"); if (!string.IsNullOrEmpty(path)) row.Executable.value = path; }) { text = "Choose executable…" });
            foldout.Add(new Button(() => { _gateFields.Remove(row); foldout.RemoveFromHierarchy(); InvalidatePolicy(); }) { text = "Remove this command draft" });
            foreach (TextField field in new[] { row.Id, row.Executable, row.Arguments, row.ToolFiles }) field.RegisterValueChangedCallback(change => InvalidatePolicy());
            row.Timeout.RegisterValueChangedCallback(change => InvalidatePolicy()); _gateFields.Add(row); InvalidatePolicy(); StyleRows(foldout);
        }

        /// <summary>Invalidates reviewed commands immediately after relevant authoring changes without running a check on repaint.</summary>
        private void InvalidatePolicy()
        {
            _policyReview = null; _applyPolicy?.SetEnabled(false);
            if (_policySection != null) _policySection.text = "Committed project policy — not reviewed";
            if (_policyFinding != null) { _policyFinding.text = "Not checked — values changed; review the complete current policy before saving."; _policyFinding.messageType = HelpBoxMessageType.Info; }
        }

        /// <summary>Creates a typed trusted start/recovery request from current controls and the installed package's bound manifest.</summary>
        private GitFlowHostStart StartPayload(string id = "", string reason = "")
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GitFlowBrick).Assembly);
            if (package == null) throw new InvalidOperationException("Installed package identity is unavailable.");
            string manifest = File.ReadAllText(Path.Combine(package.resolvedPath, "Tools~/RunnerRelease.json"));
            return new GitFlowHostStart(_publisherRoot.value, _runner.value, manifest, id, reason);
        }

        /// <summary>Consumes one explicit acknowledgement and dispatches through God's supported mutation admission to shared services.</summary>
        private async Task Execute(string operation, object payload)
        {
            if (!_authorize.value) throw new InvalidOperationException("Review the shown values and authorize this one explicit action first.");
            _authorize.value = false;
            var request = new ForgeOperationRequest(GitFlowBrick.PackageId, operation, arguments: new Dictionary<string, string> { ["confirmed"] = "true" }, payload: payload);
            ForgeOperationResult result = await ForgeOperations.ExecuteAsync(request);
            if (!_closed) _status.text = operation.Replace('_', ' ') + " — " + result.Status + ": " + result.Detail;
            if (result.Status == FORGE_OPERATION_STATUS.FAILED || result.Status == FORGE_OPERATION_STATUS.UNAVAILABLE) ForgeLog.Error("GitFlow " + operation + ": " + result.Detail);
        }

        /// <summary>Observes one workflow task and shows errors inline; no operation is silently retried or polled.</summary>
        private async void RunWorkflow(Func<Task> action, string label)
        {
            if (_workflowBusy) { _status.text = "Busy — finish the current bounded action first."; return; }
            _workflowBusy = true;
            try { await action(); }
            catch (Exception error)
            {
                if (!_closed)
                {
                    _status.text = label + " — Error: " + error.GetType().Name + ": " + error.Message;
                    if (label == "Policy review") { _policySection.text = "✖ Error — Committed project policy"; _policyFinding.messageType = HelpBoxMessageType.Error; _policyFinding.text = "Error — " + error.Message + " Correct the named values and review again."; }
                }
            }
            finally { _workflowBusy = false; }
        }

        /// <summary>Loads local drafts only; damaged God settings remain intact and visible.</summary>
        private void LoadDrafts()
        {
            try
            {
                GitFlowPreferences value = GitFlowBrick.Settings?.Load<GitFlowPreferences>(GitFlowBrick.PackageId) ?? new GitFlowPreferences();
                if (!string.IsNullOrEmpty(value.repository)) _repository.value = value.repository;
                _remote.value = value.remoteName; _publisherRoot.value = value.publisherRoot; _publisherRemote.value = value.publisherRemote; _runner.value = value.runnerExecutable;
                _versionFile.value = value.versionFile; _protectedPaths.value = value.protectedPaths;
                foreach (var gate in value.gates) AddGate(gate);
                if (_gateFields.Count == 0) AddGate(new GitFlowGatePreference());
            }
            catch (Exception error) { _status.text = "Warning — saved drafts retained unchanged: " + error.Message; }
        }

        /// <summary>Saves only explicit local form values through God; no host or repository policy is changed.</summary>
        private void SaveDrafts()
        {
            try
            {
                if (!_authorize.value) throw new InvalidOperationException("Authorize this explicit settings save first."); _authorize.value = false;
                if (GitFlowBrick.Settings == null) throw new InvalidOperationException("Enable GitFlow in God first.");
                var value = new GitFlowPreferences { repository = _repository.value, remoteName = _remote.value, publisherRoot = _publisherRoot.value, publisherRemote = _publisherRemote.value, runnerExecutable = _runner.value,
                    versionFile = _versionFile.value, protectedPaths = _protectedPaths.value,
                    gates = _gateFields.Select(row => new GitFlowGatePreference { id = row.Id.value, executable = row.Executable.value, arguments = row.Arguments.value, additionalToolFiles = row.ToolFiles.value, timeoutSeconds = row.Timeout.value }).ToArray() };
                GitFlowBrick.Settings.Save(GitFlowBrick.PackageId, value); _status.text = "Saved local drafts through God. No host or policy was changed.";
            }
            catch (Exception error) { _status.text = "Settings save — Error: " + error.Message; }
        }

        /// <summary>Splits authoring lines into literal arguments/paths, without shell or JSON interpretation.</summary>
        private static string[] Lines(string value) => (value ?? "").Replace("\r\n", "\n").Split('\n').Where(line => !string.IsNullOrEmpty(line)).ToArray();
        /// <summary>Creates a complete wrapping editable field with a stable local label.</summary>
        private static TextField Field(VisualElement parent, string label, string value, bool multiline = false)
        { var field = new TextField(label) { value = value ?? "", multiline = multiline, name = label }; field.style.marginTop = 8; parent.Add(field); return field; }
    }
}
