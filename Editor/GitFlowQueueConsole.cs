// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static readonly HashSet<string> OverviewFields = new HashSet<string>(new[] {
            "title", "state", "status", "id", "size", "version", "submitted_utc", "started_utc", "updated_utc", "completed_utc", "finished_utc" }, StringComparer.Ordinal);
        private static readonly HashSet<string> SourceFields = new HashSet<string>(new[] {
            "source", "base", "base_main", "control_commit", "dependencies", "candidate", "main_commit", "verified_main" }, StringComparer.Ordinal);
        private static readonly HashSet<string> ProgressFields = new HashSet<string>(new[] {
            "attempts", "checks", "diagnostic", "receipt", "pulls", "repair", "failure", "failure_phase", "failure_summary", "next_action" }, StringComparer.Ordinal);
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
            _snapshot.Add(new Label("Expand a queue item above for its labelled request, source, dependency and validation fields."));
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

        /// <summary>Creates a readable status/title card; full identifiers remain in selectable fields inside it.</summary>
        private void Item(VisualElement parent, JObject request)
        {
            string state = (string)request["state"] ?? "unknown";
            string prefix = state == "blocked" || state == "validation_failed" ? "\u274c Error - " : "";
            string identity = (string)request["id"] ?? "unknown";
            var section = Section(parent, prefix + DisplayValue("state", request["state"]) + " | " + GitFlowPresentation.Text((string)request["title"]), false, identity);
            section.AddToClassList("forge-card");
            section.style.paddingLeft = section.style.paddingRight = section.style.paddingTop = section.style.paddingBottom = 12;
            // Project before constructing controls: display labels must never bypass the shared redaction policy.
            var safe = (JObject)GitFlowPresentation.ProjectFields(request);
            int remaining = GitFlowPresentation.MaximumCharacters;
            RecordGroup(section, "Overview", safe.Properties().Where(property => OverviewFields.Contains(property.Name)), true, identity + "/overview", ref remaining);
            RecordGroup(section, "Source and dependencies", safe.Properties().Where(property => SourceFields.Contains(property.Name)), false, identity + "/source", ref remaining);
            RecordGroup(section, "Validation and progress", safe.Properties().Where(property => ProgressFields.Contains(property.Name)), false, identity + "/progress", ref remaining);
            RecordGroup(section, "Additional evidence", safe.Properties().Where(property => !OverviewFields.Contains(property.Name) && !SourceFields.Contains(property.Name) && !ProgressFields.Contains(property.Name)), false, identity + "/evidence", ref remaining);
            DisplayLimit(section, remaining);
        }

        /// <summary>Adds bounded operational evidence as labelled, read-only controls rather than a JSON block.</summary>
        private Foldout Add(VisualElement parent, string title, JToken value, bool expanded)
        {
            string scope = "snapshot/" + title;
            var section = Section(parent, title, expanded, scope);
            int remaining = GitFlowPresentation.MaximumCharacters;
            Fields(section, GitFlowPresentation.ProjectFields(value), scope, ref remaining);
            DisplayLimit(section, remaining);
            return section;
        }

        /// <summary>Retains both item and nested section expansion across live queue refreshes.</summary>
        private Foldout Section(VisualElement parent, string title, bool expanded, string scope)
        {
            var section = new Foldout { text = title, value = _expandedItems.TryGetValue(scope, out bool saved) ? saved : expanded };
            section.style.marginTop = 16;
            section.Q<Toggle>().labelElement.style.whiteSpace = WhiteSpace.Normal;
            section.Q<Toggle>().labelElement.style.flexShrink = 1;
            section.RegisterValueChangedCallback(change => { if (change.target == section) _expandedItems[scope] = change.newValue; });
            parent.Add(section); return section;
        }

        /// <summary>Groups related request fields without creating empty or duplicated sections.</summary>
        private void RecordGroup(VisualElement parent, string title, IEnumerable<JProperty> properties, bool expanded, string scope, ref int remaining)
        {
            JProperty[] values = properties.ToArray();
            if (values.Length == 0 || remaining <= 0) return;
            var section = Section(parent, title, expanded, scope);
            foreach (JProperty property in values) Field(section, property.Name, property.Value, scope + "/" + property.Name, ref remaining);
        }

        /// <summary>Renders already-projected objects and collections, preserving explicit empty and omitted values.</summary>
        private void Fields(VisualElement parent, JToken value, string scope, ref int remaining)
        {
            if (remaining <= 0) return;
            if (value is JObject record)
            {
                if (!record.Properties().Any()) parent.Add(new Label("No reported fields."));
                foreach (JProperty property in record.Properties()) Field(parent, property.Name, property.Value, scope + "/" + property.Name, ref remaining);
            }
            else if (value is JArray entries)
            {
                if (entries.Count == 0) parent.Add(new Label("None recorded."));
                for (int index = 0; index < entries.Count && remaining > 0; index++)
                    Field(parent, "entry_" + (index + 1).ToString(CultureInfo.InvariantCulture), entries[index], scope + "/" + index, ref remaining);
            }
            else Field(parent, "value", value, scope + "/value", ref remaining);
        }

        /// <summary>Creates one wrapping field or nested group, within the existing total display-character bound.</summary>
        private void Field(VisualElement parent, string name, JToken value, string scope, ref int remaining)
        {
            if (remaining <= 0) return;
            string label = FieldLabel(name);
            remaining -= label.Length;
            if (value is JObject || value is JArray)
            {
                var section = Section(parent, label, false, scope);
                Fields(section, value, scope, ref remaining);
                return;
            }
            string text = DisplayValue(name, value);
            if (text.Length > Math.Max(0, remaining)) text = text.Substring(0, Math.Max(0, remaining)) + " [Further text omitted]";
            remaining -= text.Length;
            var field = new TextField(label) { name = "queue_field_" + name, value = text, isReadOnly = true, multiline = true };
            field.tooltip = "Reported value (read-only). Select and copy the text.";
            field.style.whiteSpace = WhiteSpace.Normal; field.style.flexDirection = FlexDirection.Column; field.style.marginTop = 8;
            field.labelElement.style.whiteSpace = WhiteSpace.Normal; field.labelElement.style.minWidth = 0;
            parent.Add(field);
        }

        /// <summary>Keeps display omissions visible without implying that a field limit makes the queue empty.</summary>
        private static void DisplayLimit(VisualElement parent, int remaining)
        {
            if (remaining <= 0) parent.Add(new HelpBox("Warning: Further reported details exceed the display limit and are omitted. Original queue evidence is unchanged.", HelpBoxMessageType.Warning));
        }

        /// <summary>Uses familiar labels for common queue fields and readable words for other supported names.</summary>
        private static string FieldLabel(string name)
        {
            switch (name)
            {
                case "id": return "Request ID";
                case "state": case "status": return "Status";
                case "size": return "Requested version increase";
                case "source": return "Source commit";
                case "base": return "Base commit";
                case "control_commit": return "Queue record commit";
                case "schema": return "Record format version";
                case "evidence_scope": return "Evidence limits";
                case "submitted_utc": return "Submitted";
                case "started_utc": return "Started";
                case "updated_utc": return "Last updated";
                case "completed_utc": return "Completed";
                case "finished_utc": return "Finished";
                default: return Sentence(name);
            }
        }

        /// <summary>Formats states, booleans and timestamps while retaining exact identifiers and other reported values.</summary>
        private static string DisplayValue(string name, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null) return "Not reported";
            if (value.Type == JTokenType.Date) return value.ToObject<DateTimeOffset>().ToString("dd MMM yyyy, HH:mm:ss zzz", CultureInfo.InvariantCulture);
            string text = value.Type == JTokenType.String ? GitFlowPresentation.Text((string)value) : value.ToString(Newtonsoft.Json.Formatting.None);
            if (value.Type == JTokenType.Boolean) return (bool)value ? "Yes" : "No";
            if (name == "state" || name == "status" || name == "size") return Sentence(text);
            if (name.EndsWith("_utc", StringComparison.Ordinal) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset timestamp))
                return timestamp.ToString("dd MMM yyyy, HH:mm:ss zzz", CultureInfo.InvariantCulture);
            return string.IsNullOrEmpty(text) ? "Not reported" : text;
        }

        /// <summary>Turns supported field/state identifiers into ordinary words without altering the underlying record.</summary>
        private static string Sentence(string value)
        {
            string text = value.Replace('_', ' ');
            return string.IsNullOrEmpty(text) ? "Not reported" : char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
