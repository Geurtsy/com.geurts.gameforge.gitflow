// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Read-only protocol-1 queue adapter. It executes only fixed Git reads and fetches into an owned temporary bare repository.</summary>
    /// <remarks>Help: Documentation~/AIUsability.md. No repository scripts, publishers, lock probes or configuration writes are supported.</remarks>
    public sealed class GitFlowInspection : IDisposable
    {
        public const int MaximumRequests = 128;
        public const int MaximumJsonBytes = 1024 * 1024;
        public const int RefreshBudgetSeconds = 45;
        private const string QueuePrefix = "refs/heads/codex/queue/";
        private const string LeaseRef = "refs/heads/codex/integration-lock";
        private readonly object _gate = new object();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private Task<GitFlowSnapshot> _pending;
        private string _pendingKey;
        private DateTime _nextRefresh;
        private bool _disposed;
        public GitFlowSnapshot LastSnapshot { get; private set; }
        public bool Busy { get { lock (_gate) return _pending != null && !_pending.IsCompleted; } }

        /// <summary>Starts one bounded refresh; identical overlapping callers share work, and subsequent requests have a two-second cooldown.</summary>
        /// <param name="target">Existing local checkout and remote; neither is changed.</param>
        /// <returns>An immutable observation, including unavailable/partial evidence and cleanup failures.</returns>
        public Task<GitFlowSnapshot> RefreshAsync(GitFlowTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            string key = target.Repository + "\n" + target.Remote;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(GitFlowInspection));
                if (_pending != null && !_pending.IsCompleted)
                {
                    if (_pendingKey == key) return _pending;
                    throw new InvalidOperationException("An inspection is already running. Wait for its bounded result before selecting another repository.");
                }
                if (DateTime.UtcNow < _nextRefresh) throw new InvalidOperationException("Refresh cooldown: wait two seconds between completed inspections.");
                _pendingKey = key;
                _pending = Task.Run(() => Read(target), _lifetime.Token);
                return _pending;
            }
        }

        /// <summary>Reads independent processor and remote evidence, then removes only this inspection's temporary object store.</summary>
        private GitFlowSnapshot Read(GitFlowTarget target)
        {
            DateTime started = DateTime.UtcNow;
            var rows = new List<GitFlowRequest>();
            var issues = new List<string>();
            string queue = "Unavailable", processor = "Unknown", processorDetail = "Not inspected.", lease = "Unknown", leaseDetail = "Not inspected.";
            string temporary = null;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(RefreshBudgetSeconds));
                try
                {
                    string repository = Path.GetFullPath(target.Repository ?? "");
                    CheckPath(repository);
                    if (!Directory.Exists(repository)) throw new IOException("Repository is unavailable. Select an existing Git checkout.");
                    if (!Regex.IsMatch(target.Remote ?? "", "^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,63}$")) throw new ArgumentException("Select an existing remote name, for example origin.");
                    string common = Git(repository, budget.Token, "rev-parse", "--path-format=absolute", "--git-common-dir").Trim();
                    try { ReadProcessor(common, out processor, out processorDetail); }
                    catch (Exception error) { processor = "Unavailable"; processorDetail = Detail(error); issues.Add("Processor: " + processorDetail); }
                    string remote = Git(repository, budget.Token, "config", "--get", "remote." + target.Remote + ".url").Trim();
                    // Inspection deliberately supports no external remote helpers or interactive authentication.
                    if (!(remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || Path.IsPathRooted(remote)))
                        throw new InvalidOperationException("Inspection supports HTTPS and absolute local remotes. SSH/custom helpers are unavailable in this read-only release.");
                    if (remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && (!Uri.TryCreate(remote, UriKind.Absolute, out var address) || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0))
                        throw new InvalidOperationException("Embedded remote credentials are unsupported. Remove secrets through the repository owner's workflow.");
                    temporary = Path.Combine(Path.GetTempPath(), "ggf_inspect_" + Guid.NewGuid().ToString("N"));
                    CheckPath(temporary + "/objects/pack/pack-" + new string('a', 40) + ".pack");
                    Directory.CreateDirectory(temporary);
                    Git(temporary, budget.Token, "init", "--bare", "--quiet");
                    string advertised = Git(temporary, budget.Token, "ls-remote", "--heads", remote, QueuePrefix + "*", LeaseRef);
                    var refs = ParseRefs(advertised);
                    if (refs.Keys.Count(value => value != LeaseRef) > MaximumRequests) throw new InvalidOperationException("Queue exceeds the 128-request inspection bound. Ask the queue owner to provide a paged adapter; no partial queue is labelled complete.");
                    if (refs.Count > 0)
                    {
                        var fetch = new List<string> { "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", "--depth=1", remote };
                        fetch.AddRange(refs.Values.Distinct());
                        Git(temporary, budget.Token, fetch.ToArray());
                    }
                    queue = "Observed";
                    lease = "Absent"; leaseDetail = "No lease ref was advertised during this refresh. Absence does not authorize acquisition.";
                    foreach (var pair in refs)
                    {
                        try
                        {
                            if (Git(temporary, budget.Token, "cat-file", "-t", pair.Value).Trim() != "commit") throw new InvalidDataException("Control object is not a commit.");
                            string raw = Git(temporary, budget.Token, "show", pair.Value + ":IntegrationRequest.json");
                            JObject data = ParseObject(raw);
                            if (pair.Key == LeaseRef) { ReadLease(data, pair.Value, out lease, out leaseDetail); continue; }
                            string id = Text(data, "id");
                            if (data["schema"]?.Type != JTokenType.Integer || (int?)data["schema"] != 1 || !Regex.IsMatch(id, "^q_[0-9]{8}_[0-9]{6}_[0-9a-f]{12}$") || pair.Key != QueuePrefix + id)
                                throw new InvalidDataException("Unknown schema or request/ref identity mismatch.");
                            string state = State(Text(data, "status"));
                            string source = Text(data, "source");
                            if (!Regex.IsMatch(source, "^[0-9a-f]{40}$")) throw new InvalidDataException("Submitted source must be an exact commit identity.");
                            string receipt = "", receiptDetail = "Not published; no receipt inspected.";
                            string published = Text(data, "main_commit");
                            if (state == "Delivered")
                            {
                                if (!Regex.IsMatch(published, "^[0-9a-f]{40}$")) throw new InvalidDataException("Delivered record lacks an exact published commit.");
                                try
                                {
                                    Git(temporary, budget.Token, "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", "--depth=1", remote, published);
                                    receipt = Git(temporary, budget.Token, "show", published + ":Tools/IntegrationQueue/Receipts/" + id + ".json");
                                    JObject proof = ParseObject(receipt);
                                    if ((int?)proof["schema"] != 1 || Text(proof, "id") != id || Text(proof, "source") != source || Text(proof, "version") != Text(data, "version"))
                                        throw new InvalidDataException("Receipt identity/source/version does not match the request.");
                                    receiptDetail = "Receipt identity/source/version matched at recorded commit. Validation claims and current-main ancestry remain unverified.";
                                }
                                catch (Exception error) { receiptDetail = "Unavailable: " + Detail(error); issues.Add(id + " receipt: " + receiptDetail); }
                            }
                            if (state == "Unknown") issues.Add(id + ": unsupported status '" + Text(data, "status") + "'. Ask the owning queue adapter to declare its meaning.");
                            rows.Add(new GitFlowRequest(id, state, Text(data, "title"), source, Text(data, "size"), Text(data, "version"), published,
                                Text(data, "blocker"), pair.Value, raw, receipt, receiptDetail));
                        }
                        catch (Exception error)
                        {
                            string detail = pair.Key + ": " + Detail(error) + " Preserve control data and ask its owning operator to repair it.";
                            issues.Add(detail);
                            if (pair.Key == LeaseRef) { lease = "Unavailable"; leaseDetail = detail; }
                        }
                    }
                    if (issues.Count > 0) queue = "Partial";
                }
                catch (Exception error) { issues.Add(Detail(error) + " Correct the reported access/input problem, then refresh explicitly."); }
                finally
                {
                    if (temporary != null)
                    {
                        try { CheckPath(temporary); Directory.Delete(temporary, true); }
                        catch (Exception error) { issues.Add("Temporary inspection cleanup failed at " + temporary + ": " + Detail(error)); queue = "Partial"; }
                    }
                }
            }
            var snapshot = new GitFlowSnapshot(target.Repository, target.Remote, started, queue, processor, processorDetail, lease, leaseDetail, rows.OrderBy(value => value.Id, StringComparer.Ordinal), issues);
            lock (_gate) { LastSnapshot = snapshot; _nextRefresh = DateTime.UtcNow.AddSeconds(2); }
            return snapshot;
        }

        /// <summary>Reads local status without creating the state directory or probing its ownership lock.</summary>
        private static void ReadProcessor(string common, out string state, out string detail)
        {
            string directory = Path.Combine(common, "integration_processor");
            string path = Path.Combine(directory, "processor-state.json");
            CheckPath(path);
            if (!Directory.Exists(directory)) { state = "Unconfigured"; detail = "No local processor directory. Other machines and actual ownership are unknown."; return; }
            if (!File.Exists(path)) { state = "Unknown"; detail = "Local processor directory exists without state evidence. Inspect the configured owner's logs."; return; }
            JObject data = ParseObject(ReadFile(path));
            string reported = Text(data, "status");
            int? pid = (int?)data["pid"];
            string presence = "No PID supplied; OS process presence unknown.";
            state = "Unknown";
            if (pid.HasValue && pid > 0)
            {
                try
                {
                    using (var process = Process.GetProcessById(pid.Value))
                        presence = process.HasExited ? "Recorded PID has exited." : "Recorded PID exists. PID reuse and lock ownership are unverified.";
                }
                catch (ArgumentException) { presence = "Recorded PID is absent; the recorded process is stopped."; state = "Stopped"; }
            }
            if (state == "Unknown" && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromMinutes(2)) state = "Stale / health unknown";
            if (reported == "blocked" || reported == "failed") state = "Failed (reported)";
            detail = "Reported phase: " + reported + ". " + presence + " State file timestamp: " + File.GetLastWriteTimeUtc(path).ToString("O") + ". No lock acquired.\n" + data.ToString(Formatting.Indented);
        }

        /// <summary>Validates known lease metadata without deriving expiry or takeover permission from timestamps.</summary>
        private static void ReadLease(JObject data, string commit, out string state, out string detail)
        {
            if (data["lease_protocol"] == null) { state = "Legacy / unknown"; detail = "Legacy lock " + commit + "; confirmed-stopped owner migration is required. No expiry inferred."; return; }
            if (data["lease_protocol"]?.Type != JTokenType.Integer || data["lease_seconds"]?.Type != JTokenType.Integer || (int?)data["lease_protocol"] != 1 || !new[] { 180, 1800 }.Contains((int?)data["lease_seconds"] ?? 0) ||
                !new[] { "owned", "released" }.Contains(Text(data, "state")) || !Regex.IsMatch(Text(data, "owner"), "^[0-9a-f]{32}$") ||
                string.IsNullOrWhiteSpace(Text(data, "host")) || !DateTimeOffset.TryParse(Text(data, "updated_utc"), out _))
                throw new InvalidDataException("Malformed or unsupported lease protocol/timing. Upgrade the supported adapter or ask its owner to inspect preserved evidence.");
            state = Text(data, "state") == "released" ? "Released" : "Owned / health unknown";
            detail = "Observed commit " + commit + ". Timestamps are audit metadata; renewal health/expiry are unknown from one observation.\n" + data.ToString(Formatting.Indented);
        }

        /// <summary>Accepts only expected queue refs with exact SHA-1 commit identifiers.</summary>
        private static Dictionary<string, string> ParseRefs(string output)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string row in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = row.Split('\t');
                if (parts.Length != 2 || !Regex.IsMatch(parts[0], "^[0-9a-f]{40}$") ||
                    !(parts[1] == LeaseRef || parts[1].StartsWith(QueuePrefix, StringComparison.Ordinal)) || result.ContainsKey(parts[1]))
                    throw new InvalidDataException("Malformed or duplicated advertised queue reference.");
                result.Add(parts[1], parts[0]);
            }
            return result;
        }

        /// <summary>Runs a fixed Git argument vector with bounded output, no shell, no console and no credential helpers.</summary>
        private static string Git(string directory, CancellationToken cancellation, params string[] arguments)
        {
            cancellation.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            // Unity's baseline lacks ProcessStartInfo.ArgumentList. Quote each fixed vector element for Windows CRT parsing.
            string[] prefix = { "--no-optional-locks", "-c", "credential.helper=", "-c", "credential.interactive=never", "-c", "core.hooksPath=", "-c", "protocol.ext.allow=never", "-c", "protocol.ssh.allow=never", "-c", "gc.auto=0" };
            start.Arguments = string.Join(" ", prefix.Concat(arguments).Select(Quote));
            foreach (string name in start.EnvironmentVariables.Keys.Cast<string>().Where(value => value.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.EnvironmentVariables.Remove(name);
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            start.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0"; start.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"] = "1";
            start.EnvironmentVariables["GIT_CONFIG_GLOBAL"] = "NUL"; start.EnvironmentVariables["GIT_LFS_SKIP_SMUDGE"] = "1";
            try { return GitFlowProcess.Run(start, cancellation); }
            catch (IOException error) { throw new IOException("Git " + arguments[0] + ": " + error.Message, error); }
        }

        /// <summary>Quotes a single Windows process argument without shell evaluation.</summary>
        private static string Quote(string value)
        {
            var quoted = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value ?? "")
            {
                if (character == '\\') { slashes++; continue; }
                if (character == '"') quoted.Append('\\', slashes * 2 + 1).Append('"');
                else quoted.Append('\\', slashes).Append(character);
                slashes = 0;
            }
            return quoted.Append('\\', slashes * 2).Append('"').ToString();
        }

        /// <summary>Reads local metadata through a shared read handle without locking or rewriting it.</summary>
        private static string ReadFile(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            { if (file.Length > MaximumJsonBytes) throw new InvalidDataException("Processor metadata exceeds one MiB."); using (var reader = new StreamReader(file, Encoding.UTF8, true)) return reader.ReadToEnd(); }
        }

        /// <summary>Rejects malformed, duplicated, oversized and deeply nested control objects.</summary>
        private static JObject ParseObject(string raw)
        {
            if (Encoding.UTF8.GetByteCount(raw) > MaximumJsonBytes) throw new InvalidDataException("JSON exceeds one MiB.");
            using (var reader = new JsonTextReader(new StringReader(raw)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException("Trailing JSON data.");
                return value;
            }
        }

        /// <summary>Reads a string field while preserving absent values as explicitly unknown.</summary>
        private static string Text(JObject data, string name) => data[name]?.Type == JTokenType.String ? (string)data[name] : "";

        /// <summary>Maps only explicitly supported protocol states and preserves unsupported phases as unknown.</summary>
        private static string State(string value)
        {
            switch (value) { case "queued": return "Queued"; case "processing": return "Processing"; case "validating": return "Validating";
                case "integrated": case "delivered": return "Delivered"; case "blocked": case "failed": return "Failed"; default: return "Unknown"; }
        }

        /// <summary>Checks the resolved path and every existing ancestor before inspection or temporary creation.</summary>
        private static void CheckPath(string path)
        {
            string full = Path.GetFullPath(path);
            if (full.Length >= 260) throw new PathTooLongException(full + " (" + full.Length + " characters); select a shorter path.");
            for (string current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Inspection does not traverse reparse paths: " + current);
        }

        /// <summary>Retains useful exception type and remedy context; cancellation never becomes a successful observation.</summary>
        private static string Detail(Exception error) => error is OperationCanceledException ? "Inspection cancelled or exceeded its 45-second budget." : error.GetType().Name + ": " + error.Message;

        /// <summary>Cancels owned inspection without touching the observed repository, processor or caller settings.</summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return; _disposed = true; _lifetime.Cancel();
                if (_pending == null || _pending.IsCompleted) _lifetime.Dispose();
                else _pending.ContinueWith(_ => _lifetime.Dispose(), TaskScheduler.Default);
            }
        }
    }
}
