// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System.Linq;
using Geurts.GameForge.GitFlow.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Tests
{
    /// <summary>Focused regressions for material raw-data leakage, bounded output and dated snapshot preservation risks.</summary>
    public sealed class GitFlowPresentationTests
    {
        /// <summary>Unknown fields and raw error/diagnostic payloads must not become selectable panel output.</summary>
        [Test]
        public void ProjectionWithholdsRawDiagnosticsAndCredentialLikeValues()
        {
            var value = new JObject { ["id"] = "request_fixture", ["title"] = "token=DO_NOT_DISPLAY", ["command"] = "DO_NOT_DISPLAY",
                ["error"] = "DO_NOT_DISPLAY", ["warnings"] = new JArray("DO_NOT_DISPLAY"), ["errors"] = 2, ["environment"] = "DO_NOT_DISPLAY",
                ["diagnostic"] = new JObject { ["cause_code"] = "required_check_exit", ["cause"] = "Required check failed", ["error"] = "DO_NOT_DISPLAY" } };
            string text = GitFlowPresentation.Format(value);
            Assert.That(text, Does.Not.Contain("DO_NOT_DISPLAY"));
            Assert.That(text, Does.Contain("Raw diagnostics withheld"));
            Assert.That(text, Does.Contain("required_check_exit"));
            Assert.That(text, Does.Contain("\"errors\": 2"));
            Assert.That(value["error"].ToString(), Is.EqualTo("DO_NOT_DISPLAY"));
        }

        /// <summary>Large evidence remains a visibly partial bounded projection and never changes the source record.</summary>
        [Test]
        public void ProjectionBoundsDeepOrLargeEvidenceVisibly()
        {
            var requests = new JArray(Enumerable.Range(0, 150).Select(_ => new JObject { ["title"] = new string('a', 5000) }));
            string text = GitFlowPresentation.Format(new JObject { ["requests"] = requests });
            Assert.That(text.Length, Is.LessThan(GitFlowPresentation.MaximumCharacters + 200));
            Assert.That(text, Does.Contain("omitted").Or.Contain("Display bound"));
            Assert.That(requests.Count, Is.EqualTo(150));
        }

        /// <summary>Local cancellation/results retain the frozen observation and history stays a bounded display rather than publisher telemetry.</summary>
        [Test]
        public void ConsolePreservesDatedScopeAndBoundsRepeatedLocalEvents()
        {
            var root = new VisualElement();
            var console = new GitFlowQueueConsole(root);
            var result = new JObject { ["state"] = "partial", ["repository"] = "C:/fixture", ["remote"] = "origin",
                ["coverage"] = new JObject { ["returned"] = 1, ["advertised"] = 140, ["failed"] = 0, ["omitted"] = 139 },
                ["requests"] = new JArray() };
            console.Present(result); result["repository"] = "C:/changed";
            console.Present(new JObject { ["state"] = "cancelled" });
            Assert.That((string)console.LastObservation["repository"], Is.EqualTo("C:/fixture"));
            for (int index = 0; index < 70; index++) console.Record("local_event_" + index);
            TextField history = root.Query<TextField>().ToList().First(field => field.label == "Local action history (UTC)");
            Assert.That(history.value.Split('\n').Length, Is.EqualTo(64));
            Assert.That(GitFlowPresentation.Summary(console.LastObservation), Does.Contain("139 outside this page"));
        }

        /// <summary>Empty and stopped messages require complete evidence; every item stays visible across the old display limit.</summary>
        [Test]
        public void QueuePanelDistinguishesEmptyPartialStoppedAndAllItemStatuses()
        {
            var root = new VisualElement(); var console = new GitFlowQueueConsole(root);
            var result = new JObject { ["state"] = "observed", ["repository"] = "C:/fixture", ["remote"] = "origin",
                ["coverage"] = new JObject { ["offset"] = 0, ["advertised"] = 0, ["returned"] = 0, ["failed"] = 0, ["omitted"] = 0 },
                ["legacy"] = new JObject { ["state"] = "none_advertised" }, ["findings"] = new JArray(), ["requests"] = new JArray(),
                ["recorded_processor"] = new JObject { ["state"] = "not_started", ["process_health"] = "not_started" } };
            console.Present(result);
            Assert.That(root.Q<Label>("queue_count").text, Is.EqualTo("No active items in Queue."));
            Assert.That(root.Q<Label>("publisher_status").text, Is.EqualTo("Publisher not started on this computer."));
            result["coverage"]["omitted"] = 1;
            console.Present(result);
            Assert.That(root.Q<Label>("queue_count").text, Does.Contain("cannot be confirmed"));
            result["coverage"]["omitted"] = 0; result["coverage"]["advertised"] = 140;
            result["requests"] = new JArray(Enumerable.Range(0, 140).Select(index => new JObject {
                ["id"] = "request_" + index, ["title"] = "Queue item " + index, ["state"] = index == 139 ? "validation_failed" : "queued" }));
            result["recorded_processor"] = new JObject { ["state"] = "idle", ["process_health"] = "not_running" };
            console.Present(result);
            VisualElement items = root.Q("queue_items");
            Assert.That(items.Query<Foldout>().ToList().Count, Is.EqualTo(140));
            Assert.That(items.Query<Foldout>().ToList().Last().text, Does.Contain("validation failed"));
            Assert.That(root.Q<Label>("publisher_status").text, Is.EqualTo("Publisher is not running on this computer."));
            Assert.That(console.Panel.text, Does.Contain("Error: blocked items"));
        }
    }
}
