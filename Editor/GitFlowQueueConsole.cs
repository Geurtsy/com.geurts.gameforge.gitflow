// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Embedded read-only queue console over explicitly returned host evidence, not a terminal or second queue.</summary>
    internal sealed class GitFlowQueueConsole
    {
        private readonly Queue<string> _events = new Queue<string>();
        private readonly Dictionary<string, bool> _expandedItems = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly TextField _history;
        private readonly VisualElement _snapshot;
        private readonly Label _summary;
        private readonly Label _publisher;
        private readonly VisualElement _items;
        internal Foldout Panel { get; }
        internal JObject LastObservation { get; private set; }

        /// <summary>Creates local widgets only; selection, expansion and repaint never inspect a repository.</summary>
        internal GitFlowQueueConsole(VisualElement parent)
        {
            var group = new Foldout { text = "Queue Panel - active queue", value = true }; Panel = group;
            group.AddToClassList("forge-card"); group.style.marginTop = 16;
            group.style.paddingLeft = group.style.paddingRight = group.style.paddingTop = group.style.paddingBottom = 12;
            parent.Add(group);
            _summary = new Label("Queue not checked. Select Refresh Queue Panel to show all items and their status.");
            _summary.style.whiteSpace = WhiteSpace.Normal; group.Add(_summary);
            _publisher = new Label("Publisher: Not checked.") { name = "publisher_status" };
            _publisher.style.whiteSpace = WhiteSpace.Normal; _publisher.style.marginTop = 8; group.Add(_publisher);
            _items = new VisualElement { name = "queue_items" }; group.Add(_items);
            var history = new Foldout { text = "Local action history", value = false }; history.style.marginTop = 16; group.Add(history);
            _history = new TextField("Local action history (UTC)") { multiline = true, isReadOnly = true };
            _history.style.whiteSpace = WhiteSpace.Normal;
            _history.style.flexDirection = FlexDirection.Column;
            _history.style.minWidth = 0;
            _history.labelElement.style.minWidth = 0;
            _history.labelElement.style.whiteSpace = WhiteSpace.Normal;
            _history.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _history.style.maxHeight = 220; history.Add(_history);
            history.Add(new Button(() => { _events.Clear(); _history.value = ""; }) { text = "Clear local display history" });
            var details = new Foldout { text = "Queue evidence and Help", value = false }; details.style.marginTop = 16; group.Add(details);
            details.Add(new HelpBox("Refresh Queue Panel reads every page for the selected repository. Results are dated; refresh again to see progress. Local process checks do not prove shared ownership or validation success. An unavailable, legacy or partially read queue is never reported as empty. No processor starts when this panel opens. Local history is not publisher output.", HelpBoxMessageType.Info));
            _snapshot = new VisualElement(); details.Add(_snapshot);
        }

        /// <summary>Appends one local action event without reading provider state or implying live processor progress.</summary>
        internal void Record(string text)
        {
            _events.Enqueue(DateTime.UtcNow.ToString("O") + " " + GitFlowPresentation.Text(text));
            while (_events.Count > 64) _events.Dequeue();
            _history.value = string.Join("\n", _events);
        }

        /// <summary>Renders a frozen dated result, preserving the last queue snapshot when another action returns.</summary>
        internal void Present(JObject result)
        {
            Record(GitFlowPresentation.Summary(result));
            if (!(result["coverage"] is JObject)) return;
            LastObservation = (JObject)result.DeepClone();
            _snapshot.Clear(); _summary.text = GitFlowPresentation.Text((string)result["repository"]) + " | " + GitFlowPresentation.Text((string)result["remote"]) +
                "\nObserved UTC: " + GitFlowPresentation.Text((string)result["completed_utc"]) + "\n" + GitFlowPresentation.Summary(result);
            PresentItems(result);
            Add(_snapshot, "Observed scope and freshness", new JObject { ["repository"] = result["repository"], ["remote"] = result["remote"],
                ["started_utc"] = result["started_utc"], ["completed_utc"] = result["completed_utc"], ["observation"] = result["observation"], ["coverage"] = result["coverage"] }, true);
            Add(_snapshot, "Setup, shared lease and recorded processor", new JObject { ["setup"] = result["setup"], ["owner"] = result["owner"],
                ["recorded_processor"] = result["recorded_processor"], ["legacy"] = result["legacy"] }, true);
            if (result["findings"] is JArray findings && findings.Count > 0)
            {
                var problems = new Foldout { text = "\u26a0 Warning - partial or unavailable evidence (" + findings.Count + ")", value = true };
                _snapshot.Add(problems);
                foreach (JToken finding in findings) problems.Add(new HelpBox(GitFlowPresentation.Text((string)finding), HelpBoxMessageType.Warning));
            }
            var requests = result["requests"] as JArray ?? new JArray();
            foreach (var state in requests.OfType<JObject>().GroupBy(request => (string)request["state"] ?? "unknown"))
            {
                string severity = state.Key == "blocked" || state.Key == "validation_failed" ? "\u274c Error - " : "";
                var section = new Foldout { text = severity + GitFlowPresentation.Text(state.Key) + " - " + state.Count() + " returned records", value = false };
                _snapshot.Add(section);
                foreach (JObject request in state)
                    Add(section, GitFlowPresentation.Text((string)request["id"]) + " | " + GitFlowPresentation.Text((string)request["title"]), request, false);
            }
            if (requests.Count == 0) _snapshot.Add(new Label("No readable modern requests on this page. Check coverage and legacy findings before treating the queue as empty."));
        }

        /// <summary>Shows every returned item with readable state and keeps empty, stopped and unavailable states distinct.</summary>
        private void PresentItems(JObject result)
        {
            _items.Clear();
            string health = (string)result["recorded_processor"]?["process_health"];
            string phase = (string)result["recorded_processor"]?["state"];
            _publisher.text = health == "not_started" ? "Publisher not started on this computer." : health == "not_running" ? "Publisher is not running on this computer." :
                health == "running" ? "Publisher running - " + GitFlowPresentation.Text(phase) + " (local process observed)." :
                phase == "stopped" ? "Publisher stopped." : "Publisher status unavailable. Refresh or inspect the retained processor evidence.";
            var requests = (result["requests"] as JArray ?? new JArray()).OfType<JObject>().ToArray();
            bool complete = (int?)result["coverage"]?["offset"] == 0 && (int?)result["coverage"]?["failed"] == 0 &&
                (int?)result["coverage"]?["omitted"] == 0 && (int?)result["coverage"]?["advertised"] == requests.Length &&
                !(result["findings"] as JArray ?? new JArray()).Any() &&
                (string)result["legacy"]?["state"] == "none_advertised";
            int active = requests.Count(request => (string)request["state"] != "integrated");
            var count = new Label(active == 0 && complete ? "No active items in Queue." :
                active == 0 ? "Queue unavailable or partially checked. Empty queue cannot be confirmed." :
                active + " active item(s) in Queue" + (complete ? "." : " - partial observation; refresh for remaining items."));
            count.name = "queue_count"; count.style.whiteSpace = WhiteSpace.Normal; count.style.marginTop = 8; _items.Add(count);
            // Keep item status in the visible row, rather than hiding it behind raw JSON or a collapsed details section.
            foreach (JObject request in requests.Where(request => (string)request["state"] != "integrated")) Item(_items, request);
            var completed = requests.Where(request => (string)request["state"] == "integrated").ToArray();
            if (completed.Length > 0)
            {
                var history = new Foldout { text = "Completed items (" + completed.Length + ")", value = false };
                history.style.marginTop = 16; _items.Add(history);
                foreach (JObject request in completed) Item(history, request);
            }
            if (!complete) Panel.text = "Queue Panel - \u26a0 Warning: partial or unavailable queue";
            else if (requests.Any(request => (string)request["state"] == "blocked" || (string)request["state"] == "validation_failed")) Panel.text = "Queue Panel - \u274c Error: blocked items";
            else Panel.text = "Queue Panel - active queue";
        }

        /// <summary>Creates a wrapping status row with selectable details using the existing bounded projection.</summary>
        private void Item(VisualElement parent, JObject request)
        {
            string state = (string)request["state"] ?? "unknown";
            string prefix = state == "blocked" || state == "validation_failed" ? "\u274c Error - " : "";
            string identity = (string)request["id"] ?? "unknown";
            var section = Add(parent, prefix + GitFlowPresentation.Text(state.Replace('_', ' ')) + " | " + GitFlowPresentation.Text((string)request["title"]) +
                " | " + GitFlowPresentation.Text(identity), request, _expandedItems.TryGetValue(identity, out bool expanded) && expanded);
            section.RegisterValueChangedCallback(change => { if (change.target == section) _expandedItems[identity] = change.newValue; });
        }

        /// <summary>Adds selectable bounded operational evidence in a labelled collapsible section.</summary>
        private static Foldout Add(VisualElement parent, string title, JToken value, bool expanded)
        {
            var section = new Foldout { text = title, value = expanded }; section.style.marginTop = 16;
            section.Q<Toggle>().labelElement.style.whiteSpace = WhiteSpace.Normal;
            section.Q<Toggle>().labelElement.style.flexShrink = 1;
            var field = new TextField("Reported fields") { multiline = true, isReadOnly = true, value = GitFlowPresentation.Format(value) };
            field.style.whiteSpace = WhiteSpace.Normal; field.style.flexDirection = FlexDirection.Column;
            field.labelElement.style.whiteSpace = WhiteSpace.Normal; field.labelElement.style.minWidth = 0;
            section.Add(field); parent.Add(section); return section;
        }
    }
}
