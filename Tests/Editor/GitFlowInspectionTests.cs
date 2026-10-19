// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Geurts.GameForge.GitFlow.Editor;
using Geurts.GameForge.God;
using Geurts.GameForge.God.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Tests
{
    /// <summary>Disposable real-Git acceptance fixtures for the viewer's material preservation and truthful-state boundaries.</summary>
    public sealed class GitFlowInspectionTests
    {
        private string _root, _checkout, _remote;

        /// <summary>Creates an isolated remote and checkout; no production repository or processor is used.</summary>
        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ggf_test_" + Guid.NewGuid().ToString("N"));
            _checkout = Path.Combine(_root, "checkout"); _remote = Path.Combine(_root, "remote repo.git");
            Directory.CreateDirectory(_root); Directory.CreateDirectory(_checkout);
            Run(_root, "init", "--bare", _remote); Run(_checkout, "init", "-b", "main");
            Run(_checkout, "config", "user.email", "fixture@example.invalid"); Run(_checkout, "config", "user.name", "GitFlow fixture");
            Write("tracked.txt", "base\r\n"); Run(_checkout, "add", "."); Run(_checkout, "commit", "-m", "fixture base");
            Run(_checkout, "remote", "add", "origin", _remote); Run(_checkout, "push", "origin", "main");
        }

        /// <summary>Removes only this fixture's validated temporary root after processes have exited.</summary>
        [TearDown]
        public void TearDown()
        {
            Assert.That(Path.GetFullPath(_root).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "ggf_test_", StringComparison.OrdinalIgnoreCase));
            foreach (string path in Directory.GetFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }

        /// <summary>Exercises all five queue phases, a released lease, matching receipts, shared API/UI identity and operation declarations.</summary>
        [Test]
        public async Task ViewerAndApiUseOneObservationForAllPhases()
        {
            string source = Run(_checkout, "rev-parse", "HEAD").Trim();
            string[] phases = { "queued", "processing", "validating", "integrated", "blocked" };
            for (int i = 0; i < phases.Length; i++) PublishRequest(i, phases[i], source);
            PublishLease("released", 1);
            WriteState(new JObject { ["status"] = "running", ["pid"] = Process.GetCurrentProcess().Id });
            using (var service = new GitFlowInspection())
            {
                GitFlowSnapshot result = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(result.Issues, Is.Empty, string.Join("\n", result.Issues));
                Assert.That(result.QueueState, Is.EqualTo("Observed")); Assert.That(result.LeaseState, Is.EqualTo("Released"));
                CollectionAssert.AreEquivalent(new[] { "Queued", "Processing", "Validating", "Delivered", "Failed" }, result.Requests.Select(value => value.State));
                Assert.That(result.ProcessorState, Is.EqualTo("Unknown")); StringAssert.Contains("Recorded PID exists", result.ProcessorDetail);
                Assert.That(result.Requests.Single(value => value.State == "Delivered").ReceiptJson, Is.Not.Empty);
                Assert.That(result.Requests.Single(value => value.State == "Failed").Failure, Does.Contain("required check failed"));
                var window = ScriptableObject.CreateInstance<GitFlowWindow>();
                try
                {
                    window.CreateGUI(); window.Present(result);
                    Assert.That(window.DisplayedSnapshot, Is.SameAs(result));
                    Assert.That(window.rootVisualElement.Query<Foldout>().ToList().Any(value => value.text == "Help"));
                    Assert.That(window.rootVisualElement.Query<Foldout>().ToList().Any(value => value.text.Contains("Error — Failed")));
                    Assert.That(window.rootVisualElement.ClassListContains("geurts-forge"));
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
                var actions = new GitFlowAutomationProvider().Operations().ToArray();
                Assert.That(actions.Select(value => value.Id), Does.Contain("inspect_queue"));
                Assert.That(actions[0].MutatesProject, Is.False); Assert.That(actions[0].ConfirmationRequired, Is.False);
            }
        }

        /// <summary>Proves repeated refresh preserves exact caller files/index/FETCH_HEAD, remote objects/refs and never invokes a publisher.</summary>
        [Test]
        public async Task RepeatedRefreshPreservesFilesIndexesRefsAndPublisherSentinel()
        {
            PublishRequest(0, "queued", Run(_checkout, "rev-parse", "HEAD").Trim());
            Write("Tools/IntegrationQueue/integration_queue.py", "raise RuntimeError('Publisher must never be invoked')\r\n");
            Write("tracked.txt", "staged\r\n"); Run(_checkout, "add", "tracked.txt"); Write("tracked.txt", "unstaged\r\n"); Write("untracked.txt", "preserve me\r\n");
            Write(".git/FETCH_HEAD", "preserve unrelated fetch state\r\n");
            string files = Fingerprint(_checkout), remote = Fingerprint(_remote);
            string[] before = Directory.GetDirectories(Path.GetTempPath(), "ggf_inspect_*");
            using (var service = new GitFlowInspection())
            {
                var first = service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(service.RefreshAsync(new GitFlowTarget(_checkout)), Is.SameAs(first));
                Assert.Throws<InvalidOperationException>(() => service.RefreshAsync(new GitFlowTarget(_checkout, "other")));
                Assert.That((await first).Complete, Is.True);
                Assert.Throws<InvalidOperationException>(() => service.RefreshAsync(new GitFlowTarget(_checkout)));
                await Task.Delay(2100);
                Assert.That((await service.RefreshAsync(new GitFlowTarget(_checkout))).Complete, Is.True);
                Assert.That(service.LastSnapshot.Requests.Count, Is.EqualTo(1));
            }
            Assert.That(Fingerprint(_checkout), Is.EqualTo(files), "Caller checkout/index/config/ref bytes changed");
            Assert.That(Fingerprint(_remote), Is.EqualTo(remote), "Remote files/refs changed");
            CollectionAssert.AreEquivalent(before, Directory.GetDirectories(Path.GetTempPath(), "ggf_inspect_*"));
        }

        /// <summary>Keeps valid requests visible when malformed control records and unknown leases are present.</summary>
        [Test]
        public async Task MalformedAndFutureRecordsStayActionable()
        {
            string source = Run(_checkout, "rev-parse", "HEAD").Trim(); PublishRequest(0, "queued", source);
            PublishRequest(2, "failed", source);
            Write("IntegrationRequest.json", "{\"schema\":1,\"schema\":2}"); PublishControl("refs/heads/codex/queue/q_20261019_000001_000000000001");
            PublishLease("owned", 2);
            using (var service = new GitFlowInspection())
            {
                var result = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(result.QueueState, Is.EqualTo("Partial")); Assert.That(result.Requests.Count, Is.EqualTo(2));
                Assert.That(result.LeaseState, Is.EqualTo("Unavailable")); Assert.That(result.Issues.Count, Is.EqualTo(3));
                StringAssert.Contains("original exception", result.Requests.Single(value => value.State == "Failed").Failure);
                Assert.That(result.Issues.Count(value => value.Contains("Preserve control data")), Is.EqualTo(2));
            }
        }

        /// <summary>Distinguishes stopped OS process evidence from stale reports and never expires an owned lease by date.</summary>
        [Test]
        public async Task StoppedAndStaleHealthNeverInventLeaseExpiry()
        {
            PublishLease("owned", 1); WriteState(new JObject { ["status"] = "running", ["pid"] = int.MaxValue });
            using (var service = new GitFlowInspection())
            {
                var stopped = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(stopped.ProcessorState, Is.EqualTo("Stopped")); Assert.That(stopped.LeaseState, Is.EqualTo("Owned / health unknown"));
                await Task.Delay(2100); WriteState(new JObject { ["status"] = "running", ["pid"] = Process.GetCurrentProcess().Id });
                File.SetLastWriteTimeUtc(Path.Combine(_checkout, ".git/integration_processor/processor-state.json"), DateTime.UtcNow.AddDays(-1));
                var stale = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(stale.ProcessorState, Is.EqualTo("Stale / health unknown")); Assert.That(stale.LeaseState, Is.EqualTo("Owned / health unknown"));
            }
        }

        /// <summary>Separates malformed local state, genuine retained failure and unavailable remote transport without configuration changes.</summary>
        [Test]
        public async Task FailedUnavailableAndMalformedProcessorEvidenceRemainVisible()
        {
            WriteState(new JObject { ["status"] = "blocked", ["blocker"] = "validation gate failed" });
            using (var service = new GitFlowInspection())
            {
                var failed = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(failed.ProcessorState, Is.EqualTo("Failed (reported)")); StringAssert.Contains("validation gate failed", failed.ProcessorDetail);
                await Task.Delay(2100); Write(".git/integration_processor/processor-state.json", "{broken");
                var malformed = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(malformed.ProcessorState, Is.EqualTo("Unavailable")); Assert.That(malformed.QueueState, Is.EqualTo("Partial"));
                await Task.Delay(2100); Run(_checkout, "config", "remote.origin.url", Path.Combine(_root, "missing.git"));
                var unavailable = await service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(unavailable.QueueState, Is.EqualTo("Unavailable")); Assert.That(unavailable.Issues, Is.Not.Empty);
            }
        }

        /// <summary>Disposed lifetime refuses new work and missing repositories remain unavailable without creating directories.</summary>
        [Test]
        public async Task MissingAndDisposedInspectionCleanUp()
        {
            string missing = Path.Combine(_root, "never-create"); var service = new GitFlowInspection();
            var result = await service.RefreshAsync(new GitFlowTarget(missing));
            Assert.That(result.QueueState, Is.EqualTo("Unavailable")); Assert.That(Directory.Exists(missing), Is.False);
            service.Dispose(); service.Dispose(); Assert.Throws<ObjectDisposedException>(() => service.RefreshAsync(new GitFlowTarget(_checkout)));
        }

        /// <summary>Verifies installed God discovery stays offline and dispatch uses the active shared lifecycle service.</summary>
        [Test]
        public async Task GodDiscoveryAndDispatchMatchImplementedDeclaration()
        {
            string before = Fingerprint(_checkout);
            ForgeOperations.Refresh();
            var actions = ForgeOperations.Discover(GitFlowBrick.PackageId);
            CollectionAssert.AreEquivalent(new[] { "inspect_queue", "submit_request", "apply_policy", "configure_publisher", "start_publisher", "stop_publisher" }, actions.Select(value => value.Id));
            Assert.That(actions.Where(value => value.Id == "inspect_queue").All(value => !value.MutatesProject && !value.ConfirmationRequired));
            Assert.That(actions.Where(value => value.Id != "inspect_queue").All(value => value.MutatesProject && value.ConfirmationRequired));
            Assert.That(GitFlowBrick.Service, Is.Not.Null, "God did not start the installed Editor-only brick.");
            var request = new ForgeOperationRequest(GitFlowBrick.PackageId, "inspect_queue", payload: new GitFlowTarget(_checkout));
            Assert.That(ForgeOperations.Inspect(request).Status, Is.EqualTo(FORGE_OPERATION_STATUS.SUCCEEDED));
            Assert.That(Fingerprint(_checkout), Is.EqualTo(before), "Discovery/admission changed the inspected repository");
            var result = await ForgeOperations.ExecuteAsync(request);
            Assert.That(result.Status, Is.EqualTo(FORGE_OPERATION_STATUS.SUCCEEDED), result.Detail);
            Assert.That(GitFlowBrick.Service.LastSnapshot.Repository, Is.EqualTo(_checkout));
            Assert.That(Fingerprint(_checkout), Is.EqualTo(before));
        }

        /// <summary>Authors policy through hidden controls, compares the public API, and submits through real God dispatch without launching a host.</summary>
        [Test]
        public async Task NativePolicyControlsSubmissionAndViewerShareServices()
        {
            Write("VERSION", "1.0.0\r\n"); Write("Tools/Validation/required.txt", "protected gate input\r\n");
            string executable = Environment.GetEnvironmentVariable("PATH").Split(';').Select(directory => Path.Combine(directory.Trim('"'), "git.exe")).First(File.Exists);
            var window = ScriptableObject.CreateInstance<GitFlowWindow>();
            try
            {
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Query<Tab>().ToList().Select(tab => tab.label), Is.EquivalentTo(new[] { "Inspect", "Submit", "Publisher" }));
                var fields = window.rootVisualElement.Query<TextField>().ToList();
                fields.Single(field => field.label == "Repository").value = _checkout;
                fields.Single(field => field.label == "Native console executable").value = executable;
                fields.Single(field => field.label == "Literal arguments (one per line)").value = "--version";
                window.rootVisualElement.Query<Toggle>().ToList().Single(toggle => toggle.label?.StartsWith("Every required command", StringComparison.Ordinal) == true).value = true;
                string before = Fingerprint(_checkout);
                GitFlowPolicyReview controls = await window.ReviewPolicyControlsAsync();
                GitFlowPolicyReview api = await GitFlowBrick.ConfigurationService.ReviewPolicyAsync(_checkout, "VERSION", new[] { "Tools/Validation" }, new[] { new GitFlowGate { Executable = executable, Arguments = new[] { "--version" }, TimeoutSeconds = 300 } });
                Assert.That(controls.PolicyJson, Is.EqualTo(api.PolicyJson)); Assert.That(Fingerprint(_checkout), Is.EqualTo(before));
                var denied = await ForgeOperations.ExecuteAsync(new ForgeOperationRequest(GitFlowBrick.PackageId, "apply_policy", payload: controls));
                Assert.That(denied.Status, Is.EqualTo(FORGE_OPERATION_STATUS.BLOCKED)); Assert.That(Fingerprint(_checkout), Is.EqualTo(before));
                var applied = await ForgeOperations.ExecuteAsync(new ForgeOperationRequest(GitFlowBrick.PackageId, "apply_policy", arguments: new System.Collections.Generic.Dictionary<string, string> { ["confirmed"] = "true" }, payload: controls));
                Assert.That(applied.Status, Is.EqualTo(FORGE_OPERATION_STATUS.SUCCEEDED), applied.Detail);
                Run(_checkout, "add", "."); Run(_checkout, "commit", "-m", "fixture committed generic policy"); Run(_checkout, "push", "origin", "main");
                string integratedBase = Run(_checkout, "rev-parse", "HEAD").Trim(); Write("tracked.txt", "native source\r\n"); Run(_checkout, "add", "tracked.txt"); Run(_checkout, "commit", "-m", "fixture native source");
                string source = Run(_checkout, "rev-parse", "HEAD").Trim(); before = Fingerprint(_checkout);
                var payload = new GitFlowSubmission(_checkout, source, integratedBase, "patch", "native API handoff", "Scope: fixture. Dependencies: exact base. Evidence/environment: owning Unity acceptance. Native rendered UI unverified; human review next.");
                var submitted = await ForgeOperations.ExecuteAsync(new ForgeOperationRequest(GitFlowBrick.PackageId, "submit_request", arguments: new System.Collections.Generic.Dictionary<string, string> { ["confirmed"] = "true" }, payload: payload));
                Assert.That(submitted.Status, Is.EqualTo(FORGE_OPERATION_STATUS.SUCCEEDED), submitted.Detail); StringAssert.Contains("gf_" + source, submitted.Detail);
                Assert.That(Fingerprint(_checkout), Is.EqualTo(before)); Assert.That(Directory.Exists(Path.Combine(_root, "publisher")), Is.False);
                using (var inspection = new GitFlowInspection())
                {
                    GitFlowSnapshot observation = await inspection.RefreshAsync(new GitFlowTarget(_checkout)); window.Present(observation);
                    Assert.That(observation.QueueState, Is.EqualTo("Observed"), string.Join("\n", observation.Issues)); Assert.That(window.DisplayedSnapshot, Is.SameAs(observation));
                    Assert.That(observation.Requests.Single().Id, Is.EqualTo("gf_" + source)); Assert.That(observation.Requests.Single().State, Is.EqualTo("Queued"));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        /// <summary>Preserves stale/malformed policy and God drafts; explicit settings saves retain the brick enablement.</summary>
        [Test]
        public async Task ConfigurationReviewsAndSavedDraftsPreserveUnknownData()
        {
            Write("VERSION", "1.0.0\r\n");
            string executable = Environment.GetEnvironmentVariable("PATH").Split(';').Select(directory => Path.Combine(directory.Trim('"'), "git.exe")).First(File.Exists);
            var configuration = new GitFlowConfiguration();
            GitFlowPolicyReview review = await configuration.ReviewPolicyAsync(_checkout, "VERSION", new[] { "Tools/Validation" }, new[] { new GitFlowGate { Executable = executable, Arguments = new[] { "--version" } } });
            Write(".geurts-gitflow.json", "{\"schema\":999}"); string before = Fingerprint(_checkout);
            Assert.Throws<IOException>(() => configuration.ApplyPolicy(review)); Assert.That(Fingerprint(_checkout), Is.EqualTo(before));
            var store = new BrickSettingsStore(Path.Combine(_root, "settings"));
            store.Save(GitFlowBrick.PackageId, new GitFlowPreferences { publisherRoot = "C:\\fixture_only", gates = new[] { new GitFlowGatePreference { executable = executable } } });
            var saved = store.Load<GitFlowPreferences>(GitFlowBrick.PackageId); Assert.That(saved.publisherRoot, Is.EqualTo("C:\\fixture_only")); Assert.That(saved.gates[0].executable, Is.EqualTo(executable)); Assert.That(store.Read(GitFlowBrick.PackageId).enabled, Is.True);
            string file = Path.Combine(store.Root, GitFlowBrick.PackageId + ".json"); File.WriteAllText(file, "{\"id\":\"" + GitFlowBrick.PackageId + "\",\"enabled\":true,\"json\":\"{\\\"schema\\\":999}\"}");
            byte[] malformed = File.ReadAllBytes(file); Assert.Throws<InvalidDataException>(() => store.Load<GitFlowPreferences>(GitFlowBrick.PackageId));
            Assert.Throws<InvalidDataException>(() => store.Save(GitFlowBrick.PackageId, new GitFlowPreferences())); Assert.That(File.ReadAllBytes(file), Is.EqualTo(malformed));
        }

        /// <summary>Verifies native receipt/tree/stream identities and released leases without any remote or caller mutation.</summary>
        [Test]
        public async Task NativeReceiptHashesAndReleasedLeaseRemainReadOnly()
        {
            string source = Run(_checkout, "rev-parse", "HEAD").Trim(), id = "gf_" + source;
            string store = Path.Combine(_root, "native-store"); Directory.CreateDirectory(store);
            var git = new GitFlowGit(store, _remote); git.Run(default, "init", "--bare", "--quiet"); git.Fetch(source, default);
            string output = "native required check evidence";
            var receipt = new JObject { ["schema"] = 1, ["id"] = id, ["source"] = source, ["version"] = "1.0.1", ["candidate"] = source, ["tree"] = Run(_checkout, "rev-parse", "HEAD^{tree}").Trim(),
                ["validation"] = new JObject { ["checks"] = new JArray(new JObject { ["exit_code"] = 0, ["stdout"] = output, ["stderr"] = "", ["stdout_sha256"] = GitFlowGit.Hash(Encoding.UTF8.GetBytes(output)), ["stderr_sha256"] = GitFlowGit.Hash(Array.Empty<byte>()) }) } };
            var record = new JObject { ["schema"] = 1, ["kind"] = "request", ["adapter"] = "generic_git", ["id"] = id, ["source"] = source, ["base"] = source, ["dependencies"] = new JArray(), ["title"] = "native receipt fixture", ["status"] = "delivered", ["main_commit"] = source, ["version"] = "1.0.1", ["receipt"] = receipt };
            string control = git.Commit(record, new[] { source }, default); git.Run(default, "push", _remote, control + ":" + GitFlowGit.RequestPrefix + id);
            var lease = new JObject { ["schema"] = 1, ["kind"] = "lease", ["lease_protocol"] = 1, ["owner_seconds"] = 180, ["renewal_seconds"] = 20, ["transport_seconds"] = 15, ["write_reserve_seconds"] = 120, ["owner"] = new string('a', 32), ["state"] = "released" };
            string leaseCommit = git.Commit(lease, Array.Empty<string>(), default); git.Run(default, "push", _remote, leaseCommit + ":" + GitFlowGit.LeaseRef);
            string before = Fingerprint(_checkout), remote = Fingerprint(_remote);
            using (var inspection = new GitFlowInspection())
            {
                GitFlowSnapshot observation = await inspection.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(observation.QueueState, Is.EqualTo("Observed"), string.Join("\n", observation.Issues)); Assert.That(observation.LeaseState, Is.EqualTo("Released"));
                StringAssert.Contains("retained stream hashes match", observation.Requests.Single().ReceiptDetail);
                Assert.That(observation.Requests.Single().EvidenceJson, Is.EqualTo(git.Run(default, "show", control + ":GitFlowRecord.json")));
                Assert.That(Fingerprint(_checkout), Is.EqualTo(before)); Assert.That(Fingerprint(_remote), Is.EqualTo(remote));
            }
            record["receipt"]["validation"]["checks"][0]["stdout_sha256"] = new string('0', 64);
            string tampered = git.Commit(record, new[] { control }, default); git.Run(default, "push", _remote, tampered + ":" + GitFlowGit.RequestPrefix + id);
            using (var inspection = new GitFlowInspection())
            { GitFlowSnapshot observation = await inspection.RefreshAsync(new GitFlowTarget(_checkout)); Assert.That(observation.QueueState, Is.EqualTo("Partial")); Assert.That(observation.Issues.Any(issue => issue.Contains("hash mismatch")), Is.True); }
        }

        /// <summary>Exercises a real stalled HTTPS helper from the detached batch parent and proves disposal closes its connection within five seconds.</summary>
        [Test]
        public async Task CancellationContainsStalledHttpsHelperAndCleansObjects()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            string[] before = Directory.GetDirectories(Path.GetTempPath(), "ggf_inspect_*");
            var service = new GitFlowInspection();
            try
            {
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                Run(_checkout, "config", "remote.origin.url", "https://127.0.0.1:" + port + "/fixture.git");
                Task<TcpClient> accepted = listener.AcceptTcpClientAsync();
                Task<GitFlowSnapshot> operation = service.RefreshAsync(new GitFlowTarget(_checkout));
                Assert.That(await Task.WhenAny(accepted, Task.Delay(10000)), Is.SameAs(accepted), "Git did not create its HTTPS helper connection");
                using (TcpClient connection = await accepted)
                {
                    service.Dispose();
                    Assert.That(await Task.WhenAny(operation, Task.Delay(5000)), Is.SameAs(operation), "Cancelled helper exceeded cleanup bound");
                    GitFlowSnapshot result = await operation;
                    Assert.That(result.QueueState, Is.EqualTo("Unavailable")); Assert.That(result.Issues.Any(value => value.Contains("cancelled")));
                    connection.ReceiveTimeout = 2000; var bytes = new byte[8192];
                    bool closed = false;
                    try { for (int i = 0; i < 8; i++) if (connection.GetStream().Read(bytes, 0, bytes.Length) == 0) { closed = true; break; } }
                    catch (IOException error) { closed = error.InnerException is SocketException socket && socket.SocketErrorCode == SocketError.ConnectionReset; }
                    Assert.That(closed, Is.True, "A Git/helper descendant kept the HTTPS connection alive after cancellation");
                }
            }
            finally { service.Dispose(); listener.Stop(); }
            CollectionAssert.AreEquivalent(before, Directory.GetDirectories(Path.GetTempPath(), "ggf_inspect_*"));
        }

        /// <summary>Creates exact protocol request metadata and a source/version-matched receipt for the delivered case.</summary>
        private void PublishRequest(int index, string status, string source)
        {
            string id = "q_20261019_00000" + index + "_" + index.ToString("x12");
            var request = new JObject { ["schema"] = 1, ["id"] = id, ["status"] = status, ["title"] = "Fixture " + status,
                ["source"] = source, ["size"] = "small", ["version"] = "0.1.0", ["validation"] = new JArray("actual fixture claim"),
                ["blocker"] = status == "blocked" ? "required check failed; inspect fixture.log" : "" };
            if (status == "integrated")
            {
                Write("Tools/IntegrationQueue/Receipts/" + id + ".json", new JObject { ["schema"] = 1, ["id"] = id, ["source"] = source, ["version"] = "0.1.0", ["validation"] = "fixture" }.ToString());
                Run(_checkout, "add", "."); Run(_checkout, "commit", "-m", "fixture receipt");
                request["main_commit"] = Run(_checkout, "rev-parse", "HEAD").Trim(); Run(_checkout, "push", "origin", "main");
            }
            Write("IntegrationRequest.json", request.ToString()); PublishControl("refs/heads/codex/queue/" + id);
        }

        /// <summary>Publishes a lease record whose deliberately old date never serves as an expiry signal.</summary>
        private void PublishLease(string state, int protocol)
        {
            Write("IntegrationRequest.json", new JObject { ["lease_protocol"] = protocol, ["lease_seconds"] = 1800,
                ["state"] = state, ["owner"] = new string('a', 32), ["host"] = "fixture", ["updated_utc"] = "2000-01-01T00:00:00Z" }.ToString());
            PublishControl("refs/heads/codex/integration-lock");
        }

        /// <summary>Commits control data only inside the disposable fixture and publishes a normal non-force ref.</summary>
        private void PublishControl(string reference)
        { Run(_checkout, "add", "IntegrationRequest.json"); Run(_checkout, "commit", "-m", "fixture control"); Run(_checkout, "push", "origin", "HEAD:" + reference); }

        /// <summary>Writes local processor evidence without starting a processor.</summary>
        private void WriteState(JObject value) => Write(".git/integration_processor/processor-state.json", value.ToString());

        /// <summary>Writes one known disposable fixture path, creating only its missing parent.</summary>
        private void Write(string relative, string value)
        { string path = Path.Combine(_checkout, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, value, new UTF8Encoding(false)); }

        /// <summary>Hashes every file path and raw byte so preservation covers index, refs, settings and untracked content.</summary>
        private static string Fingerprint(string directory)
        {
            using (var hash = SHA256.Create()) return string.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories).OrderBy(value => value, StringComparer.Ordinal)
                .Select(path => path.Substring(directory.Length) + ":" + File.GetAttributes(path) + ":" + Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)))));
        }

        /// <summary>Runs bounded real Git only inside fixtures with console and credential prompts suppressed.</summary>
        private static string Run(string directory, params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, Arguments = string.Join(" ", arguments.Select(value => "\"" + value.Replace("\\", "/").Replace("\"", "\\\"") + "\"")) };
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15))) return GitFlowProcess.Run(start, budget.Token);
        }
    }
}
