// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Geurts.GameForge.God;
using Newtonsoft.Json.Linq;
using UnityEditor.PackageManager;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Shared Editor bridge to explicit deterministic host actions and local Codex metadata.</summary>
    public sealed class GitFlowHost : IDisposable
    {
        private readonly BrickSettingsStore _settings;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly string _package;
        private int _busy;

        /// <summary>Retains the owner settings service and installed package path without launching anything.</summary>
        public GitFlowHost(BrickSettingsStore settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _package = PackageInfo.FindForAssembly(typeof(GitFlowHost).Assembly)?.resolvedPath
                ?? throw new InvalidOperationException("GitFlow must be resolved by Unity Package Manager.");
        }

        /// <summary>Reads only this package's supported non-secret settings; invalid data remains an error.</summary>
        public GitFlowPreferences Load() => _settings.Load<GitFlowPreferences>(GitFlowBrick.PackageId);

        /// <summary>Saves selected paths/model/effort without starting a model request or processor.</summary>
        public void Save(GitFlowPreferences preferences)
        {
            string error = preferences.ValidateSettings();
            if (!string.IsNullOrEmpty(error)) throw new InvalidDataException(error);
            _settings.Save(GitFlowBrick.PackageId, preferences);
        }

        /// <summary>Queries the installed Codex metadata interface; no inference or project context is sent.</summary>
        public Task<JObject> DiscoverModelsAsync(GitFlowPreferences preferences, CancellationToken cancellation = default)
            => RunAsync(preferences.PythonExecutable, "GitFlowModels.py", new[] { "--executable", preferences.CodexExecutable, "list-models" }, 45, cancellation);

        /// <summary>Executes an explicit supported host command; callers retain the operation's authorization boundary.</summary>
        public Task<JObject> ExecuteQueueAsync(GitFlowPreferences preferences, IEnumerable<string> arguments, int timeoutSeconds = 60, CancellationToken cancellation = default)
            => RunAsync(preferences.PythonExecutable, "GitFlowQueue.py", new[] { "--repository", preferences.Repository, "--remote", preferences.Remote }.Concat(arguments), timeoutSeconds, cancellation);

        /// <summary>Runs one authorized proposal request without applying it; supplied context must be separately reviewed.</summary>
        public Task<JObject> DiagnoseAsync(GitFlowPreferences preferences, string contextPath, CancellationToken cancellation = default)
            => RunAsync(preferences.PythonExecutable, "GitFlowModels.py", new[] { "--executable", preferences.CodexExecutable, "diagnose", "--model", preferences.Model,
                "--effort", preferences.ReasoningEffort, "--context", contextPath, "--authorize-diagnosis" }, 650, cancellation);

        /// <summary>Configures a new exclusive Unity publisher using native version APIs and required project-selected tests.</summary>
        public async Task<JObject> ConfigureUnityAsync(GitFlowPreferences preferences, string workspace, string unity, string testFilter, bool automaticRepair, CancellationToken cancellation = default)
        {
            if (!File.Exists(unity) || string.IsNullOrWhiteSpace(testFilter)) throw new ArgumentException("Select the installed Unity Editor and a required project test filter.");
            string adapter = Path.Combine(_package, "Tools~", "Queue", "GitFlowUnity.py");
            var common = new[] { "--repository", "{repository}", "--unity", unity, "--evidence", "{evidence}" };
            var checks = new JArray();
            foreach (string action in new[] { "compile-project", "run-tests" })
            {
                var command = new List<string> { preferences.PythonExecutable, adapter, action };
                command.AddRange(common); command.AddRange(new[] { "--run", "{run}", "--candidate", "{candidate}" });
                if (action == "run-tests") command.AddRange(new[] { "--test-filter", testFilter });
                checks.Add(new JObject { ["name"] = action == "compile-project" ? "compile_project" : "validate_project", ["kind"] = action == "compile-project" ? "compilation" : "final",
                    ["command"] = new JArray(command), ["inputs"] = new JArray("."), ["timeout_seconds"] = 7200 });
            }
            var versionCommand = new List<string> { preferences.PythonExecutable, adapter, "synchronize-version" };
            versionCommand.AddRange(common); versionCommand.AddRange(new[] { "--version", "{version}" });
            var policy = new JObject { ["schema"] = 1, ["version_file"] = "ProjectSettings/GeurtsGameForgeQueueVersion.txt", ["checks"] = checks,
                ["native_version_command"] = new JArray(versionCommand), ["native_version_outputs"] = new JArray("ProjectSettings/ProjectSettings.asset"),
                ["repair"] = new JObject { ["enabled"] = automaticRepair, ["window_seconds"] = 10800, ["executable"] = preferences.CodexExecutable,
                    ["model"] = preferences.Model, ["effort"] = preferences.ReasoningEffort } };
            string temporary = Path.Combine(Path.GetTempPath(), "gitflow_policy_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(temporary, policy.ToString(), new UTF8Encoding(false));
                return await ExecuteQueueAsync(preferences, new[] { "configure", "--operator", "--workspace-root", workspace, "--policy", temporary }, 60, cancellation);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        /// <summary>Starts an independent windowless Python host; the host's OS locks reject duplicate service ownership.</summary>
        public void StartPublisher(GitFlowPreferences preferences)
        {
            string tool = Path.Combine(_package, "Tools~", "Queue", "GitFlowQueue.py");
            if (!File.Exists(preferences.PythonExecutable) || !File.Exists(tool)) throw new FileNotFoundException("The selected Python executable or queue runner is missing.");
            var start = new ProcessStartInfo(preferences.PythonExecutable) { WorkingDirectory = _package, UseShellExecute = false, CreateNoWindow = true,
                Arguments = string.Join(" ", new[] { tool, "--repository", preferences.Repository, "serve", "--operator" }.Select(Quote)) };
            using (var child = Process.Start(start))
                if (child == null) throw new IOException("The publisher did not start.");
        }

        /// <summary>Serializes bridge requests and contains every native descendant through cancellation and reload.</summary>
        private async Task<JObject> RunAsync(string python, string script, IEnumerable<string> arguments, int timeoutSeconds, CancellationToken cancellation)
        {
            if (Interlocked.Exchange(ref _busy, 1) != 0) throw new InvalidOperationException("A GitFlow host request is already running.");
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _stopping.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    string tool = Path.Combine(_package, "Tools~", "Queue", script);
                    if (!File.Exists(tool)) throw new FileNotFoundException("The installed GitFlow host is incomplete.", tool);
                    var start = new ProcessStartInfo { WorkingDirectory = _package, UseShellExecute = false, CreateNoWindow = true,
                        Arguments = string.Join(" ", new[] { tool }.Concat(arguments).Select(Quote)) };
                    string output = await Task.Run(() => GitFlowProcess.RunExecutable(start, python, timeout.Token), timeout.Token);
                    return JObject.Parse(output);
                }
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        /// <summary>Quotes one Windows argument without evaluating shell syntax or expanding user text.</summary>
        private static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value ?? "")
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') result.Append('\\', slashes * 2 + 1).Append('"');
                else result.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        /// <summary>Cancels this lifecycle owner's in-flight metadata/proposal children; independent processors retain their ownership.</summary>
        public void Dispose() { _stopping.Cancel(); }
    }

    /// <summary>Non-secret project preferences. No field grants source changes, inference or background execution.</summary>
    [Serializable]
    public sealed class GitFlowPreferences : IValidatedBrickSettings
    {
        public int Schema = 1;
        public string PythonExecutable = "";
        public string CodexExecutable = "";
        public string Model = "";
        public string ReasoningEffort = "";
        public string Repository = "";
        public string Remote = "origin";

        /// <summary>Rejects unsupported payloads and malformed values while allowing disconnected initial setup.</summary>
        public string ValidateSettings()
        {
            if (Schema != 1) return "Unsupported GitFlow settings format. Preserve the existing file.";
            if (new[] { PythonExecutable, CodexExecutable, Model, ReasoningEffort, Repository, Remote }.Any(value => value == null || value.Length > 259 || value.Contains('\0')))
                return "GitFlow settings contain an invalid or oversized value.";
            return null;
        }
    }
}
