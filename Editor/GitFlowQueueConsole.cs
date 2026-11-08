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
        private readonly TextField _history;
        private readonly VisualElement _snapshot;
        private readonly Label _summary;
        internal JObject LastObservation { get; private set; }

        /// <summary>Creates local widgets only; selection, expansion and repaint never inspect a repository.</summary>
        internal GitFlowQueueConsole(VisualElement parent)
        {
            var group = new Foldout { text = "Queue console - recorded observations", value = true };
            group.AddToClassList("forge-card"); group.style.marginTop = 16;
            group.style.paddingLeft = group.style.paddingRight = group.style.paddingTop = group.style.paddingBottom = 12;
            parent.Add(group);
            _summary = new Label("Not checked. Use Inspect reusable queue once. No live publisher output is streamed.");
            _summary.style.whiteSpace = WhiteSpace.Normal; group.Add(_summary);
            group.Add(new HelpBox("Read-only console, not a shell. History contains the last 64 local action events, not publisher logs. Snapshot fields are bounded projections; raw diagnostics, commands and unknown fields are withheld. No validation or publication is rerun. Pages are separate dated observations, not one atomic whole-queue scan.", HelpBoxMessageType.Info));
            _history = new TextField("Local action history (UTC)") { multiline = true, isReadOnly = true };
            _history.style.maxHeight = 220; group.Add(_history);
            group.Add(new Button(() => { _events.Clear(); _history.value = ""; }) { text = "Clear local display history" });
            _snapshot = new VisualElement(); group.Add(_snapshot);
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
            _snapshot.Clear(); _summary.text = GitFlowPresentation.Summary(result);
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

        /// <summary>Adds selectable bounded operational evidence in a labelled collapsible section.</summary>
        private static void Add(VisualElement parent, string title, JToken value, bool expanded)
        {
            var section = new Foldout { text = title, value = expanded }; section.style.marginTop = 16;
            var field = new TextField("Reported fields") { multiline = true, isReadOnly = true, value = GitFlowPresentation.Format(value) };
            field.style.whiteSpace = WhiteSpace.Normal; field.style.flexDirection = FlexDirection.Column;
            field.labelElement.style.whiteSpace = WhiteSpace.Normal; field.labelElement.style.minWidth = 0;
            section.Add(field); parent.Add(section);
        }
    }
}
