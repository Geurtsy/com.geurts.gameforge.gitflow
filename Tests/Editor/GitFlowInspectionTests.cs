// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
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
using Geurts.GameForge.God.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Geurts.GameForge.God;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
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
                Assert.That(actions.Select(value => value.Id), Is.EqualTo(new[] { "inspect_queue" }));
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
            CollectionAssert.AreEqual(new[] { "inspect_queue" }, actions.Select(value => value.Id));
            Assert.That(actions.All(value => !value.MutatesProject && !value.ConfirmationRequired));
            Assert.That(GitFlowBrick.Service, Is.Not.Null, "God did not start the installed Editor-only brick.");
            var request = new ForgeOperationRequest(GitFlowBrick.PackageId, "inspect_queue", payload: new GitFlowTarget(_checkout));
            Assert.That(ForgeOperations.Inspect(request).Status, Is.EqualTo(FORGE_OPERATION_STATUS.SUCCEEDED));
            Assert.That(Fingerprint(_checkout), Is.EqualTo(before), "Discovery/admission changed the inspected repository");
            var result = await ForgeOperations.ExecuteAsync(request);
            Assert.That(result.Status, Is.EqualTo(FORGE_OPERATION_STATUS.SUCCEEDED), result.Detail);
            Assert.That(GitFlowBrick.Service.LastSnapshot.Repository, Is.EqualTo(_checkout));
            Assert.That(Fingerprint(_checkout), Is.EqualTo(before));
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

        /// <summary>Captures an actual native saved Inspector change through the same installed host used by the button.</summary>
        [Test]
        public async Task NativeSavedInspectorChangeQueuesAndPreservesWorkingState()
        {
            Assert.That(Application.isBatchMode, Is.True, "This native-save acceptance belongs only in a disposable batch project.");
            string python = Environment.GetEnvironmentVariable("GEURTS_GITFLOW_TEST_PYTHON");
            Assert.That(File.Exists(python), Is.True, "Acceptance requires an explicit installed Python executable.");
            string folder = "Assets/GitFlowCaptureAcceptance_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            string scenePath = folder + "/Enemy.unity";
            // The disposable batch runner begins with an untitled empty scene; preserve it through Unity's native API.
            Scene initial = SceneManager.GetActiveScene();
            bool savedInitial = string.IsNullOrEmpty(initial.path);
            if (savedInitial) Assert.That(EditorSceneManager.SaveScene(initial, folder + "/Initial.unity"), Is.True);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var enemy = new GameObject("Enemy"); SceneManager.MoveGameObjectToScene(enemy, scene);
                enemy.transform.localPosition = new Vector3(3, 0, 0);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport);
                Directory.CreateDirectory(Path.Combine(_checkout, "Assets"));
                File.Copy(scenePath, Path.Combine(_checkout, "Assets/Enemy.unity"));
                File.Copy(scenePath + ".meta", Path.Combine(_checkout, "Assets/Enemy.unity.meta"));
                Run(_checkout, "add", "Assets"); Run(_checkout, "commit", "-m", "Native saved scene baseline");
                string head = Run(_checkout, "rev-parse", "HEAD").Trim();
                var inspector = new SerializedObject(enemy.transform);
                inspector.FindProperty("m_LocalPosition").vector3Value = new Vector3(4, 0, 0);
                inspector.ApplyModifiedProperties(); EditorSceneManager.MarkSceneDirty(scene);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                var owned = new Material(Shader.Find("Hidden/InternalErrorShader"));
                var excluded = new Material(Shader.Find("Hidden/InternalErrorShader"));
                AssetDatabase.CreateFolder(folder, "Plugins");
                AssetDatabase.CreateAsset(owned, folder + "/Owned.mat");
                AssetDatabase.CreateAsset(excluded, folder + "/Plugins/Excluded.mat");
                owned.name = "Owned change"; excluded.name = "Preserved unrelated change";
                EditorUtility.SetDirty(owned); EditorUtility.SetDirty(excluded);
                Assert.That(EditorUtility.IsDirty(excluded), Is.True, "Fixture must begin with an unsaved excluded asset.");
                GitFlowControls.SaveActiveProject(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), folder);
                Assert.That(EditorUtility.IsDirty(owned), Is.False, "Included asset was not saved through its native API.");
                Assert.That(EditorUtility.IsDirty(excluded), Is.True, "Excluded unsaved asset state was changed.");
                File.Copy(scenePath, Path.Combine(_checkout, "Assets/Enemy.unity"), true);
                byte[] saved = File.ReadAllBytes(Path.Combine(_checkout, "Assets/Enemy.unity"));
                byte[] index = File.ReadAllBytes(Path.Combine(_checkout, ".git/index"));
                using (var host = new GitFlowHost(new BrickSettingsStore(Path.Combine(_root, "settings"))))
                {
                    var preferences = new GitFlowPreferences { PythonExecutable = python, Repository = _checkout, Remote = "origin", CaptureScope = "Assets" };
                    JObject result = await host.ExecuteQueueAsync(preferences, new[] { "queue-local-changes", "--scope", "Assets", "--title", "Move enemy from 3 to 4" }, 120);
                    Assert.That((string)result["state"], Is.EqualTo("queued"));
                    Assert.That(Run(_checkout, "show", (string)result["source"] + ":Assets/Enemy.unity"), Does.Contain("x: 4"));
                    Assert.That(Run(_checkout, "rev-parse", "HEAD").Trim(), Is.EqualTo(head));
                    CollectionAssert.AreEqual(index, File.ReadAllBytes(Path.Combine(_checkout, ".git/index")));
                    CollectionAssert.AreEqual(saved, File.ReadAllBytes(Path.Combine(_checkout, "Assets/Enemy.unity")));
                }
                enemy.transform.localPosition = new Vector3(5, 0, 0); EditorSceneManager.MarkSceneDirty(scene);
                Assert.Throws<InvalidOperationException>(() => GitFlowControls.SaveActiveProject(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), folder));
                Assert.That(scene.isDirty, Is.True); Assert.That(EditorUtility.IsDirty(excluded), Is.True);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (savedInitial) EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
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
