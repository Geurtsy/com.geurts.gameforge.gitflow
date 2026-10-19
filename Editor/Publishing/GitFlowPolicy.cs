// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Generic adapter for one committed plain SemVer file and strictly required, console-only validation commands.</summary>
    public sealed class GitFlowPolicy
    {
        public string VersionFile { get; }
        public string Fingerprint { get; }
        internal JObject Data { get; }

        /// <summary>Validates a complete committed policy; unknown schema or undeclared gates fail closed.</summary>
        public GitFlowPolicy(string json)
        {
            Data = GitFlowGit.Json(json);
            if (Data.Properties().Any(property => !new[] { "schema", "adapter", "publication", "headless_silent_commands", "version_file", "gates", "protected_paths" }.Contains(property.Name)))
                throw new InvalidDataException("Unknown project policy fields require an owning adapter upgrade; no requirement is silently ignored.");
            if (Data["schema"]?.Type != JTokenType.Integer || (int?)Data["schema"] != 1 || (string)Data["adapter"] != "generic_git" || (string)Data["publication"] != "atomic_main" || Data["headless_silent_commands"]?.Type != JTokenType.Boolean || (bool?)Data["headless_silent_commands"] != true)
                throw new InvalidDataException("Require schema 1, generic_git, atomic_main and an explicit headless_silent_commands declaration. PR/release-required projects need their own adapter.");
            VersionFile = Relative((string)Data["version_file"]);
            if (!(Data["gates"] is JArray gates) || gates.Count == 0 || gates.Count > 8 || !(Data["protected_paths"] is JArray paths) || paths.Count == 0)
                throw new InvalidDataException("Declare one to eight required gates and all protected controller/gate paths.");
            foreach (JToken gate in gates)
            {
                if (gate is JObject command && command.Properties().Any(property => !new[] { "id", "executable", "executable_sha256", "tool_files", "arguments", "timeout_seconds" }.Contains(property.Name))) throw new InvalidDataException("Unknown validation command policy fields.");
                if (!(gate is JObject) || !Regex.IsMatch((string)gate["id"] ?? "", "^[a-z0-9]+(?:_[a-z0-9]+)*$") ||
                    !Path.IsPathRooted((string)gate["executable"] ?? "") || !Regex.IsMatch((string)gate["executable_sha256"] ?? "", "^[0-9a-f]{64}$") ||
                    !(gate["arguments"] is JArray arguments) || arguments.Any(arg => arg.Type != JTokenType.String || ((string)arg).IndexOf('\0') >= 0) ||
                    (int?)gate["timeout_seconds"] < 1 || (int?)gate["timeout_seconds"] > 3600 || gate["timeout_seconds"]?.Type != JTokenType.Integer)
                    throw new InvalidDataException("Each required gate needs a unique ID, absolute native console executable/SHA256, literal argument array and a 1–3600 second budget.");
                GitFlowGit.CheckPath((string)gate["executable"]);
                if (!(gate["tool_files"] is JArray dependencies) || dependencies.Count > 64) throw new InvalidDataException("Declare the command's pinned external tool dependency files.");
                foreach (JToken dependency in dependencies)
                {
                    if (!(dependency is JObject file) || file.Properties().Count() != 2 || !Path.IsPathRooted((string)file["path"] ?? "") || !Regex.IsMatch((string)file["sha256"] ?? "", "^[0-9a-f]{64}$")) throw new InvalidDataException("Invalid tool dependency identity.");
                    GitFlowGit.CheckPath((string)file["path"]);
                }
            }
            if (gates.Select(gate => (string)gate["id"]).Distinct(StringComparer.Ordinal).Count() != gates.Count) throw new InvalidDataException("Duplicate validation gate ID.");
            foreach (JToken path in paths) Relative((string)path);
            Fingerprint = GitFlowGit.Hash(System.Text.Encoding.UTF8.GetBytes(Data.ToString(Newtonsoft.Json.Formatting.None)));
        }

        /// <summary>Checks whether a worker path belongs to immutable controller, policy or queue-owned version metadata.</summary>
        internal bool Protected(string path) => path.Equals(GitFlowGit.PolicyFile, StringComparison.OrdinalIgnoreCase) || path.Equals(VersionFile, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(".geurts-gitflow/", StringComparison.OrdinalIgnoreCase) || ((JArray)Data["protected_paths"]).Any(token => path.Equals((string)token, StringComparison.OrdinalIgnoreCase) || path.StartsWith(((string)token).TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));

        /// <summary>Validates an unambiguous repository-relative path without metacharacters or traversal.</summary>
        internal static string Relative(string path)
        { if (string.IsNullOrEmpty(path) || path.Length > 120 || !Regex.IsMatch(path, "^[a-zA-Z0-9_. /-]+$") || path.StartsWith("/") || path.Split('/').Any(part => part == ".." || part == "." || part.Length == 0) || path.EndsWith(" ") || path.Contains("\\")) throw new InvalidDataException("Declare an unambiguous relative path: " + path); return path; }

        /// <summary>Assigns a higher plain three-part SemVer while preserving the declared increment.</summary>
        internal static string NextVersion(string value, string increment)
        {
            if (!Regex.IsMatch(value ?? "", "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$")) throw new InvalidDataException("Version file/history must contain plain MAJOR.MINOR.PATCH.");
            int[] parts = value.Split('.').Select(int.Parse).ToArray();
            checked { switch (increment) { case "major": parts[0]++; parts[1] = parts[2] = 0; break; case "minor": parts[1]++; parts[2] = 0; break; case "patch": parts[2]++; break; default: throw new InvalidDataException("Select major, minor or patch."); } }
            return string.Join(".", parts);
        }
    }
}
