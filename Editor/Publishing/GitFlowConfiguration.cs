// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>No-code/API gate draft; tool hashing is performed by the shared policy review, never hand-entered.</summary>
    public sealed class GitFlowGate
    {
        public string Id { get; set; } = "required_check";
        public string Executable { get; set; } = "";
        public string[] Arguments { get; set; } = Array.Empty<string>();
        public string[] AdditionalToolFiles { get; set; } = Array.Empty<string>();
        public int TimeoutSeconds { get; set; } = 300;
    }

    /// <summary>Immutable exact-byte policy review; applying it refuses stale or malformed original data.</summary>
    public sealed class GitFlowPolicyReview
    {
        public string Path { get; }
        public string PolicyJson { get; }
        internal string OriginalHash { get; }
        /// <summary>Freezes the reviewed policy and original raw-file identity.</summary>
        internal GitFlowPolicyReview(string path, string json, string originalHash) { Path = path; PolicyJson = json; OriginalHash = originalHash; }
    }

    /// <summary>Explicit configuration and trusted committed-runner launch shared by Editor controls and supported code.</summary>
    public sealed class GitFlowConfiguration
    {
        /// <summary>Builds a read-only policy review from typed controls and verified console tools; no repository/index writes occur.</summary>
        public Task<GitFlowPolicyReview> ReviewPolicyAsync(string repository, string versionFile, string[] protectedPaths, GitFlowGate[] gates)
            => Task.Run(() =>
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetFullPath(repository), GitFlowGit.PolicyFile); GitFlowGit.CheckPath(path);
                string original = null;
                if (File.Exists(path)) { byte[] bytes = File.ReadAllBytes(path); new GitFlowPolicy(new UTF8Encoding(false, true).GetString(bytes)); original = GitFlowGit.Hash(bytes); }
                var commands = new JArray();
                foreach (GitFlowGate gate in gates ?? Array.Empty<GitFlowGate>())
                {
                    if (!System.IO.Path.IsPathRooted(gate.Executable)) throw new InvalidDataException("Select an absolute native console executable path.");
                    GitFlowProcess.RequireConsoleExecutable(gate.Executable);
                    commands.Add(new JObject { ["id"] = gate.Id, ["executable"] = System.IO.Path.GetFullPath(gate.Executable), ["executable_sha256"] = GitFlowGit.Hash(File.ReadAllBytes(gate.Executable)), ["tool_files"] = ToolFiles(gate.Executable, gate.AdditionalToolFiles), ["arguments"] = new JArray(gate.Arguments), ["timeout_seconds"] = gate.TimeoutSeconds });
                }
                var json = new JObject { ["schema"] = 1, ["adapter"] = "generic_git", ["publication"] = "atomic_main", ["headless_silent_commands"] = true,
                    ["version_file"] = versionFile, ["protected_paths"] = new JArray(protectedPaths ?? Array.Empty<string>()), ["gates"] = commands };
                new GitFlowPolicy(json.ToString());
                string versionPath = System.IO.Path.Combine(repository, versionFile.Replace('/', System.IO.Path.DirectorySeparatorChar)); GitFlowGit.CheckPath(versionPath);
                GitFlowPolicy.NextVersion(File.ReadAllText(versionPath).Trim(), "patch");
                return new GitFlowPolicyReview(path, json.ToString(Formatting.Indented), original);
            });

        /// <summary>Pins declared external dependencies and ordinary adjacent .NET apphost assemblies/configuration, whose executable stub alone is insufficient.</summary>
        internal static JArray ToolFiles(string executable, string[] additional = null)
        {
            if ((additional ?? Array.Empty<string>()).Any(path => !System.IO.Path.IsPathRooted(path))) throw new InvalidDataException("Additional external tool dependencies must be absolute paths.");
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(executable)), stem = System.IO.Path.GetFileNameWithoutExtension(executable);
            string[] adjacent = File.Exists(System.IO.Path.Combine(directory, stem + ".runtimeconfig.json")) ? Directory.GetFiles(directory).Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)).ToArray() : Array.Empty<string>();
            var files = new JArray();
            foreach (string path in adjacent.Concat(additional ?? Array.Empty<string>()).Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.Ordinal))
            { GitFlowGit.CheckPath(path); files.Add(new JObject { ["path"] = path, ["sha256"] = GitFlowGit.Hash(File.ReadAllBytes(path)) }); }
            if (files.Count > 64) throw new InvalidDataException("More than 64 tool dependency files; use an owning bundled validation tool."); return files;
        }

        /// <summary>Applies exactly one reviewed policy file without staging, committing, switching branches or changing other files.</summary>
        public void ApplyPolicy(GitFlowPolicyReview review)
        {
            if (review == null) throw new ArgumentNullException(nameof(review)); GitFlowGit.CheckPath(review.Path);
            string current = File.Exists(review.Path) ? GitFlowGit.Hash(File.ReadAllBytes(review.Path)) : null;
            if (current != review.OriginalHash) throw new IOException("Policy changed after review; reload and review its exact current values.");
            if (File.Exists(review.Path)) new GitFlowPolicy(File.ReadAllText(review.Path));
            new GitFlowPolicy(review.PolicyJson);
            string temporary = review.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                GitFlowGit.Write(temporary, review.PolicyJson);
                if ((File.Exists(review.Path) ? GitFlowGit.Hash(File.ReadAllBytes(review.Path)) : null) != review.OriginalHash) throw new IOException("Policy changed while saving. Preserve and review it again.");
                if (File.Exists(review.Path)) File.Replace(temporary, review.Path, null); else File.Move(temporary, review.Path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        /// <summary>Creates or updates only a stopped, explicitly dedicated root's bound configuration; retained candidates/evidence remain intact.</summary>
        public void ConfigurePublisher(GitFlowPublisherOptions options)
        {
            options.Validate(); if (!string.IsNullOrEmpty(options.resumeRequestId) || !string.IsNullOrEmpty(options.resumeReason)) throw new InvalidDataException("Scoped recovery is transient; saved host configuration remains in all-requests mode.");
            Directory.CreateDirectory(options.root);
            using (var owner = new FileStream(System.IO.Path.Combine(options.root, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                string marker = System.IO.Path.Combine(options.root, "owner.json");
                var binding = new JObject { ["schema"] = 1, ["protocol"] = 1, ["remote"] = options.remote, ["runner"] = "0.2.0" };
                if (File.Exists(marker)) { if (!JToken.DeepEquals(GitFlowGit.Json(File.ReadAllText(marker)), binding)) throw new InvalidDataException("Configured root belongs to another remote/runner; no replacement or migration is implicit."); }
                else
                {
                    if (Directory.GetFileSystemEntries(options.root).Any(path => System.IO.Path.GetFileName(path) != "owner.lock")) throw new IOException("Selected root has unknown files. Preserve it and select an empty dedicated root.");
                    GitFlowGit.Write(marker, binding.ToString(Formatting.Indented));
                }
                string config = System.IO.Path.Combine(options.root, "configuration.json");
                if (File.Exists(config)) ReadOptions(File.ReadAllText(config));
                string temporary = config + ".tmp"; GitFlowGit.Write(temporary, JsonConvert.SerializeObject(options, Formatting.Indented));
                if (File.Exists(config)) File.Replace(temporary, config, null); else File.Move(temporary, config);
            }
        }

        /// <summary>Validates a complete known host schema, preserving unknown/future settings instead of ignoring requirements.</summary>
        public static GitFlowPublisherOptions ReadOptions(string json)
        {
            JObject data = GitFlowGit.Json(json);
            foreach (var property in data.Properties()) if (!new[] { "schema", "root", "remote", "originalCheckout", "resumeRequestId", "resumeReason" }.Contains(property.Name)) throw new InvalidDataException("Unknown host setting: " + property.Name);
            GitFlowPublisherOptions options = data.ToObject<GitFlowPublisherOptions>(); options.Validate(); return options;
        }

        /// <summary>Starts a reviewed release runner independently; package-bound hashes prevent silent runner replacement or self-upgrades.</summary>
        public int StartPublisher(string root, string runnerExecutable, string trustedReleaseManifest, bool serve = true, string resumeId = "", string resumeReason = "")
        {
            GitFlowGit.CheckPath(root); GitFlowProcess.RequireConsoleExecutable(runnerExecutable);
            JObject manifest = GitFlowGit.Json(trustedReleaseManifest);
            if ((int?)manifest["schema"] != 1 || (string)manifest["version"] != "0.2.0" || !(manifest["files"] is JObject files) || files.Count < 5) throw new InvalidDataException("Required package-bound runner release manifest is absent/incompatible. Extract the matching immutable runner release first.");
            string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(runnerExecutable));
            foreach (var file in files.Properties())
            {
                if (System.IO.Path.GetFileName(file.Name) != file.Name) throw new InvalidDataException("Runner manifest path escapes its directory.");
                string path = System.IO.Path.Combine(directory, file.Name); GitFlowGit.CheckPath(path);
                if (GitFlowGit.Hash(File.ReadAllBytes(path)) != (string)file.Value) throw new IOException("Runner file differs from the installed package's release manifest: " + file.Name + ". Preserve the configured runner and explicitly adopt the verified release.");
            }
            if (!files.ContainsKey(System.IO.Path.GetFileName(runnerExecutable))) throw new InvalidDataException("Executable is not part of the trusted runner manifest.");
            GitFlowPublisherOptions options = ReadOptions(File.ReadAllText(System.IO.Path.Combine(root, "configuration.json")));
            if (System.IO.Path.GetFullPath(options.root) != System.IO.Path.GetFullPath(root)) throw new InvalidDataException("Configured root mismatch.");
            ProcessStartInfo start;
            using (var owner = new FileStream(System.IO.Path.Combine(root, "owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                string stop = System.IO.Path.Combine(root, "stop.request"); if (File.Exists(stop)) File.Delete(stop);
                options.resumeRequestId = resumeId; options.resumeReason = resumeReason; options.Validate();
                string invocation = System.IO.Path.Combine(root, "invocation-" + Guid.NewGuid().ToString("N") + ".json");
                GitFlowGit.Write(invocation, JsonConvert.SerializeObject(options, Formatting.Indented));
                start = new ProcessStartInfo(System.IO.Path.GetFullPath(runnerExecutable)) { WorkingDirectory = directory,
                    Arguments = (serve && string.IsNullOrEmpty(resumeId) ? "serve" : "once") + " " + GitFlowGit.Quote(invocation) + " " + GitFlowGit.Quote(invocation + ".outcome.json") };
                start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
                start.EnvironmentVariables["DOTNET_DISABLE_GUI_ERRORS"] = "1";
            }
            // The native host must acquire its own OS lock; release the admission probe before it starts.
            return GitFlowProcess.StartDetached(start);
        }

        /// <summary>Requests a graceful stop between tasks; never kills another host or claims that an acknowledgement proves it stopped.</summary>
        public void RequestStop(string root)
        { GitFlowPublisherOptions options = ReadOptions(File.ReadAllText(System.IO.Path.Combine(root, "configuration.json"))); if (System.IO.Path.GetFullPath(options.root) != System.IO.Path.GetFullPath(root)) throw new InvalidDataException("Root mismatch."); GitFlowGit.Write(System.IO.Path.Combine(root, "stop.request"), DateTime.UtcNow.ToString("O")); }
    }
}
