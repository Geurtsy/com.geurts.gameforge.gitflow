// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Projects bounded operational fields for the panel without exposing executable or raw diagnostic payloads.</summary>
    internal static class GitFlowPresentation
    {
        internal const int MaximumCharacters = 49152;
        private static readonly HashSet<string> Fields = new HashSet<string>((
            "schema id state status title source base size version candidate main_commit verified_main evidence_candidate submitted_utc started_utc finished_utc completed_utc updated_utc " +
            "policy base_main dependencies attempts receipt checks name kind folder run warnings errors skipped reused pulls publisher original repair " +
            "owner host request stage process_id pid lease_protocol lease_seconds control_commit process_health recorded_processor setup " +
            "repository remote coverage offset limit advertised returned failed omitted next_offset ordering legacy requests locks migration " +
            "runner_current workspace_root policy_hash automatic_repair timeout_seconds configuration_identity evidence_scope observation findings reason failure_summary failure_phase next_action " +
            "account_state models model display_name efforts window_started_utc consecutive_stalled attempt_count " +
            "diagnostic cause_code cause exception_type root_exception_type evidence observed_utc owner_failure_file check_name exit_code line column").Split(' '), StringComparer.Ordinal);

        /// <summary>Redacts credential-like text and remote URLs, removes control characters and visibly bounds a display value.</summary>
        internal static string Text(string value)
        {
            value = value ?? "Unknown / not supplied";
            value = Regex.Replace(value, @"(?i)(?:https?|ssh)://[^\s]+", "[remote URL omitted]");
            value = Regex.Replace(value, @"(?i)(?:bearer\s+\S+|(?:token|password|secret|api[_-]?key)\s*[:=]\s*\S+|gh[pousr]_[A-Za-z0-9_]+|sk-[A-Za-z0-9_-]+)", "[credential-like text omitted]");
            value = new string(value.Where(character => character == '\n' || character == '\t' || !char.IsControl(character)).ToArray());
            return value.Length > 1024 ? value.Substring(0, 1024) + " [text omitted]" : value;
        }

        /// <summary>Builds a selectable display projection; original provider data remains unchanged and is never copied wholesale.</summary>
        internal static string Format(JToken value)
        {
            string text = Project(value, 0).ToString(Formatting.Indented);
            return text.Length > MaximumCharacters ? text.Substring(0, MaximumCharacters) + "\n[Display bound reached. Narrow the page; this is partial evidence.]" : text;
        }

        /// <summary>Copies only known operational fields, with bounded depth, lists and strings.</summary>
        private static JToken Project(JToken value, int depth)
        {
            if (depth > 8) return new JValue("[Deeper evidence omitted]");
            if (value is JObject record)
            {
                var safe = new JObject();
                foreach (JProperty property in record.Properties())
                    if (Fields.Contains(property.Name) && (!(property.Name == "errors" || property.Name == "warnings" || property.Name == "skipped" || property.Name == "pid" || property.Name == "process_id" || property.Name == "exit_code" || property.Name == "line" || property.Name == "column") || property.Value.Type == JTokenType.Integer) && (!(property.Name == "verified_main" || property.Name == "evidence_candidate") || property.Value.Type == JTokenType.String && Regex.IsMatch((string)property.Value, "^[0-9a-f]{40}$")))
                        safe[property.Name] = Project(property.Value, depth + 1);
                if (record["failure"] != null || record["error"] != null || record["blocker"] != null)
                    safe["failure"] = "Recorded failure. Raw diagnostics withheld; consult the owning retained evidence.";
                return safe;
            }
            if (value is JArray array)
            {
                var safe = new JArray(array.Take(64).Select(item => Project(item, depth + 1)));
                if (array.Count > 64) safe.Add("[Further entries omitted]");
                return safe;
            }
            return value?.Type == JTokenType.String ? new JValue(Text((string)value)) : value?.DeepClone() ?? JValue.CreateNull();
        }

        /// <summary>Describes coverage without claiming a single observation establishes live health, full history or delivery.</summary>
        internal static string Summary(JObject result)
        {
            if (result["coverage"] is JObject coverage)
                return "Snapshot " + Text((string)result["state"]) + ": " + coverage["returned"] + " returned / " + coverage["advertised"] +
                    " advertised, " + coverage["failed"] + " failed, " + coverage["omitted"] + " outside this page. Recorded health only; refresh explicitly.";
            string state = (string)result["state"] ?? (string)result["account_state"] ?? "reported result";
            if (state == "queued") return "Queued - awaiting integration. This is not delivery.";
            if (state == "published") return "Published (reported). Required synchronization remains pending.";
            if (state == "integrated") return "Integrated (reported). Read its recorded validation, receipt and synchronization scope.";
            return "Result: " + Text(state) + ". Unknown or omitted evidence is not a pass.";
        }
    }
}
