// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
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
    /// <summary>Explicit host configuration. The dedicated processor root is never inferred from a consumer checkout.</summary>
    [Serializable]
    public sealed class GitFlowPublisherOptions
    {
        public int schema = 1;
        public string root = "", remote = "", originalCheckout = "";
        public string resumeRequestId = "", resumeReason = "";
        /// <summary>Checks configuration before ownership or remote mutation; unknown roots/settings remain visible.</summary>
        public void Validate()
        {
            if (schema != 1 || !Path.IsPathRooted(root ?? "")) throw new InvalidDataException("Select a dedicated absolute publisher root with options schema 1.");
            GitFlowGit.CheckPath(Path.Combine(root, "store.git/objects/pack/pack-" + new string('a', 40) + ".pack")); GitFlowGit.CheckRemote(remote);
            if (!string.IsNullOrEmpty(originalCheckout)) { if (!Path.IsPathRooted(originalCheckout)) throw new InvalidDataException("Original checkout must be absolute."); GitFlowGit.CheckPath(originalCheckout); }
            if (!string.IsNullOrEmpty(resumeRequestId) && (!System.Text.RegularExpressions.Regex.IsMatch(resumeRequestId, "^gf_[0-9a-f]{40}$") || string.IsNullOrWhiteSpace(resumeReason))) throw new InvalidDataException("Recovery requires the exact original request ID and an operator reason.");
        }
    }

    /// <summary>Finite observed host outcome; pending/busy/stopped states never claim publication.</summary>
    public sealed class GitFlowPublisherResult
    {
        public string State { get; internal set; }
        public string Detail { get; internal set; }
        public int Delivered { get; internal set; }
    }

    /// <summary>Independent deterministic generic publisher with one owned checkout, local exclusivity, strict gates and atomic lease fencing.</summary>
    /// <remarks>Help: Documentation~/Publisher.md. Run in the supplied native host, never from a waiting worker.</remarks>
    public sealed class GitFlowPublisher
    {
        /// <summary>Runs one bounded queue pass, or a configured host loop without chat/Editor ownership; cancellation ends owned checks.</summary>
        public Task<GitFlowPublisherResult> RunAsync(GitFlowPublisherOptions options, bool serve = false, CancellationToken cancellation = default)
            => Task.Run(() => Run(options, serve, cancellation), cancellation);

        /// <summary>Owns one root for the entire native host lifetime and retains all candidate/failure evidence across shutdown.</summary>
        private static GitFlowPublisherResult Run(GitFlowPublisherOptions options, bool serve, CancellationToken cancellation)
        {
            options.Validate(); string root = Path.GetFullPath(options.root), store = Path.Combine(root, "store.git"), checkout = Path.Combine(root, "candidate");
            Directory.CreateDirectory(root);
            using (var localOwner = new FileStream(Path.Combine(root, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                string marker = Path.Combine(root, "owner.json");
                var binding = new JObject { ["schema"] = 1, ["protocol"] = 1, ["remote"] = options.remote, ["runner"] = "0.2.0" };
                if (File.Exists(marker))
                {
                    if (!JToken.DeepEquals(GitFlowGit.Json(File.ReadAllText(marker)), binding)) throw new InvalidDataException("Root belongs to another configuration/runner. Preserve it and use the explicit stopped-runner upgrade procedure.");
                }
                else
                {
                    if (Directory.GetFileSystemEntries(root).Any(path => Path.GetFileName(path) != "owner.lock")) throw new IOException("Publisher root contains unknown work. Select a new empty dedicated root; no files were removed.");
                    GitFlowGit.Write(marker, binding.ToString(Formatting.Indented));
                }
                string configuration = Path.Combine(root, "configuration.json");
                if (File.Exists(configuration))
                {
                    GitFlowPublisherOptions configured = GitFlowConfiguration.ReadOptions(File.ReadAllText(configuration));
                    if (configured.remote != options.remote || Path.GetFullPath(configured.root) != root || configured.originalCheckout != options.originalCheckout)
                        throw new InvalidDataException("Invocation differs from preserved host configuration. Review it explicitly while stopped.");
                }
                Directory.CreateDirectory(store); var git = new GitFlowGit(store, options.remote);
                if (!File.Exists(Path.Combine(store, "HEAD"))) git.Run(cancellation, "init", "--bare", "--quiet");
                var result = new GitFlowPublisherResult { State = "stopped", Detail = "No queue pass has run." };
                var stateGuard = new object(); Action<string> health = phase => { lock (stateGuard) WriteState(root, phase, result); };
                using (var lease = new GitFlowLease(git, health))
                {
                    try
                    {
                        do
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (File.Exists(Path.Combine(root, "stop.request"))) { result.State = "stopped"; result.Detail = "Graceful stop requested; settings and candidate retained."; break; }
                            // Preflight known dirty work and the actual tools before claiming any request.
                            var refs = git.Refs(cancellation, "refs/heads/main", GitFlowGit.RequestPrefix + "*");
                            if (!refs.TryGetValue("refs/heads/main", out string main)) throw new InvalidDataException("Remote main is absent.");
                            git.Fetch(main, cancellation); var policy = new GitFlowPolicy(git.Run(cancellation, "show", main + ":" + GitFlowGit.PolicyFile));
                            PreflightTools(policy);
                            if (File.Exists(checkout)) throw new IOException("Dedicated candidate directory is occupied by an unknown file. Preserve it; no task was claimed.");
                            if (Directory.Exists(checkout))
                            {
                                var retained = new GitFlowGit(checkout, options.remote); EnsureClean(retained, cancellation);
                                retained.Run(cancellation, "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", options.remote, main);
                                if (string.IsNullOrEmpty(options.resumeRequestId) && retained.Run(cancellation, "rev-list", "--max-count=1", main + "..HEAD").Trim().Length != 0)
                                    throw new IOException("Processor retains a committed candidate ahead of remote main. Resume its original request explicitly; unrelated queued requests remain pending.");
                            }
                            if (!lease.Active && !lease.Acquire(cancellation)) { result.State = "busy"; result.Detail = "Compatible owner observed; monotonic abandonment interval not yet proven."; }
                            else
                            {
                                result.State = "idle"; result.Detail = "Owned queue pass; no eligible request observed.";
                                var rows = new List<(string reference, string commit, JObject data)>();
                                foreach (var pair in refs.Where(pair => pair.Key.StartsWith(GitFlowGit.RequestPrefix, StringComparison.Ordinal)))
                                { git.Fetch(pair.Value, cancellation); JObject data = git.Record(pair.Value, cancellation); ValidateRequest(pair.Key, data); rows.Add((pair.Key, pair.Value, data)); }
                                var eligible = rows.Where(row => string.IsNullOrEmpty(options.resumeRequestId) ? (string)row.data["status"] == "queued" : (string)row.data["id"] == options.resumeRequestId)
                                    .OrderBy(row => (string)row.data["submitted_utc"], StringComparer.Ordinal).ThenBy(row => row.reference, StringComparer.Ordinal).ToArray();
                                if (!string.IsNullOrEmpty(options.resumeRequestId) && eligible.Length != 1) throw new InvalidDataException("Recovery request is absent; preserve the original ID.");
                                foreach (var row in eligible)
                                {
                                    cancellation.ThrowIfCancellationRequested();
                                    if (File.Exists(Path.Combine(root, "stop.request"))) break;
                                    Process(options, git, lease, checkout, row.reference, row.commit, row.data, rows.Select(value => value.data), result, cancellation);
                                    health(result.State);
                                    if (result.State == "failed" || result.State == "partial") return result;
                                }
                            }
                            health(result.State);
                            if (serve && string.IsNullOrEmpty(options.resumeRequestId)) cancellation.WaitHandle.WaitOne(TimeSpan.FromSeconds(30)); else break;
                        } while (serve);
                        return result;
                    }
                    catch (Exception error)
                    { result.State = "failed"; result.Detail = error.GetType().Name + ": " + error.Message + " Preserve this configured root and its evidence; do not create replacement requests."; health("failed"); return result; }
                    finally { health("stopping: " + result.State); }
                }
            }
        }

        /// <summary>Integrates one original source, reserves one version and publishes a receipt only after strict final-candidate gates.</summary>
        private static void Process(GitFlowPublisherOptions options, GitFlowGit git, GitFlowLease lease, string checkout, string reference, string control,
            JObject data, IEnumerable<JObject> reservations, GitFlowPublisherResult result, CancellationToken cancellation)
        {
            string id = (string)data["id"], source = (string)data["source"], candidate = null;
            var attempts = data["attempts"] as JArray;
            if (attempts == null) { attempts = new JArray(); data["attempts"] = attempts; }
            var attempt = new JObject { ["started_utc"] = DateTime.UtcNow.ToString("O"), ["operator_reason"] = options.resumeReason, ["phase"] = "preflight", ["checks"] = new JArray() }; attempts.Add(attempt);
            if (attempts.Count > 8) throw new InvalidDataException("Eight-attempt bound exhausted. Preserve original evidence; no budget reset or replacement ID.");
            var code = new GitFlowGit(checkout, options.remote);
            try
            {
                var refs = git.Refs(cancellation, "refs/heads/main", reference);
                if (!refs.TryGetValue(reference, out string current) || current != control) throw new IOException("Request changed before processing.");
                string main = refs["refs/heads/main"]; git.Fetch(main, cancellation); git.Fetch(source, cancellation);
                var policy = new GitFlowPolicy(git.Run(cancellation, "show", main + ":" + GitFlowGit.PolicyFile));
                if (policy.Fingerprint != (string)data["policy_sha256"]) throw new InvalidDataException("Project policy changed since handoff; an operator must reconcile the original request without changing its source/version identity.");
                GitFlowQueue.CheckScope(git, policy, (string)data["base"], source, cancellation);
                git.Run(cancellation, "merge-base", "--is-ancestor", (string)data["base"], main);
                if (!Directory.Exists(checkout)) git.Run(cancellation, "clone", "--quiet", "--no-hardlinks", "--branch", "main", options.remote, checkout);
                EnsureClean(code, cancellation); CheckFiles(checkout);
                CheckTree(code, cancellation);
                // An acknowledged atomic receipt is recovered under its original commit/version, even if a prior pull failed.
                string delivered = (string)data["main_commit"];
                if (!string.IsNullOrEmpty(delivered))
                {
                    GitFlowGit.RequireSha(delivered); git.Run(cancellation, "merge-base", "--is-ancestor", delivered, main);
                    if (data["receipt"] == null || (string)data["receipt"]["source"] != source || (string)data["receipt"]["version"] != (string)data["version"]) throw new InvalidDataException("Delivered receipt identity is missing or incompatible.");
                    FinishPulls(options, code, data, cancellation); result.State = (string)data["status"]; result.Detail = "Recovered original receipt " + id + " at " + delivered + "; no version/check/publication was repeated.";
                    string recovered = git.Commit(data, new[] { control, delivered }, cancellation); lease.Publish(new Dictionary<string, string> { [reference] = recovered }, false, cancellation); return;
                }
                string head = code.Run(cancellation, "rev-parse", "HEAD").Trim();
                if ((string)data["status"] != "queued")
                {
                    if (string.IsNullOrEmpty(options.resumeRequestId)) throw new InvalidDataException("Original failed/in-flight request requires an explicit same-ID operator continuation.");
                    if ((string)data["integrated_main"] != main || (string)data["candidate"] != head) throw new InvalidDataException("Retained candidate/main differ from the recorded checkpoint. Preserve bytes/index and obtain a reviewed recovery adapter; no reset or new version.");
                    if (string.IsNullOrEmpty((string)data["version"])) throw new InvalidDataException("Failed pre-checkpoint integration has no reusable version/candidate. Resolve the retained conflict through the owning operator; automatic reset is forbidden.");
                    candidate = head;
                }
                else
                {
                    if (head != main)
                    {
                        code.Run(cancellation, "pull", "--ff-only", options.remote, "main"); head = code.Run(cancellation, "rev-parse", "HEAD").Trim();
                        if (head != main) throw new IOException("Remote main moved during the pass or the retained candidate diverged. Preserve it and refresh the owning processor; no reset.");
                    }
                    if (git.Run(cancellation, "rev-list", "--max-count=1", main + ".." + source).Trim().Length == 0) throw new InvalidDataException("Submitted source is already in main without a matching queue receipt. Obtain owning version/publication evidence; no duplicate version is assigned.");
                    attempt["phase"] = "merging";
                    code.Run(cancellation, "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", options.remote, source);
                    code.Run(cancellation, "merge", "--no-ff", "--no-commit", source);
                    CheckFiles(checkout);
                    string versionPath = Path.Combine(checkout, policy.VersionFile.Replace('/', Path.DirectorySeparatorChar)); GitFlowGit.CheckPath(versionPath);
                    byte[] originalVersion = File.ReadAllBytes(versionPath); string versionText = new UTF8Encoding(false, true).GetString(originalVersion);
                    string priorVersion = versionText.Trim(); GitFlowPolicy.NextVersion(priorVersion, "patch");
                    string highest = reservations.Where(row => !string.IsNullOrEmpty((string)row["version"])).Select(row => (string)row["version"]).Concat(new[] { priorVersion }).OrderBy(value => Version.Parse(value)).Last();
                    string version = GitFlowPolicy.NextVersion(highest, (string)data["increment"]);
                    // Preserve the declared file's exact BOM/newline shape; mixed/ambiguous formatting is rejected.
                    if (versionText != priorVersion && versionText != priorVersion + "\n" && versionText != priorVersion + "\r\n") throw new InvalidDataException("Version file must contain only plain SemVer and one optional LF/CRLF; preserve and correct it through the policy owner.");
                    string suffix = versionText.Substring(priorVersion.Length); File.WriteAllText(versionPath, version + suffix, new UTF8Encoding(false));
                    code.Run(cancellation, "add", "--", policy.VersionFile);
                    code.Run(cancellation, "diff", "--check", main);
                    code.Run(cancellation, "commit", "-m", "GitFlow " + id + " — " + version);
                    candidate = code.Run(cancellation, "rev-parse", "HEAD").Trim();
                    data["version"] = version; data["candidate"] = candidate; data["integrated_main"] = main; data["status"] = "processing";
                    git.Run(cancellation, "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", checkout, candidate);
                    control = git.Commit(data, new[] { control, candidate }, cancellation);
                    lease.Publish(new Dictionary<string, string> { [reference] = control, [GitFlowGit.Prefix + "wip/" + id] = candidate }, false, cancellation);
                }
                EnsureClean(code, cancellation); CheckTree(code, cancellation); attempt["phase"] = "validating"; data["status"] = "validating";
                control = git.Commit(data, new[] { control, candidate }, cancellation); lease.Publish(new Dictionary<string, string> { [reference] = control }, false, cancellation);
                string tree = code.Run(cancellation, "rev-parse", "HEAD^{tree}").Trim(); attempt["candidate"] = candidate; attempt["tree"] = tree; attempt["policy_sha256"] = policy.Fingerprint;
                foreach (JObject gate in (JArray)policy.Data["gates"])
                {
                    if (!lease.Active) throw new IOException("Lease heartbeat lost; final gates cannot authorize publication.");
                    RunGate(options.root, checkout, gate, (JArray)attempt["checks"], cancellation);
                    EnsureClean(code, cancellation); CheckFiles(checkout);
                    if (code.Run(cancellation, "rev-parse", "HEAD").Trim() != candidate || code.Run(cancellation, "rev-parse", "HEAD^{tree}").Trim() != tree) throw new IOException("Validation changed the candidate commit/tree. Retained for review; no reset or publication.");
                }
                attempt["finished_utc"] = DateTime.UtcNow.ToString("O"); attempt["phase"] = "validated";
                data["receipt"] = new JObject { ["schema"] = 1, ["id"] = id, ["source"] = source, ["version"] = data["version"], ["candidate"] = candidate,
                    ["tree"] = tree, ["policy_sha256"] = policy.Fingerprint, ["validation"] = attempt.DeepClone(), ["unverified"] = "Worker claims are historical; GUI/native/player behavior is covered only by explicitly declared gates. Immutable releases/PR policies require a different adapter." };
                data["main_commit"] = candidate; data["status"] = "published_pending_pull"; data.Remove("blocker");
                string publication = git.Commit(data, new[] { control, candidate }, cancellation);
                attempt["phase"] = "publishing";
                // main and its immutable result/receipt become visible in the same all-or-nothing fenced transaction.
                lease.Publish(new Dictionary<string, string> { ["refs/heads/main"] = candidate, [reference] = publication }, false, cancellation);
                control = publication;
                FinishPulls(options, code, data, cancellation);
                string completed = git.Commit(data, new[] { control, candidate }, cancellation); lease.Publish(new Dictionary<string, string> { [reference] = completed }, false, cancellation);
                result.State = (string)data["status"]; result.Detail = id + " version " + (string)data["version"] + " at verified " + candidate + ". " + (string)data["pull_detail"];
                if (result.State == "delivered") result.Delivered++;
            }
            catch (Exception error)
            {
                string failure = "Request " + id + ", phase " + (string)attempt["phase"] + ", " + error.GetType().Name + ": " + error.Message + ". Preserve the original ID, version, candidate/index and evidence; correct the named problem before explicit same-ID continuation.";
                attempt["finished_utc"] = DateTime.UtcNow.ToString("O"); attempt["exception"] = failure;
                data["status"] = string.IsNullOrEmpty((string)data["main_commit"]) ? "failed" : "published_pending_pull"; data["blocker"] = failure;
                GitFlowGit.Write(Path.Combine(options.root, "failure-" + id + ".json"), data.ToString(Formatting.Indented));
                if (lease.Active)
                {
                    // Exception cleanup is fenced too; an uncertain owner keeps local evidence and makes no further writes.
                    try { string failed = git.Commit(data, new[] { control, candidate }, cancellation); lease.Publish(new Dictionary<string, string> { [reference] = failed }, false, cancellation); }
                    catch (Exception fence) { failure += " Failure publication unavailable: " + fence.GetType().Name + ": " + fence.Message; }
                }
                result.State = "failed"; result.Detail = failure;
            }
        }

        /// <summary>Verifies each declared tool's current raw bytes and native console subsystem before acquiring ownership.</summary>
        private static void PreflightTools(GitFlowPolicy policy)
        {
            foreach (JObject gate in (JArray)policy.Data["gates"])
            {
                string path = (string)gate["executable"]; GitFlowProcess.RequireConsoleExecutable(path); if (GitFlowGit.Hash(File.ReadAllBytes(path)) != (string)gate["executable_sha256"]) throw new IOException("Required gate tool changed/unavailable: " + (string)gate["id"] + ". Review its owning policy; no automatic tool upgrade.");
                foreach (JObject dependency in (JArray)gate["tool_files"])
                    if (GitFlowGit.Hash(File.ReadAllBytes((string)dependency["path"])) != (string)dependency["sha256"]) throw new IOException("Required gate dependency changed/unavailable: " + (string)dependency["path"] + ". Review its owning policy.");
            }
        }

        /// <summary>Captures strict required checks against the actual candidate with bounded output, duration and descendant lifetime.</summary>
        private static void RunGate(string root, string checkout, JObject gate, JArray checks, CancellationToken cancellation)
        {
            PreflightTools(new GitFlowPolicy(new JObject { ["schema"] = 1, ["adapter"] = "generic_git", ["publication"] = "atomic_main", ["headless_silent_commands"] = true,
                ["version_file"] = "VERSION", ["protected_paths"] = new JArray(".geurts-gitflow.json"), ["gates"] = new JArray(gate.DeepClone()) }.ToString()));
            var proof = new JObject { ["id"] = gate["id"], ["command"] = gate.DeepClone(), ["started_utc"] = DateTime.UtcNow.ToString("O"), ["machine"] = Environment.MachineName,
                ["os"] = Environment.OSVersion.ToString(), ["runtime"] = Environment.Version.ToString(), ["model_invoked"] = false }; checks.Add(proof);
            var start = new ProcessStartInfo((string)gate["executable"]) { WorkingDirectory = checkout, UseShellExecute = false, CreateNoWindow = true,
                Arguments = string.Join(" ", ((JArray)gate["arguments"]).Select(arg => GitFlowGit.Quote((string)arg))) };
            foreach (string key in start.EnvironmentVariables.Keys.Cast<string>().Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.EnvironmentVariables.Remove(key);
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never"; start.EnvironmentVariables["CI"] = "true";
            start.EnvironmentVariables.Remove("GEURTS_GITFLOW_TOKEN");
            start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"; start.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            proof["environment_sha256"] = GitFlowGit.Hash(Encoding.UTF8.GetBytes(string.Join("\n", start.EnvironmentVariables.Keys.Cast<string>().OrderBy(key => key, StringComparer.Ordinal).Select(key => key + "=" + start.EnvironmentVariables[key]))));
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                budget.CancelAfter(TimeSpan.FromSeconds((int)gate["timeout_seconds"]));
                GitFlowProcess.Result outcome = GitFlowProcess.Capture(start, budget.Token);
                proof["finished_utc"] = DateTime.UtcNow.ToString("O"); proof["exit_code"] = outcome.ExitCode;
                string output = outcome.Output, error = outcome.Error;
                string evidence = Path.Combine(root, "gate-" + (string)gate["id"] + "-" + Guid.NewGuid().ToString("N"));
                GitFlowGit.CheckPath(evidence + ".stdout.txt");
                File.WriteAllText(evidence + ".stdout.txt", output, new UTF8Encoding(false)); File.WriteAllText(evidence + ".stderr.txt", error, new UTF8Encoding(false));
                proof["stdout_sha256"] = GitFlowGit.Hash(Encoding.UTF8.GetBytes(output)); proof["stderr_sha256"] = GitFlowGit.Hash(Encoding.UTF8.GetBytes(error));
                if (Encoding.UTF8.GetByteCount(output) > 32768 || Encoding.UTF8.GetByteCount(error) > 32768) throw new IOException("Gate output exceeds portable receipt bound (32 KiB per stream). Full local logs retained; provide a bounded owning gate.");
                proof["stdout"] = output; proof["stderr"] = error;
                if (outcome.ExitCode != 0) throw new IOException("Required gate " + (string)gate["id"] + " failed (exit " + outcome.ExitCode + "): " + error + output);
            }
        }

        /// <summary>Requires clean committed work, index and untracked/ignored state before integration or after validation.</summary>
        private static void EnsureClean(GitFlowGit code, CancellationToken cancellation)
        {
            if (code.Run(cancellation, "status", "--porcelain=v1", "--untracked-files=all", "--ignored").Length != 0) throw new IOException("Processor checkout contains retained/unknown work, index conflicts or generated output. Preserve it; the publisher never resets, stashes or cleans it.");
            if (code.Run(cancellation, "symbolic-ref", "--short", "HEAD").Trim() != "main") throw new IOException("Processor checkout is not on main; no branch is switched automatically.");
        }

        /// <summary>Rejects native reparse entries and overlong generated descendants before checks/publication.</summary>
        private static void CheckFiles(string checkout)
        {
            var directories = new Stack<string>(); directories.Push(checkout);
            while (directories.Count != 0) foreach (string path in Directory.GetFileSystemEntries(directories.Pop()))
            { GitFlowGit.CheckPath(path); if (Directory.Exists(path)) directories.Push(path); }
        }

        /// <summary>Refuses submodule and symlink entries whose content cannot be certified by this generic whole-tree adapter.</summary>
        private static void CheckTree(GitFlowGit code, CancellationToken cancellation)
        {
            foreach (string row in code.Run(cancellation, "ls-tree", "-r", "HEAD").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                if (!row.StartsWith("100644 blob ", StringComparison.Ordinal) && !row.StartsWith("100755 blob ", StringComparison.Ordinal)) throw new InvalidDataException("Generic adapter does not support submodules/symlink trees: " + row);
        }

        /// <summary>Performs required post-publication fast-forward pulls without switching branches or touching dirty original work.</summary>
        private static void FinishPulls(GitFlowPublisherOptions options, GitFlowGit code, JObject data, CancellationToken cancellation)
        {
            EnsureClean(code, cancellation); code.Run(cancellation, "pull", "--ff-only", options.remote, "main");
            string detail = "Integration checkout pulled with --ff-only."; bool complete = true;
            if (!string.IsNullOrEmpty(options.originalCheckout))
            {
                try
                { var original = new GitFlowGit(options.originalCheckout, options.remote); EnsureClean(original, cancellation); original.Run(cancellation, "pull", "--ff-only", options.remote, "main"); detail += " Configured original checkout pulled with --ff-only."; }
                catch (Exception error) { complete = false; detail += " Original checkout pull blocked, preserved unchanged before pull: " + error.GetType().Name + ": " + error.Message; }
            }
            else detail += " No original checkout configured; no worker checkout was modified.";
            data["pull_detail"] = detail; data["status"] = complete ? "delivered" : "published_pending_pull";
        }

        /// <summary>Validates the exact native namespace/source identity and every supported control phase.</summary>
        private static void ValidateRequest(string reference, JObject data)
        {
            string source = (string)data["source"], id = (string)data["id"]; GitFlowGit.RequireSha(source); GitFlowGit.RequireSha((string)data["base"]);
            if (!string.IsNullOrEmpty((string)data["version"])) GitFlowPolicy.NextVersion((string)data["version"], "patch");
            if ((int?)data["schema"] != 1 || (string)data["kind"] != "request" || (string)data["adapter"] != "generic_git" || id != "gf_" + source || reference != GitFlowGit.RequestPrefix + id ||
                !new[] { "queued", "processing", "validating", "delivered", "published_pending_pull", "failed" }.Contains((string)data["status"])) throw new InvalidDataException("Malformed/unknown native queue record: " + reference);
            if (!System.Text.RegularExpressions.Regex.IsMatch((string)data["policy_sha256"] ?? "", "^[0-9a-f]{64}$") || !DateTime.TryParse((string)data["submitted_utc"], out _)) throw new InvalidDataException("Native policy/time evidence missing: " + reference);
            if ((string)data["status"] == "delivered" || (string)data["status"] == "published_pending_pull")
            {
                GitFlowGit.RequireSha((string)data["main_commit"]);
                if (!(data["receipt"] is JObject receipt) || (string)receipt["source"] != source || (string)receipt["version"] != (string)data["version"] || (string)receipt["candidate"] != (string)data["main_commit"]) throw new InvalidDataException("Published record lacks its original matching receipt: " + reference);
            }
        }

        /// <summary>Records actual host identity/phase and audit time without claiming that a PID alone proves lease ownership.</summary>
        private static void WriteState(string root, string phase, GitFlowPublisherResult result)
        {
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
            {
                var state = new JObject { ["schema"] = 1, ["pid"] = process.Id, ["process_start_utc"] = process.StartTime.ToUniversalTime().ToString("O"), ["updated_utc"] = DateTime.UtcNow.ToString("O"), ["phase"] = phase, ["state"] = result.State, ["detail"] = result.Detail };
                string temporary = Path.Combine(root, "processor-state.tmp"), final = Path.Combine(root, "processor-state.json"); GitFlowGit.Write(temporary, state.ToString(Formatting.Indented));
                if (File.Exists(final)) File.Replace(temporary, final, null); else File.Move(temporary, final);
            }
        }
    }
}
