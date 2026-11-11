// IMPORTANT: This script must comply with GeurtsGameForgeDocumentation/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeDocumentation/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geurts.GameForge.God.Editor;
using Geurts.GameForge.God;
using Newtonsoft.Json.Linq;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Visible Odin model configuration and explicit queue controls shared by standalone and embedded views.</summary>
    internal sealed class GitFlowControls : IDisposable
    {
        /// <summary>Editable authoring model; choices come only from the installed Codex catalogue.</summary>
        [Serializable]
        internal sealed class Configuration
        {
            [FoldoutGroup("Local tools", Expanded = true), BoxGroup("Local tools/Paths", ShowLabel = false), Sirenix.OdinInspector.FilePath(Extensions = "exe")]
            [Tooltip("Installed Python 3.10 or newer. No runtime is downloaded automatically.")]
            public string PythonExecutable;
            [FoldoutGroup("Local tools"), BoxGroup("Local tools/Paths"), Sirenix.OdinInspector.FilePath(Extensions = "exe")]
            [Tooltip("Installed native codex.exe. Codex owns its existing sign-in.")]
            public string CodexExecutable;
            [FoldoutGroup("Model selection", Expanded = true), BoxGroup("Model selection/Values", ShowLabel = false), ValueDropdown(nameof(ModelChoices))]
            [Tooltip("Exact model returned by local Codex. Saving does not authorize an account request.")]
            public string Model;
            [FoldoutGroup("Model selection"), BoxGroup("Model selection/Values"), ValueDropdown(nameof(EffortChoices))]
            [Tooltip("Reasoning effort supported by the selected model. No fallback is selected automatically.")]
            public string ReasoningEffort;
            [FoldoutGroup("Repository", Expanded = true), BoxGroup("Repository/Target", ShowLabel = false), FolderPath]
            [Tooltip("Existing project checkout. Queue local changes preserves its branch and staging. The optional committed-work route requires a clean commit.")]
            public string Repository;
            [FoldoutGroup("Repository"), BoxGroup("Repository/Target")]
            [Tooltip("Existing Git remote name; this control does not change remotes.")]
            public string Remote = "origin";
            [FoldoutGroup("Repository"), BoxGroup("Repository/Target")]
            [Tooltip("Task-owned folder captured by Queue local changes. Choose a narrower folder when other work is present. Generated, licensed vendor and validation files are excluded.")]
            public string CaptureScope = "Assets";
            [NonSerialized] public JArray Models = new JArray();

            /// <summary>Returns provider-reported display labels paired with their exact model identities.</summary>
            private IEnumerable<ValueDropdownItem<string>> ModelChoices()
                => Models.Select(item => new ValueDropdownItem<string>((string)item["display_name"], (string)item["model"]));

            /// <summary>Returns only efforts supported by this exact discovered model.</summary>
            private IEnumerable<string> EffortChoices()
                => Models.FirstOrDefault(item => (string)item["model"] == Model)?["efforts"]?.Values<string>() ?? Enumerable.Empty<string>();

            /// <summary>Copies non-secret authoring fields into the shared settings contract.</summary>
            internal GitFlowPreferences Preferences() => new GitFlowPreferences { PythonExecutable = PythonExecutable ?? "", CodexExecutable = CodexExecutable ?? "",
                Model = Model ?? "", ReasoningEffort = ReasoningEffort ?? "", Repository = Repository ?? "", Remote = Remote ?? "", CaptureScope = CaptureScope ?? "Assets" };
        }

        private readonly Configuration _configuration;
        private readonly PropertyTree _tree;
        private ForgeEditorTheme _theme;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly HelpBox _status;
        private readonly TextField _output;
        private readonly VisualElement _configurationView;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly GitFlowQueueConsole _console;
        private readonly Button _cancel;
        private readonly IVisualElementScheduledItem _liveUpdates;
        private CancellationTokenSource _requestCancellation;
        private GitFlowPreferences _actionPreferences;
        private bool _busy, _disposed;

        /// <summary>Creates controls and loads local settings only; no discovery, inference or queue action runs on opening.</summary>
        internal GitFlowControls(VisualElement parent, VisualElement feedbackParent = null)
        {
            var group = new Foldout { text = "Queue host and local Codex â€” Not checked", value = false };
            group.AddToClassList("forge-card"); group.style.marginTop = 16; group.style.paddingLeft = group.style.paddingRight = 12; parent.Add(group);
            _configuration = new Configuration { Repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) };
            string failure = null;
            try
            {
                var saved = GitFlowBrick.Host?.Load() ?? ReadSavedPreferences(_configuration.Repository);
                if (saved != null)
                {
                    _configuration.PythonExecutable = saved.PythonExecutable; _configuration.CodexExecutable = saved.CodexExecutable;
                    _configuration.Model = saved.Model; _configuration.ReasoningEffort = saved.ReasoningEffort;
                    if (!string.IsNullOrEmpty(saved.Repository)) _configuration.Repository = saved.Repository;
                    _configuration.Remote = saved.Remote;
                    _configuration.CaptureScope = saved.CaptureScope ?? "Assets";
                }
            }
            catch (Exception error) { failure = "Error - " + error.GetType().Name + ": saved GitFlow settings could not be loaded. Original bytes are retained; review the owning settings file before retrying."; }
            _tree = PropertyTree.Create(_configuration);
            _configurationView = new IMGUIContainer(() => { if (_theme == null) _theme = new ForgeEditorTheme(); using (_theme.Scope()) _tree.Draw(false); });
            group.Add(_configurationView);
            _status = new HelpBox(failure ?? "Information: Not checked. Model discovery sends no project context and makes no inference request.", failure == null ? HelpBoxMessageType.Info : HelpBoxMessageType.Error);
            _status.style.whiteSpace = WhiteSpace.Normal; (feedbackParent ?? parent).Add(_status);
            _cancel = new Button(() => { _requestCancellation?.Cancel(); _status.messageType = HelpBoxMessageType.Warning; _status.text = "Warning: Cancellation requested. Awaiting cleanup; dispatched writes may already have completed. The independent publisher is not stopped."; }) { text = "Cancel current panel request" };
            _cancel.SetEnabled(false); (feedbackParent ?? parent).Add(_cancel);
            _console = new GitFlowQueueConsole(parent);
            Button(_console.Panel, "Refresh Queue Panel", RefreshQueuePanel);
            var live = new Toggle("Live queue updates (every 15 seconds)"); _console.Panel.Add(live);
            live.RegisterValueChangedCallback(change => { if (change.newValue && !_busy) _ = RunAction("Refresh Queue Panel", RefreshQueuePanel); });
            _liveUpdates = _console.Panel.schedule.Execute(() =>
            {
                if (live.value && !_busy && !_disposed) _ = RunAction("Refresh Queue Panel", RefreshQueuePanel);
            }).Every(15000);
            Button(group, "Find Python and Codex executables", async () =>
            {
                CancellationToken cancellation = _requestCancellation.Token;
                string python = "", codex = "";
                await Task.Run(() =>
                {
                    python = GitFlowToolDiscovery.Resolve(_actionPreferences.PythonExecutable, GitFlowToolDiscovery.PythonCandidates(), cancellation);
                    codex = GitFlowToolDiscovery.Resolve(_actionPreferences.CodexExecutable, GitFlowToolDiscovery.CodexCandidates(), cancellation);
                }, cancellation);
                if (_disposed) return new JObject { ["state"] = "cancelled" };
                if (!string.IsNullOrEmpty(python)) _configuration.PythonExecutable = python;
                if (!string.IsNullOrEmpty(codex)) _configuration.CodexExecutable = codex;
                _tree.UpdateTree(); _configurationView.MarkDirtyRepaint();
                return new JObject { ["state"] = string.IsNullOrEmpty(python) || string.IsNullOrEmpty(codex) ? "partial" : "found",
                    ["checks"] = new JArray(new JObject { ["name"] = "Python", ["state"] = string.IsNullOrEmpty(python) ? "unavailable" : "found", ["source"] = python },
                        new JObject { ["name"] = "Codex", ["state"] = string.IsNullOrEmpty(codex) ? "unavailable" : "found", ["source"] = codex }),
                    ["findings"] = new JArray("Existing valid manual paths are kept. Review or edit the visible paths under Local tools; choose missing executables manually. Then discover models and save preferences. No executable was run or installed; Python version and Codex access are not yet verified.") };
            });
            Button(group, "Discover local Codex models", async () =>
            {
                var result = await Host().DiscoverModelsAsync(_actionPreferences, _requestCancellation.Token);
                _configuration.Models = (JArray)result["models"];
                return result;
            });
            Button(group, "Save project and model preferences", () =>
            {
                var selected = _configuration.Models.FirstOrDefault(item => (string)item["model"] == _configuration.Model);
                if (!string.IsNullOrEmpty(_configuration.Model) && (selected == null || !selected["efforts"].Values<string>().Contains(_configuration.ReasoningEffort)))
                    throw new InvalidOperationException("Discover models and explicitly select a supported model and reasoning effort first.");
                Host().Save(_actionPreferences);
                return Task.FromResult(new JObject { ["state"] = "saved", ["detail"] = "Selection saved. No inference, source change or processor action was authorized." });
            });
            Button(group, "Inspect reusable queue once", () => InspectPage(0));
            Button(group, "Previous observed page", () => InspectPage(PageOffset(-1)));
            Button(group, "Next observed page", () => InspectPage(PageOffset(1)));
            Button(group, "Measure configured publisher storage", () => Host().ExecuteQueueAsync(_actionPreferences, new[] { "storage" }, 150, _requestCancellation.Token));
            var capture = new Foldout { text = "Queue saved local changes", value = true }; group.Add(capture);
            capture.Add(new HelpBox("Queue local changes saves assets and prompts for modified scenes, captures eligible saved files in Capture Scope plus required Unity metadata, and submits a separate exact commit. Your branch, staging and working edits stay in place. Choose a narrow task folder when unrelated edits are present. The queue owns final validation and publication.", HelpBoxMessageType.Info));
            var captureTitle = new TextField("Optional title") { value = "Saved Unity project changes" };
            var captureSummary = new TextField("Optional summary") { value = "Saved first-party changes in the configured task scope." };
            capture.Add(captureTitle); capture.Add(captureSummary);
            Button(capture, "Preview saved changes", () => Host().ExecuteQueueAsync(_actionPreferences, new[] { "preview-local-changes", "--scope", _configuration.CaptureScope }, cancellation: _requestCancellation.Token));
            Button(capture, "Queue local changes", async () =>
            {
                SaveActiveProject(_configuration.Repository, _configuration.CaptureScope);
                return await Host().ExecuteQueueAsync(_actionPreferences, new[] { "queue-local-changes", "--scope", _configuration.CaptureScope,
                    "--title", string.IsNullOrWhiteSpace(captureTitle.value) ? "Saved Unity project changes" : captureTitle.value,
                    "--summary", string.IsNullOrWhiteSpace(captureSummary.value) ? "Saved first-party changes in the configured task scope." : captureSummary.value }, 300, _requestCancellation.Token);
            });
            var publisher = new Foldout { text = "Operator â€” Configure and start publisher", value = false }; group.Add(publisher);
            var workspace = new TextField("New short workspace root"); var unity = new TextField("Installed Unity executable");
            var tests = new TextField("Required project test filter"); var repair = new Toggle("Authorize automatic source repair for up to three hours");
            publisher.Add(workspace); publisher.Add(unity); publisher.Add(tests); publisher.Add(repair);
            publisher.Add(new HelpBox("Configure one new short root per repository. Failed checks stop publication. Automatic repair is disabled until authorized here. It uses the selected local Codex model and account allowance for up to three hours from its original start, stopping after three consecutive attempts without progress. It retains all attempts and cannot alter protected validation/native files or skip checks. No player build is created.", HelpBoxMessageType.Info));
            Button(publisher, "Auto-fill fields and start processor", async () =>
            {
                // Discover existing tools only. Never install tools or select a paid model on the user's behalf.
                string python = await Task.Run(() => GitFlowToolDiscovery.Resolve(_actionPreferences.PythonExecutable,
                    GitFlowToolDiscovery.PythonCandidates(), _requestCancellation.Token), _requestCancellation.Token);
                if (string.IsNullOrEmpty(python)) throw new InvalidOperationException("Choose an installed Python executable under Local tools first.");
                _configuration.PythonExecutable = python; _actionPreferences.PythonExecutable = python;
                JObject detected = await Host().ExecuteQueueAsync(_actionPreferences,
                    new[] { "publisher-settings", "--unity", EditorApplication.applicationPath }, cancellation: _requestCancellation.Token);
                bool existing = (string)detected["state"] == "configured";
                workspace.value = (string)detected["workspace_root"];
                unity.value = (string)detected["unity"];
                tests.value = (string)detected["test_filter"];
                if (existing) repair.value = (bool?)detected["automatic_repair"] ?? false;
                _tree.UpdateTree(); _configurationView.MarkDirtyRepaint();
                if (existing && (string)detected["process_health"] == "running")
                {
                    await RefreshQueuePanel();
                    return new JObject { ["state"] = "already_running", ["detail"] = "Fields filled from the existing policy. The local publisher is already running; no second process was launched." };
                }
                string repairNotice = repair.value ? "\nAutomatic Codex repair uses account allowance for its original three-hour budget. Model: " + (existing ? "retained from existing policy" : _actionPreferences.Model) + "; effort: " + (existing ? "retained from existing policy" : _actionPreferences.ReasoningEffort) : "\nAutomatic repair is disabled.";
                if (!EditorUtility.DisplayDialog("Start GitFlow processor", "Repository: " + _actionPreferences.Repository + "\nRemote: " + _actionPreferences.Remote +
                    "\nWorkspace: " + workspace.value + "\nUnity: " + unity.value + "\nRequired tests: " + tests.value + repairNotice +
                    (existing ? "\n\nKeep the existing validation policy and start its processor. Blocked requests and evidence are preserved." :
                        "\n\nConfigure this new publisher and start it. .* requires every Edit Mode test. Required failures stop publication. The initial queue version record must already be committed on remote main."), "Start processor", "Cancel"))
                    return new JObject { ["state"] = "cancelled", ["detail"] = "Fields filled. No publisher was configured or started." };
                // Existing configuration is never overwritten or silently upgraded. New setup uses the same strict host API.
                string expectedIdentity = (string)detected["configuration_identity"];
                if (!existing)
                {
                    JObject configured = await Host().ConfigureUnityAsync(_actionPreferences, workspace.value, unity.value, tests.value, repair.value, _requestCancellation.Token);
                    expectedIdentity = (string)configured["configuration_identity"];
                }
                if (string.IsNullOrEmpty(expectedIdentity)) throw new InvalidOperationException("Publisher setup returned no configuration identity. Inspect its retained records before starting.");
                Host().Save(_actionPreferences);
                if (existing) await Host().ExecuteQueueAsync(_actionPreferences, new[] { "resume-service", "--operator", "--expected-configuration", expectedIdentity }, cancellation: _requestCancellation.Token);
                Host().StartPublisher(_actionPreferences, expectedIdentity);
                await RefreshQueuePanel();
                return new JObject { ["state"] = "start_requested", ["detail"] = "Processor start requested. The Queue Panel reports the observed local process and all queue items; refresh to observe later changes." };
            });
            Button(publisher, "Create initial project version record", () =>
            {
                string active = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (!string.Equals(Path.GetFullPath(_configuration.Repository), active, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Create the version record only in this active project.");
                string path = Path.Combine(active, "ProjectSettings", "GeurtsGameForgeQueueVersion.txt");
                if (File.Exists(path)) throw new IOException("The existing version record is preserved.");
                if (!System.Text.RegularExpressions.Regex.IsMatch(PlayerSettings.bundleVersion, @"^\d+\.\d+\.\d+$")) throw new InvalidOperationException("Set a valid semantic player version in Unity first.");
                File.WriteAllText(path, PlayerSettings.bundleVersion + "\r\n");
                return Task.FromResult(new JObject { ["state"] = "created", ["detail"] = "Commit this record to the repository's initial main before configuring the queue.", ["path"] = path });
            });
            Button(publisher, "Configure this new publisher", async () =>
            {
                if (!EditorUtility.DisplayDialog("Configure GitFlow publisher", "Reserve " + workspace.value + " for one reusable publisher of " + _configuration.Repository + "?\nRequired test filter: " + tests.value +
                    (repair.value ? "\n\nAutomatic local Codex repair is authorized for up to three hours from its original first attempt, stopping after three consecutive attempts without progress. Restarts and model changes do not reset the clock. Model: " + _configuration.Model + "; effort: " + _configuration.ReasoningEffort + ". Source context is sent using account allowance." : "\nAutomatic repair remains disabled.") +
                    "\n\nNo skip or validation bypass exists. Configuration does not start the publisher.", "Configure", "Cancel")) return new JObject { ["state"] = "cancelled" };
                return await Host().ConfigureUnityAsync(_actionPreferences, workspace.value, unity.value, tests.value, repair.value, _requestCancellation.Token);
            });
            Button(publisher, "Start configured background publisher", () =>
            {
                if (!EditorUtility.DisplayDialog("Start configured publisher", "Start the configured deterministic background host? It runs independently of this window, validates exact candidates, and publishes only after required gates pass. Existing queue ownership and blocked work are preserved.", "Start", "Cancel")) return Task.FromResult(new JObject { ["state"] = "cancelled" });
                Host().StartPublisher(_actionPreferences);
                return Task.FromResult(new JObject { ["state"] = "start_requested", ["detail"] = "Read the recorded processor state to verify startup. A start request is not proof of ownership or readiness." });
            });
            Button(publisher, "Request graceful publisher stop", () => Host().ExecuteQueueAsync(_actionPreferences, new[] { "request-stop", "--operator" }, cancellation: _requestCancellation.Token));
            Button(publisher, "Prepare stopped publisher to resume", () => Host().ExecuteQueueAsync(_actionPreferences, new[] { "resume-service", "--operator" }, cancellation: _requestCancellation.Token));
            Button(publisher, "Adopt this installed runner after stopping", async () =>
            {
                JObject observed = await Host().ExecuteQueueAsync(_actionPreferences, new[] { "inspect" }, cancellation: _requestCancellation.Token);
                string identity = (string)observed["configuration_identity"];
                if (string.IsNullOrEmpty(identity)) throw new InvalidOperationException("There is no existing publisher configuration to upgrade.");
                if (!EditorUtility.DisplayDialog("Adopt installed GitFlow runner", "After the background program has stopped and shared ownership is released, adopt the GitFlow code that is already installed in this project? Existing requests, saved work, repair time and evidence are preserved. This action does not install or update Forge packages or documentation, and does not restart the publisher.", "Adopt installed runner", "Cancel"))
                    return new JObject { ["state"] = "cancelled" };
                return await Host().ExecuteQueueAsync(_actionPreferences, new[] { "upgrade-runner", "--operator", "--expected-configuration", identity }, cancellation: _requestCancellation.Token);
            });
            var policyPath = new TextField("Reviewed existing policy file") { isReadOnly = true }; publisher.Add(policyPath);
            string policyFingerprint = null;
            Button(publisher, "Choose existing required validation policy", () =>
            {
                string selected = EditorUtility.OpenFilePanel("Select repository-owned required validation policy", _actionPreferences.Repository, "json");
                if (!string.IsNullOrEmpty(selected)) { policyPath.value = selected; policyFingerprint = GitFlowHost.PolicyFingerprint(selected); }
                return Task.FromResult(new JObject { ["state"] = string.IsNullOrEmpty(selected) ? "cancelled" : "policy_selected" });
            });
            Button(publisher, "Configure selected repository from reviewed policy", async () =>
            {
                if (policyFingerprint == null || GitFlowHost.PolicyFingerprint(policyPath.value) != policyFingerprint) throw new InvalidOperationException("Choose and review the current policy first; its bytes must remain unchanged.");
                if (!EditorUtility.DisplayDialog("Configure selected Git repository", "Repository: " + _actionPreferences.Repository + "\nRemote: " + _actionPreferences.Remote +
                    "\nNew exclusive workspace: " + workspace.value + "\nRequired policy: " + policyPath.value + "\nReviewed SHA-256: " + policyFingerprint +
                    "\n\nReview the complete policy before continuing. Its required commands run with your local account when this publisher is explicitly started; enabled repair can send project context through Codex and use account allowance. Existing configuration/legacy queues are preserved and refused. Interrupted setup may leave a reserved workspace requiring owner reconciliation. Configuration does not start the publisher.", "Configure reviewed policy", "Cancel"))
                    return new JObject { ["state"] = "cancelled" };
                return await Host().ConfigurePolicyAsync(_actionPreferences, workspace.value, policyPath.value, policyFingerprint, _requestCancellation.Token);
            });
            var submission = new Foldout { text = "Submit committed work", value = false }; group.Add(submission);
            var title = new TextField("Title"); var summary = new TextField("Implementation summary") { multiline = true };
            var size = new DropdownField("Version increment", new List<string> { "patch", "minor", "major" }, 0);
            submission.Add(title); submission.Add(summary); submission.Add(size);
            Button(submission, "Submit exact source once", async () =>
            {
                if (!EditorUtility.DisplayDialog("Submit committed source", "Queue the exact committed source from " + _configuration.Repository + "?\n\nRequired validation runs in the owned publisher before publication. Queued is not delivered.", "Submit", "Cancel"))
                    return new JObject { ["state"] = "cancelled" };
                return await Host().ExecuteQueueAsync(_actionPreferences, new[] { "submit", "--title", title.value, "--summary", summary.value, "--size", size.value }, cancellation: _requestCancellation.Token);
            });
            Button(group, "Diagnose a selected failure context", async () =>
            {
                string path = EditorUtility.OpenFilePanel("Select bounded failure context to share", _configuration.Repository, "txt,json,md");
                if (string.IsNullOrEmpty(path)) return new JObject { ["state"] = "cancelled" };
                if (!EditorUtility.DisplayDialog("Request a local Codex proposal", "Send the complete selected file through your existing Codex sign-in?\n\n" + path +
                    "\nModel: " + _configuration.Model + "\nReasoning: " + _configuration.ReasoningEffort +
                    "\n\nThis uses account allowance. Review the file for private information first. One bounded proposal is returned; this action applies no edits and performs no retry or publication.", "Request proposal", "Cancel"))
                    return new JObject { ["state"] = "cancelled" };
                return await Host().DiagnoseAsync(_actionPreferences, path, _requestCancellation.Token);
            });
            var details = new Foldout { text = "Details - bounded result projection", value = false }; group.Add(details);
            _output = new TextField("Last reported fields (raw/unknown data withheld)") { multiline = true, isReadOnly = true }; details.Add(_output);
            var help = new Foldout { text = "Help â€” local Codex and queue boundaries", value = false }; group.Add(help);
            help.Add(new HelpBox("Select installed Python and Codex, discover models, choose a model and supported reasoning effort, then save. Choices come from local Codex and may be cached; only an actual completed request verifies access. Sign in through Codex itself. Credentials never belong in project settings.\n\nSubmit once from a clean task branch. One configured background publisher owns validation and publication. Required failures block the queue; there is no skip action. Published means main changed; Integrated additionally proves required pulls. Storage measurement is read-only and never deletes workspaces.\n\nThe embedded console observes the selected repository's gameforge queue in explicit pages of 16. It shows exact scope/timestamps, coverage, reported owner/lease, processor, setup and request evidence. Health, expiry, artifact validation and current-main ancestry remain unknown. Previous/Next require the same selected repository and do not merge snapshots. Legacy IntegrationQueue is detected but not adopted; use its existing operator and preserve all records. The lower legacy viewer remains separate and may reject large/unknown legacy schemas. Automatic repair requires separate operator configuration and explicit consent; selecting a model alone never enables it.", HelpBoxMessageType.Info));
            foreach (var child in group.Children()) child.style.marginBottom = 8;
            group.RegisterCallback<GeometryChangedEvent>(_ => group.text = "Queue host and local Codex â€” " + (_busy ? "Working" : failure != null ? "Error: settings retained" : "Explicit actions"));
        }

        /// <summary>Reads only existing God-owned preferences while lifecycle startup is pending; creates no settings or project folders.</summary>
        internal static GitFlowPreferences ReadSavedPreferences(string projectRoot)
        {
            string folder = Path.Combine(projectRoot, "ProjectSettings", "GeurtsGameForge");
            return Directory.Exists(folder) ? new BrickSettingsStore(folder).Load<GitFlowPreferences>(GitFlowBrick.PackageId) : null;
        }

        /// <summary>Returns the enabled lifecycle service and exposes a useful unavailable state otherwise.</summary>
        private static GitFlowHost Host() => GitFlowBrick.Host ?? throw new InvalidOperationException("Enable GitFlow in God before using the host.");

        /// <summary>Saves through supported Unity APIs and refuses to silently omit unsaved or untitled scene work.</summary>
        internal static void SaveActiveProject(string repository, string scope)
        {
            string active = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!string.Equals(Path.GetFullPath(repository), active, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Queue local changes captures this active Unity project. Select its repository first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Finish Play Mode, compilation and asset import before capturing saved changes.");
            string relative = (scope ?? "").Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrWhiteSpace(relative) || relative.Split('/').Contains("..") || !(relative == "Assets" || relative.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new InvalidOperationException("The Unity button requires a task-owned Assets folder as Capture Scope.");
            var excluded = new HashSet<string>(new[] { "plugins", "_thirdparty", "thirdparty", "third-party", "_generated", "generated", "tools", "tools~", "tests", "tests~", "test", "testing" }, StringComparer.OrdinalIgnoreCase);
            Func<string, bool> included = path => (path == relative || path.StartsWith(relative + "/", StringComparison.Ordinal)) && !path.Split('/').Any(excluded.Contains);
            var scenes = new List<Scene>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (string.IsNullOrEmpty(scene.path) && (scene.isDirty || scene.rootCount > 0))
                    throw new InvalidOperationException("Save your untitled scene to a project location first. No unsaved work was captured.");
                if (included(scene.path)) scenes.Add(scene);
            }
            var prefab = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefab != null && prefab.scene.isDirty)
                throw new InvalidOperationException("Save the open prefab in Prefab Mode first. Its unsaved edits are preserved.");
            UnityEngine.Object[] dirtyAssets = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .Where(asset => EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset)).ToArray();
            Scene[] dirtyScenes = scenes.Where(scene => scene.isDirty).ToArray();
            if (dirtyScenes.Length > 0 && dirtyAssets.Any(asset => !included(AssetDatabase.GetAssetPath(asset))))
                throw new InvalidOperationException("An unrelated asset has unsaved edits. Save the selected scene deliberately before queueing so Unity's scene-save flow cannot save unrelated assets.");
            if (dirtyScenes.Length > 0 && !EditorSceneManager.SaveModifiedScenesIfUserWantsTo(dirtyScenes))
                throw new InvalidOperationException("Scene saving was cancelled. No source snapshot was submitted.");
            foreach (Scene scene in scenes)
            {
                if (scene.isDirty)
                    throw new InvalidOperationException("Save every modified or untitled scene before queueing. Unsaved work has been preserved.");
            }
            foreach (UnityEngine.Object asset in dirtyAssets)
                if (included(AssetDatabase.GetAssetPath(asset)))
                    AssetDatabase.SaveAssetIfDirty(asset);
        }

        /// <summary>Returns a page boundary only when the selected target still matches its dated observation.</summary>
        private int PageOffset(int direction)
        {
            JObject observed = _console.LastObservation;
            if (observed == null || (string)observed["repository"] != Path.GetFullPath(_actionPreferences.Repository) || (string)observed["remote"] != _actionPreferences.Remote)
                throw new InvalidOperationException("Inspect the first page for this selected repository before navigating.");
            int offset = (int?)observed["coverage"]?["offset"] ?? 0;
            if (direction < 0) return Math.Max(0, offset - 16);
            return (int?)observed["coverage"]?["next_offset"] ?? throw new InvalidOperationException("No further page was advertised. Refresh explicitly for a new observation.");
        }

        /// <summary>Requests one bounded modern-protocol observation through the actual shared host.</summary>
        private Task<JObject> InspectPage(int offset) => Host().ExecuteQueueAsync(_actionPreferences,
            new[] { "inspect", "--offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture), "--limit", "16" }, 55, _requestCancellation.Token);

        /// <summary>Collects every advertised page, retaining partial coverage instead of reporting an unreadable queue as empty.</summary>
        private async Task<JObject> RefreshQueuePanel()
        {
            JObject combined = null;
            var requests = new JArray(); var findings = new JArray(); var identities = new HashSet<string>(StringComparer.Ordinal);
            int offset = 0, advertised = -1, failed = 0; string queueIdentity = null;
            while (true)
            {
                _requestCancellation.Token.ThrowIfCancellationRequested();
                JObject page = await Host().ExecuteQueueAsync(_actionPreferences, new[] { "inspect", "--offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture), "--limit", "64" }, 55, _requestCancellation.Token);
                if (!(page["coverage"] is JObject coverage)) throw new InvalidOperationException("Queue inspection returned no coverage. The previous dated view is retained.");
                if (combined == null) { combined = (JObject)page.DeepClone(); advertised = (int?)coverage["advertised"] ?? 0; queueIdentity = (string)coverage["queue_identity"]; }
                else if (advertised != (int?)coverage["advertised"] || queueIdentity != (string)coverage["queue_identity"]) { findings.Add("Queue records changed during refresh. Refresh again for complete current coverage."); break; }
                foreach (JToken finding in page["findings"] as JArray ?? new JArray()) findings.Add(finding.DeepClone());
                failed += (int?)coverage["failed"] ?? 0;
                foreach (JObject request in (page["requests"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    if (identities.Add((string)request["id"])) requests.Add(request.DeepClone());
                    else findings.Add("Queue membership changed between pages. Refresh again; this observation is partial.");
                }
                combined["completed_utc"] = page["completed_utc"]?.DeepClone();
                combined["recorded_processor"] = page["recorded_processor"]?.DeepClone();
                int? next = (int?)coverage["next_offset"];
                if (!next.HasValue) break;
                if (next.Value <= offset) throw new InvalidOperationException("Queue inspection did not advance. Previous dated observations are retained.");
                offset = next.Value;
            }
            combined["requests"] = requests; combined["findings"] = findings;
            combined["coverage"] = new JObject { ["offset"] = 0, ["limit"] = requests.Count, ["advertised"] = advertised,
                ["returned"] = requests.Count, ["failed"] = failed, ["omitted"] = Math.Max(0, advertised - requests.Count), ["queue_identity"] = queueIdentity, ["next_offset"] = null };
            combined["observation"] = "All pages requested. Dated observations collected over this refresh; changes between pages remain possible. Refresh again to observe progress.";
            combined["state"] = findings.Count == 0 && failed == 0 && requests.Count == advertised ? "observed" : "partial";
            if (!_disposed) _console.Present(combined);
            return combined;
        }

        /// <summary>Adds one explicit button using the shared serialized action handler.</summary>
        private void Button(VisualElement parent, string title, Func<Task<JObject>> action)
        {
            var button = new Button(async () => await RunAction(title, action)) { text = title };
            _buttons.Add(button); parent.Add(button);
        }

        /// <summary>Shares serialized action handling between explicit buttons and the view-owned opt-in read-only refresh.</summary>
        private async Task RunAction(string title, Func<Task<JObject>> action)
        {
            if (_busy || _disposed) return;
            _busy = true; _actionPreferences = _configuration.Preferences();
            _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
            _configurationView.SetEnabled(false); foreach (Button current in _buttons) current.SetEnabled(false);
            _cancel.SetEnabled(true); _status.messageType = HelpBoxMessageType.Info; _status.text = "Information: Working locally: " + title + ". Waiting for the actual host result; publisher progress is not streamed.";
            _console.Record("Started local action: " + title + "; selected repository: " + _actionPreferences.Repository + "; remote: " + _actionPreferences.Remote);
            try
            {
                JObject result = await action();
                if (!_disposed) { _output.value = GitFlowPresentation.Format(result); _status.text = GitFlowPresentation.Summary(result); _status.messageType = (string)result["state"] == "partial" ? HelpBoxMessageType.Warning : (string)result["state"] == "failed" || (string)result["state"] == "unavailable" ? HelpBoxMessageType.Error : HelpBoxMessageType.Info; _console.Present(result); }
            }
            catch (OperationCanceledException)
            {
                if (!_disposed) { _status.messageType = HelpBoxMessageType.Warning; _status.text = "Warning: Panel request cancelled or timed out. Prior observations remain dated. Dispatched writes may have completed: inspect original request/configuration before retrying. The publisher was not stopped."; _console.Record(_status.text); }
            }
            catch (Exception error)
            {
                // Raw subprocess/diagnostic messages may include credentials or project context.
                if (!_disposed) { _status.messageType = HelpBoxMessageType.Error; _status.text = "Error: Failed: " + error.GetType().Name + ". " + (error is InvalidOperationException || error is ArgumentException ? GitFlowPresentation.Text(error.Message) : "Check selected paths, access and owner-retained evidence. Raw diagnostics withheld; partial writes are possible."); _console.Record(_status.text); }
            }
            finally
            {
                _requestCancellation.Dispose(); _requestCancellation = null; _busy = false;
                if (!_disposed) { _configurationView.SetEnabled(true); foreach (Button current in _buttons) current.SetEnabled(true); _cancel.SetEnabled(false); }
            }
        }

        /// <summary>Releases only this view's Odin resources and cancels its in-flight bridge request.</summary>
        public void Dispose() { _disposed = true; _liveUpdates.Pause(); _cancellation.Cancel(); _requestCancellation?.Cancel(); _tree.Dispose(); _theme?.Dispose(); }
    }
}
