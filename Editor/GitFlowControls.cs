// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Geurts.GameForge.God.Editor;
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
            [Tooltip("Existing project checkout. Source must be clean and committed before submission.")]
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
        private readonly Label _status;
        private readonly TextField _output;
        private bool _busy, _disposed;

        /// <summary>Creates controls and loads local settings only; no discovery, inference or queue action runs on opening.</summary>
        internal GitFlowControls(VisualElement parent)
        {
            var group = new Foldout { text = "Queue host and local Codex â€” Not checked", value = false };
            group.AddToClassList("forge-card"); group.style.marginTop = 16; group.style.paddingLeft = group.style.paddingRight = 12; parent.Add(group);
            _configuration = new Configuration { Repository = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) };
            string failure = null;
            try
            {
                var saved = GitFlowBrick.Host?.Load();
                if (saved != null)
                {
                    _configuration.PythonExecutable = saved.PythonExecutable; _configuration.CodexExecutable = saved.CodexExecutable;
                    _configuration.Model = saved.Model; _configuration.ReasoningEffort = saved.ReasoningEffort;
                    if (!string.IsNullOrEmpty(saved.Repository)) _configuration.Repository = saved.Repository;
                    _configuration.Remote = saved.Remote;
                    _configuration.CaptureScope = saved.CaptureScope ?? "Assets";
                }
            }
            catch (Exception error) { failure = error.Message; }
            _tree = PropertyTree.Create(_configuration);
            group.Add(new IMGUIContainer(() => { if (_theme == null) _theme = new ForgeEditorTheme(); using (_theme.Scope()) _tree.Draw(false); }));
            _status = new Label(failure ?? "Not checked. Model discovery sends no project context and makes no inference request.");
            _status.style.whiteSpace = WhiteSpace.Normal; group.Add(_status);
            Button(group, "Discover local Codex models", async () =>
            {
                var result = await Host().DiscoverModelsAsync(_configuration.Preferences(), _cancellation.Token);
                _configuration.Models = (JArray)result["models"];
                return result;
            });
            Button(group, "Save project and model preferences", () =>
            {
                var selected = _configuration.Models.FirstOrDefault(item => (string)item["model"] == _configuration.Model);
                if (!string.IsNullOrEmpty(_configuration.Model) && (selected == null || !selected["efforts"].Values<string>().Contains(_configuration.ReasoningEffort)))
                    throw new InvalidOperationException("Discover models and explicitly select a supported model and reasoning effort first.");
                Host().Save(_configuration.Preferences());
                return Task.FromResult(new JObject { ["state"] = "saved", ["detail"] = "Selection saved. No inference, source change or processor action was authorized." });
            });
            Button(group, "Inspect reusable queue once", () => Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "inspect" }, cancellation: _cancellation.Token));
            Button(group, "Measure configured publisher storage", () => Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "storage" }, 150, _cancellation.Token));
            var capture = new Foldout { text = "Queue saved local changes", value = true }; group.Add(capture);
            capture.Add(new HelpBox("Queue local changes saves assets and prompts for modified scenes, captures eligible saved files in Capture Scope plus required Unity metadata, and submits a separate exact commit. Your branch, staging and working edits stay in place. Choose a narrow task folder when unrelated edits are present. The queue owns final validation and publication.", HelpBoxMessageType.Info));
            var captureTitle = new TextField("Optional title") { value = "Saved Unity project changes" };
            var captureSummary = new TextField("Optional summary") { value = "Saved first-party changes in the configured task scope." };
            capture.Add(captureTitle); capture.Add(captureSummary);
            Button(capture, "Preview saved changes", () => Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "preview-local-changes", "--scope", _configuration.CaptureScope }, cancellation: _cancellation.Token));
            Button(capture, "Queue local changes", async () =>
            {
                SaveActiveProject(_configuration.Repository);
                return await Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "queue-local-changes", "--scope", _configuration.CaptureScope,
                    "--title", string.IsNullOrWhiteSpace(captureTitle.value) ? "Saved Unity project changes" : captureTitle.value,
                    "--summary", string.IsNullOrWhiteSpace(captureSummary.value) ? "Saved first-party changes in the configured task scope." : captureSummary.value }, 300, _cancellation.Token);
            });
            var publisher = new Foldout { text = "Operator â€” Configure and start publisher", value = false }; group.Add(publisher);
            var workspace = new TextField("New short workspace root"); var unity = new TextField("Installed Unity executable");
            var tests = new TextField("Required project test filter"); var repair = new Toggle("Authorize automatic source repair for up to three hours");
            publisher.Add(workspace); publisher.Add(unity); publisher.Add(tests); publisher.Add(repair);
            publisher.Add(new HelpBox("Configure one new short root per repository. Failed checks stop the queue. Automatic repair is optional, uses the selected local Codex model and account allowance, preserves all attempts, and stops on no meaningful progress. It cannot alter protected validation/native files or skip checks. No player build is created.", HelpBoxMessageType.Info));
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
                    (repair.value ? "\n\nAutomatic local Codex repair is authorized for up to three hours from its original first attempt, stopping earlier on no meaningful source progress. Model: " + _configuration.Model + "; effort: " + _configuration.ReasoningEffort + ". Source context is sent using account allowance." : "\nAutomatic repair remains disabled.") +
                    "\n\nNo skip or validation bypass exists. Configuration does not start the publisher.", "Configure", "Cancel")) return new JObject { ["state"] = "cancelled" };
                return await Host().ConfigureUnityAsync(_configuration.Preferences(), workspace.value, unity.value, tests.value, repair.value, _cancellation.Token);
            });
            Button(publisher, "Start configured background publisher", () =>
            {
                if (!EditorUtility.DisplayDialog("Start configured publisher", "Start the configured deterministic background host? It runs independently of this window, validates exact candidates, and publishes only after required gates pass. Existing queue ownership and blocked work are preserved.", "Start", "Cancel")) return Task.FromResult(new JObject { ["state"] = "cancelled" });
                Host().StartPublisher(_configuration.Preferences());
                return Task.FromResult(new JObject { ["state"] = "start_requested", ["detail"] = "Read the recorded processor state to verify startup. A start request is not proof of ownership or readiness." });
            });
            Button(publisher, "Request graceful publisher stop", () => Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "request-stop", "--operator" }, cancellation: _cancellation.Token));
            Button(publisher, "Prepare stopped publisher to resume", () => Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "resume-service", "--operator" }, cancellation: _cancellation.Token));
            var submission = new Foldout { text = "Submit committed work", value = false }; group.Add(submission);
            var title = new TextField("Title"); var summary = new TextField("Implementation summary") { multiline = true };
            var size = new DropdownField("Version increment", new List<string> { "patch", "minor", "major" }, 0);
            submission.Add(title); submission.Add(summary); submission.Add(size);
            Button(submission, "Submit exact source once", async () =>
            {
                if (!EditorUtility.DisplayDialog("Submit committed source", "Queue the exact committed source from " + _configuration.Repository + "?\n\nRequired validation runs in the owned publisher before publication. Queued is not delivered.", "Submit", "Cancel"))
                    return new JObject { ["state"] = "cancelled" };
                return await Host().ExecuteQueueAsync(_configuration.Preferences(), new[] { "submit", "--title", title.value, "--summary", summary.value, "--size", size.value }, cancellation: _cancellation.Token);
            });
            Button(group, "Diagnose a selected failure context", async () =>
            {
                string path = EditorUtility.OpenFilePanel("Select bounded failure context to share", _configuration.Repository, "txt,json,md");
                if (string.IsNullOrEmpty(path)) return new JObject { ["state"] = "cancelled" };
                if (!EditorUtility.DisplayDialog("Request a local Codex proposal", "Send the complete selected file through your existing Codex sign-in?\n\n" + path +
                    "\nModel: " + _configuration.Model + "\nReasoning: " + _configuration.ReasoningEffort +
                    "\n\nThis uses account allowance. Review the file for private information first. One bounded proposal is returned; this action applies no edits and performs no retry or publication.", "Request proposal", "Cancel"))
                    return new JObject { ["state"] = "cancelled" };
                return await Host().DiagnoseAsync(_configuration.Preferences(), path, _cancellation.Token);
            });
            var details = new Foldout { text = "Details - exact result and evidence", value = false }; group.Add(details);
            _output = new TextField("Last exact result") { multiline = true, isReadOnly = true }; details.Add(_output);
            var help = new Foldout { text = "Help â€” local Codex and queue boundaries", value = false }; group.Add(help);
            help.Add(new HelpBox("Select installed Python and Codex, discover models, choose a model and supported reasoning effort, then save. Choices come from local Codex and may be cached; only an actual completed request verifies access. Sign in through Codex itself. Credentials never belong in project settings.\n\nSubmit once from a clean task branch. One configured background publisher owns validation and publication. Required failures block the queue; there is no skip action. Published means main changed; Integrated additionally proves required pulls. Storage measurement is read-only and never deletes workspaces.\n\nThe legacy read-only viewer below remains separate from this protocol. Automatic repair requires separate operator configuration and explicit consent; selecting a model alone never enables it.", HelpBoxMessageType.Info));
            foreach (var child in group.Children()) child.style.marginBottom = 8;
            group.RegisterCallback<GeometryChangedEvent>(_ => group.text = "Queue host and local Codex â€” " + (_busy ? "Working" : failure != null ? "Error: settings retained" : "Explicit actions"));
        }

        /// <summary>Returns the enabled lifecycle service and exposes a useful unavailable state otherwise.</summary>
        private static GitFlowHost Host() => GitFlowBrick.Host ?? throw new InvalidOperationException("Enable GitFlow in God before using the host.");

        /// <summary>Saves through supported Unity APIs and refuses to silently omit unsaved or untitled scene work.</summary>
        private static void SaveActiveProject(string repository)
        {
            string active = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!string.Equals(Path.GetFullPath(repository), active, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Queue local changes captures this active Unity project. Select its repository first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Finish Play Mode, compilation and asset import before capturing saved changes.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                throw new InvalidOperationException("Scene saving was cancelled. No source snapshot was submitted.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.isDirty || (string.IsNullOrEmpty(scene.path) && scene.rootCount > 0))
                    throw new InvalidOperationException("Save every modified or untitled scene before queueing. Unsaved work has been preserved.");
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>Adds an awaited explicit action, preserving visible errors and cancellation without async exception leakage.</summary>
        private void Button(VisualElement parent, string title, Func<Task<JObject>> action)
        {
            parent.Add(new Button(async () =>
            {
                if (_busy || _disposed) return;
                _busy = true; _status.text = "Working: " + title;
                try
                {
                    JObject result = await action();
                    if (!_disposed) { _output.value = result.ToString(); _status.text = Describe(result); }
                }
                catch (Exception error) { if (!_disposed) _status.text = "Error â€” " + error.Message; }
                finally { _busy = false; }
            }) { text = title });
        }

        /// <summary>Explains the actual outcome before exposing detailed identifiers and evidence.</summary>
        private static string Describe(JObject result)
        {
            string state = (string)result["state"];
            if (state == "queued") return "Queued: your saved checkpoint is on the waiting list. It has not joined the shared accepted project yet. Use Inspect reusable queue once for an update.";
            if (state == "published") return "Published: the checked changes reached the shared accepted project. Required local synchronization is still pending.";
            if (state == "integrated") return "Integrated: the checked changes reached the shared project and required synchronization completed.";
            return (string)result["owner_summary"] ?? ("Finished: " + (string)(result["state"] ?? result["account_state"]) + ". Open Details for the exact result.");
        }

        /// <summary>Releases only this view's Odin resources and cancels its in-flight bridge request.</summary>
        public void Dispose() { _disposed = true; _cancellation.Cancel(); _tree.Dispose(); _theme?.Dispose(); }
    }
}
