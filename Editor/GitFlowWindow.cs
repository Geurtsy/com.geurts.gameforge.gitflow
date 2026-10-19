// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Geurts.GameForge.God.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>No-code inspection, submit-once and explicit publisher workflows, with no automatic refresh or process startup.</summary>
    public sealed partial class GitFlowWindow : EditorWindow
    {
        private TextField _repository, _remote;
        private Button _refresh;
        private Label _status;
        private VisualElement _results;
        private bool _closed, _refreshing;
        private GitFlowSnapshot _displayed;
        public GitFlowSnapshot DisplayedSnapshot => _displayed;

        /// <summary>Opens the standalone viewer using God's geometry and installed-brick embedding contract.</summary>
        [MenuItem("Tools/Geurts Game Forge/GitFlow"), BrickEditorMenu(GitFlowBrick.PackageId, PanelType = typeof(GitFlowWindow))]
        public static void Open() => ForgeEditorTheme.OpenWindow<GitFlowWindow>(new Vector2(360, 280), "GitFlow");

        /// <summary>Builds local controls only; opening/restoring an embedded or standalone view performs no inspection.</summary>
        public void CreateGUI()
        {
            _closed = false;
            VisualElement root = rootVisualElement; root.Clear(); ForgeEditorTheme.ApplyToolkit(root);
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 12;
            var title = new Label("Geurts Game Forge GitFlow"); title.style.fontSize = 20; root.Add(title);
            root.Add(new Label("Inspect deliberately, submit once, and configure an independent publisher. Snapshots are never live telemetry."));
            var tabs = new TabView(); tabs.style.flexGrow = 1; root.Add(tabs);
            var inspectTab = new Tab("Inspect"); tabs.Add(inspectTab);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; inspectTab.Add(scroll);
            var scope = Section(scroll, "Repository and refresh", true);
            _repository = new TextField("Repository") { value = Path.GetFullPath(Path.Combine(Application.dataPath, "..")) };
            _remote = new TextField("Existing remote") { value = "origin" };
            _repository.isDelayed = true; _remote.isDelayed = true;
            scope.Add(_repository); scope.Add(_remote);
            var select = new Button(() => { string chosen = EditorUtility.OpenFolderPanel("Select a Git checkout to inspect", _repository.value, ""); if (!string.IsNullOrEmpty(chosen)) _repository.value = chosen; }) { text = "Choose repository…" };
            scope.Add(select);
            _refresh = new Button(RefreshClicked) { text = "Refresh queue once" }; scope.Add(_refresh);
            _status = new Label(GitFlowBrick.Service == null ? "Unavailable: enable GitFlow in God, then select Refresh." : "Not checked. Select Refresh to inspect once.");
            _status.style.whiteSpace = WhiteSpace.Normal; root.Add(_status);
            _results = new VisualElement(); scroll.Add(_results);
            var help = Section(scroll, "Help", false);
            help.Add(new HelpBox("Select a local Git checkout and its existing remote, then Refresh queue once. Opening this window and expanding sections stay offline. Refresh is limited to 45 seconds and 128 requests, with a two-second cooldown. Values remain a dated snapshot until you refresh again.\n\nInspection reads legacy codex/queue and native geurts/gitflow protocol-1 records. Public HTTPS and absolute local remotes are supported. It never submits, starts processors, takes locks or changes configuration. Submit and Publisher tabs contain separate explicit actions; consumer installation remains manual.\n\nA recorded PID does not prove ownership. Lease timestamps never prove expiry. Native receipt identities, tree and retained stream hashes are checked; required gate semantics and current-main ancestry are not rerun. For unavailable/malformed evidence, preserve it and consult the named queue operator.\n\nCode extensions use GitFlowBrick.Service.RefreshAsync(new GitFlowTarget(repository, remote, publisherRoot)); the exact same snapshot feeds this view. See local Documentation~/AIUsability.md for lifecycle and adapter limits.", HelpBoxMessageType.Info));
            StyleRows(scope); StyleRows(help);
            BuildPublishingTabs(tabs);
        }

        /// <summary>Starts one explicit request while retaining the previous dated snapshot during failure.</summary>
        private async void RefreshClicked()
        {
            if (_refreshing) return;
            GitFlowInspection service = GitFlowBrick.Service;
            if (service == null) { _status.text = "Unavailable: enable GitFlow in God before refreshing."; return; }
            _refreshing = true; _refresh.SetEnabled(false); _status.text = "Inspecting once (45-second maximum). Previous results remain a dated snapshot.";
            try
            {
                GitFlowSnapshot snapshot = await service.RefreshAsync(new GitFlowTarget(_repository.value, _remote.value, _publisherRoot?.value ?? ""));
                if (!_closed) Present(snapshot);
            }
            catch (Exception error) { if (!_closed) _status.text = "Refresh failed: " + error.GetType().Name + ": " + error.Message + " Previous results are unchanged."; }
            finally { _refreshing = false; if (!_closed) { await Task.Delay(2000); if (!_closed) _refresh.SetEnabled(true); } }
        }

        /// <summary>Renders the exact immutable API result without network or process work.</summary>
        public void Present(GitFlowSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            _displayed = snapshot; _results.Clear();
            _status.text = "Snapshot completed " + snapshot.RefreshedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") +
                " (UTC " + snapshot.RefreshedUtc.ToString("O") + "). Queue: " + snapshot.QueueState + ". Refresh again to observe changes.";
            var health = Section(_results, "Processor: " + snapshot.ProcessorState + " | Lease: " + snapshot.LeaseState, true);
            Value(health, "Repository", snapshot.Repository); Value(health, "Remote", snapshot.Remote);
            Value(health, "Processor observation", snapshot.ProcessorDetail); Value(health, "Lease observation", snapshot.LeaseDetail);
            if (snapshot.Issues.Count > 0)
            {
                var failures = Section(_results, "⚠ Warning — inspection findings (" + snapshot.Issues.Count + ")", true);
                foreach (string issue in snapshot.Issues) failures.Add(new HelpBox(issue, HelpBoxMessageType.Warning));
                StyleRows(failures);
            }
            foreach (string state in new[] { "Queued", "Processing", "Validating", "Delivered", "Failed", "Unknown" })
            {
                var requests = snapshot.Requests.Where(value => value.State == state).ToArray();
                var group = Section(_results, (state == "Failed" ? "✖ Error — " : "") + state + " (" + requests.Length + ")", state == "Failed");
                if (requests.Length == 0) group.Add(new Label("No " + state.ToLowerInvariant() + " records observed in this snapshot."));
                foreach (var request in requests)
                {
                    var card = Section(group, request.Id + " — " + request.Title, false);
                    Value(card, "Submitted source", request.Source); Value(card, "Change size", request.Size);
                    Value(card, "Assigned version", request.Version); Value(card, "Published commit (recorded)", request.PublishedCommit);
                    Value(card, "Control commit", request.ControlCommit);
                    if (!string.IsNullOrWhiteSpace(request.Failure)) card.Add(new HelpBox("Error — " + request.Failure + "\nAsk the owning operator to address this failure; GitFlow cannot retry it.", HelpBoxMessageType.Error));
                    Value(card, "Receipt verification", request.ReceiptDetail);
                    Value(Section(card, "Request and validation evidence (reported)", false), "Original control JSON", request.EvidenceJson);
                    if (!string.IsNullOrEmpty(request.ReceiptJson)) Value(Section(card, "Publication receipt", false), "Original receipt JSON", request.ReceiptJson);
                    StyleRows(card);
                }
                StyleRows(group);
            }
            StyleRows(health);
        }

        /// <summary>Creates a labelled collapsible group using God's scoped stylesheet and shared spacing baseline.</summary>
        private static Foldout Section(VisualElement parent, string title, bool expanded)
        {
            var section = new Foldout { text = title, value = expanded };
            section.AddToClassList("forge-card");
            section.style.marginTop = 16;
            section.style.paddingLeft = section.style.paddingRight = section.style.paddingTop = section.style.paddingBottom = 12;
            parent.Add(section); return section;
        }

        /// <summary>Shows a complete selectable, wrapping value with absent evidence clearly marked.</summary>
        private static void Value(VisualElement parent, string title, string value)
        {
            var field = new TextField(title) { value = string.IsNullOrEmpty(value) ? "Unknown / not supplied" : value, multiline = true, isReadOnly = true };
            field.style.whiteSpace = WhiteSpace.Normal; field.style.marginTop = 8; parent.Add(field);
        }

        /// <summary>Applies readable row gaps and vertical labels at every width, including narrow God panels.</summary>
        private static void StyleRows(VisualElement parent)
        {
            foreach (var child in parent.Children()) child.style.marginBottom = 8;
            foreach (var label in parent.Query<Label>().ToList()) label.style.whiteSpace = WhiteSpace.Normal;
            foreach (var field in parent.Query<TextField>().ToList()) { field.style.flexDirection = FlexDirection.Column; field.labelElement.style.minWidth = 0; field.labelElement.style.whiteSpace = WhiteSpace.Normal; }
        }

        /// <summary>Detaches this view without cancelling another view's lifecycle-owned service or touching a standalone sibling.</summary>
        private void OnDisable() { _closed = true; }
    }
}
