// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Geurts.GameForge.GitFlow.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Runner
{
    /// <summary>Disposable real-Git integration acceptance for cross-process publication, immutable handoff and adversarial ownership.</summary>
    internal static class IntegrationAcceptance
    {
        private static readonly JArray _checks = new JArray();
        private static string _executable;

        /// <summary>Launches acceptance from a genuinely detached parent or runs one silent bounded fixture gate.</summary>
        private static async Task<int> Main(string[] args)
        {
            _executable = Environment.ProcessPath;
            if (args.Length == 2 && args[0] == "launch")
            {
                var start = new ProcessStartInfo(_executable) { WorkingDirectory = AppContext.BaseDirectory, Arguments = "run " + GitFlowGit.Quote(args[1]) };
                int pid = GitFlowProcess.StartDetached(start); Console.WriteLine("Detached acceptance PID " + pid); return 0;
            }
            if (args.Length == 2 && args[0] == "fixture_gate")
            {
                if (GetConsoleWindow() != IntPtr.Zero) { Console.Error.Write("console exists"); return 90; }
                string started = Environment.GetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_STARTED_FILE"); if (!string.IsNullOrEmpty(started)) File.WriteAllText(started, "started");
                string mode = File.ReadAllText(args[1]).Trim();
                if (Environment.GetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_FAILURE") == "1") { Console.Error.Write("required fixture failure"); return 7; }
                if (mode == "slow") await Task.Delay(TimeSpan.FromSeconds(43));
                if (mode == "dirty") File.WriteAllText("unexpected.tmp", "retained check output");
                Console.Write("required fixture passed; no console; pid=" + Environment.ProcessId); return 0;
            }
            if (args.Length != 2 || args[0] != "run") return 2;
            int failed = 0;
            try
            {
                Require(GetConsoleWindow() == IntPtr.Zero, "Detached acceptance parent must have no console from startup.");
                await Case("submit_once_publish_versions_receipts_and_preservation", Healthy);
                await Case("required_failure_same_id_recovery", FailureRecovery);
                await Case("healthy_gate_spans_independent_renewals", LongGate);
                await Case("dirty_gate_and_unknown_processor_work_retained", Dirty);
                await Case("atomic_rejection_preserves_remote_main", AtomicReject);
                await Case("concurrent_claims_takeover_and_paused_owner_fencing", Ownership);
                await Case("unknown_policy_and_missing_tool_fail_closed", Unsupported);
                await Case("cancelled_gate_leaves_original_request_evidence", Cancellation);
                await Case("serial_versions_and_failed_processor_does_not_fail_new_workers", SerialAndRetained);
                await Case("released_independent_runner_configuration_launch_stop_and_hashes", ReleasedRunner);
            }
            catch (Exception error) { failed = 1; _checks.Add(new JObject { ["unexpected"] = error.ToString() }); }
            if (_checks.OfType<JObject>().Any(check => (bool?)check["passed"] == false)) failed = 1;
            GitFlowGit.Write(args[1], new JObject { ["schema"] = 1, ["runner_version"] = "0.2.0", ["finished_utc"] = DateTime.UtcNow.ToString("O"), ["detached_parent_no_console"] = GetConsoleWindow() == IntPtr.Zero,
                ["passed"] = _checks.OfType<JObject>().Count(check => (bool?)check["passed"] == true), ["failed"] = failed, ["checks"] = _checks,
                ["unverified"] = "Rendered GUI and transient windows created by third-party GUI/helper paths are not certified. Fixtures use native console executables and contained jobs; no desktop input, audio or player build." }.ToString(Formatting.Indented));
            return failed;
        }

        /// <summary>Records complete per-case failure evidence and continues independent fixtures.</summary>
        private static async Task Case(string name, Func<Task> action)
        {
            var watch = Stopwatch.StartNew(); var result = new JObject { ["name"] = name };
            try { await action(); result["passed"] = true; }
            catch (Exception error) { result["passed"] = false; result["error"] = error.ToString(); }
            result["seconds"] = watch.Elapsed.TotalSeconds; _checks.Add(result);
        }

        /// <summary>Verifies concurrent duplicate handoffs, exact byte/index preservation, serial versions and matching final receipts.</summary>
        private static async Task Healthy()
        {
            using (var fixture = new Fixture())
            {
                GitFlowSubmission first = fixture.Change("first");
                File.WriteAllText(Path.Combine(fixture.Remote, "hooks/post-receive"), "#!/bin/sh\nwhile read old new ref; do\n case \"$ref\" in refs/heads/geurts/gitflow/requests/*)\n child=$(git commit-tree \"$new^{tree}\" -p \"$new\" -m fixture_fast_control_advance) || exit 1\n git update-ref \"$ref\" \"$child\" \"$new\" || exit 1;;\n esac\ndone\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(fixture.Worker, "tracked.txt"), "staged worker change\r\n"); fixture.Work("add", "tracked.txt");
                File.AppendAllText(Path.Combine(fixture.Worker, "tracked.txt"), "unstaged worker change\r\n"); File.WriteAllText(Path.Combine(fixture.Worker, "untracked.txt"), "preserved");
                var before = Snapshot(fixture.Worker);
                var submitted = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => new GitFlowQueue().SubmitAsync(first)));
                File.Delete(Path.Combine(fixture.Remote, "hooks/post-receive"));
                Require(submitted.All(value => value.Id == submitted[0].Id), "Concurrent submissions created competing IDs.");
                Require(before == Snapshot(fixture.Worker), "Submission changed worker files, index, config, refs or attributes.");
                GitFlowPublisherResult published = await new GitFlowPublisher().RunAsync(fixture.Options);
                Require(published.State == "delivered" && published.Delivered == 1, published.Detail);
                JObject row = fixture.Row(submitted[0].Id); Require((string)row["version"] == "1.0.1" && (string)row["receipt"]["source"] == first.Source, "Version/receipt mismatch.");
                string main = fixture.Main; Require(main == (string)row["main_commit"], "Unverified main.");
                var reused = await new GitFlowQueue().SubmitAsync(first); Require(reused.Reused && reused.Id == submitted[0].Id && reused.State == "delivered", "Delivered snapshot was requeued.");
                Require(main == fixture.Main && before == Snapshot(fixture.Worker), "Duplicate submission changed main/worker.");
                Require((await new GitFlowPublisher().RunAsync(fixture.Options)).Delivered == 0, "Receipt was consumed twice.");
                var processor = new GitFlowGit(Path.Combine(fixture.Options.root, "candidate"), fixture.Remote);
                processor.Run(default, "config", "--local", "commit.gpgSign", "true");
                string signingConfig = File.ReadAllText(Path.Combine(fixture.Options.root, "candidate/.git/config"));
                var signing = await new GitFlowPublisher().RunAsync(fixture.Options);
                Require(signing.State == "failed" && signing.Detail.Contains("requires commit signing") && fixture.Main == main && File.ReadAllText(Path.Combine(fixture.Options.root, "candidate/.git/config")) == signingConfig, "Required signing was bypassed or prompted instead of blocked.");
            }
        }

        /// <summary>Retains failed gates and recovers the exact candidate/source/version after an explicit changed-environment operator continuation.</summary>
        private static async Task FailureRecovery()
        {
            using (var fixture = new Fixture())
            {
                var request = await new GitFlowQueue().SubmitAsync(fixture.Change("failure")); string main = fixture.Main;
                Environment.SetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_FAILURE", "1");
                try { Require((await new GitFlowPublisher().RunAsync(fixture.Options)).State == "failed", "Required failure was waived."); }
                finally { Environment.SetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_FAILURE", null); }
                JObject failed = fixture.Row(request.Id); Require(fixture.Main == main && (string)failed["status"] == "failed" && (string)failed["version"] == "1.0.1" && failed["receipt"] == null, "Failure changed main or lost version.");
                Require(((string)failed["blocker"]).Contains("required fixture failure"), "Failure lacks actionable original evidence.");
                fixture.Options.resumeRequestId = request.Id; fixture.Options.resumeReason = "Fixture-only transient environment failure removed; exact candidate/policy retained.";
                var recovered = await new GitFlowPublisher().RunAsync(fixture.Options); Require(recovered.State == "delivered", recovered.Detail);
                JObject delivered = fixture.Row(request.Id); Require((string)delivered["version"] == "1.0.1" && ((JArray)delivered["attempts"]).Count == 2, "Recovery changed identity/version or reset attempts.");
                string published = fixture.Main; var same = await new GitFlowPublisher().RunAsync(fixture.Options); Require(same.State == "delivered" && fixture.Main == published, same.Detail);
                Require((string)fixture.Row(request.Id)["version"] == "1.0.1", "Receipt recovery assigned a version again.");
            }
        }

        /// <summary>Proves the lease advances during a real 43-second required validation, independent of its checking task.</summary>
        private static async Task LongGate()
        {
            using (var fixture = new Fixture("slow"))
            {
                await new GitFlowQueue().SubmitAsync(fixture.Change("slow")); var run = new GitFlowPublisher().RunAsync(fixture.Options);
                await Task.Delay(12000); string first = fixture.Ref(GitFlowGit.LeaseRef); await Task.Delay(23000); string renewed = fixture.Ref(GitFlowGit.LeaseRef);
                Require(first != renewed, "Independent heartbeat did not advance during required validation.");
                var outcome = await run; Require(outcome.State == "delivered", outcome.Detail);
            }
        }

        /// <summary>Retains validation-created output and blocks repeated processing instead of cleaning a dirty checkout.</summary>
        private static async Task Dirty()
        {
            using (var fixture = new Fixture("dirty"))
            {
                await new GitFlowQueue().SubmitAsync(fixture.Change("dirty")); string main = fixture.Main;
                var result = await new GitFlowPublisher().RunAsync(fixture.Options); Require(result.State == "failed" && fixture.Main == main, result.Detail);
                string file = Path.Combine(fixture.Options.root, "candidate/unexpected.tmp"); Require(File.ReadAllText(file) == "retained check output", "Unknown output was discarded.");
                string before = Snapshot(Path.Combine(fixture.Options.root, "candidate"));
                Require((await new GitFlowPublisher().RunAsync(fixture.Options)).State == "failed" && before == Snapshot(Path.Combine(fixture.Options.root, "candidate")), "Unknown candidate work was reset on restart.");
            }
        }

        /// <summary>Exercises a real remote hook rejection and verifies all atomic publication targets stay unchanged.</summary>
        private static async Task AtomicReject()
        {
            using (var fixture = new Fixture())
            {
                var request = await new GitFlowQueue().SubmitAsync(fixture.Change("atomic")); string main = fixture.Main;
                string hook = Path.Combine(fixture.Remote, "hooks/pre-receive");
                File.WriteAllText(hook, "#!/bin/sh\nwhile read old new ref; do\n if test \"$ref\" = refs/heads/main; then echo required_remote_policy_rejection >&2; exit 1; fi\ndone\nexit 0\n", new UTF8Encoding(false));
                var result = await new GitFlowPublisher().RunAsync(fixture.Options);
                Require(result.State == "failed" && fixture.Main == main && (string)fixture.Row(request.Id)["status"] == "validating", "Rejected atomic push changed main/result or invented delivery. " + result.Detail);
                Require(File.Exists(Path.Combine(fixture.Options.root, "failure-" + request.Id + ".json")), "Uncertain publication lost local failure evidence.");
            }
        }

        /// <summary>Uses real Git arbitration plus an injected internal monotonic clock to prove abandonment and permanent paused-owner fencing.</summary>
        private static async Task Ownership()
        {
            using (var fixture = new Fixture())
            {
                GitFlowGit one = fixture.Store("one"), two = fixture.Store("two"); double clockA = 100, clockB = 100;
                using (var first = new GitFlowLease(one, phase => { }, () => clockA))
                using (var second = new GitFlowLease(two, phase => { }, () => clockB))
                {
                    bool[] claims = await Task.WhenAll(Task.Run(() => first.Acquire(default)), Task.Run(() => second.Acquire(default)));
                    Require(claims.Count(value => value) == 1, "Concurrent owners both claimed the same lease.");
                    GitFlowLease owner = claims[0] ? first : second, contender = claims[0] ? second : first;
                    Require(!contender.Acquire(default), "Live lease expired from timestamps/PID.");
                    if (claims[0]) clockB += 181; else clockA += 181;
                    Require(contender.Acquire(default), "Unchanged compatible lease did not recover after full monotonic observation.");
                    string main = fixture.Main, lease = fixture.Ref(GitFlowGit.LeaseRef);
                    foreach (string target in new[] { "refs/heads/main", GitFlowGit.RequestPrefix + "gf_" + main, GitFlowGit.Prefix + "wip/gf_" + main })
                    { bool blocked = false; try { owner.Publish(new Dictionary<string, string> { [target] = main }, false, default); } catch (IOException) { blocked = true; } Require(blocked, "Paused owner resumed writes to " + target); }
                    Require(fixture.Main == main && fixture.Ref(GitFlowGit.LeaseRef) == lease, "Fenced owner advanced remote state.");
                }
                var malformed = fixture.Store("future"); string old = fixture.Ref(GitFlowGit.LeaseRef); malformed.Fetch(old, default); JObject data = malformed.Record(old, default); data["lease_protocol"] = 999;
                string next = malformed.Commit(data, new[] { old }, default); malformed.Run(default, "push", fixture.Remote, next + ":" + GitFlowGit.LeaseRef);
                using (var future = new GitFlowLease(fixture.Store("reject"), phase => { })) { bool blocked = false; try { future.Acquire(default); } catch (InvalidDataException) { blocked = true; } Require(blocked, "Future lease was silently migrated."); }
            }
        }

        /// <summary>Rejects unsupported policy fields and a changed required native executable before any request can be claimed.</summary>
        private static async Task Unsupported()
        {
            bool blocked = false; try { new GitFlowPolicy("{\"schema\":999}"); } catch (InvalidDataException) { blocked = true; } Require(blocked, "Future policy accepted.");
            using (var fixture = new Fixture())
            {
                JObject policy = GitFlowGit.Json(File.ReadAllText(Path.Combine(fixture.Worker, GitFlowGit.PolicyFile))); policy["gates"][0]["executable_sha256"] = new string('0', 64);
                GitFlowGit.Write(Path.Combine(fixture.Worker, GitFlowGit.PolicyFile), policy.ToString()); fixture.Work("add", GitFlowGit.PolicyFile); fixture.Work("commit", "-m", "fixture policy unavailable"); fixture.Work("push", "origin", "main"); fixture.Base = fixture.Main;
                var request = await new GitFlowQueue().SubmitAsync(fixture.Change("unavailable")); string main = fixture.Main;
                var result = await new GitFlowPublisher().RunAsync(fixture.Options); Require(result.State == "failed" && fixture.Main == main && (string)fixture.Row(request.Id)["status"] == "queued", "Missing tool was relabelled as a worker failure or bypassed. " + result.Detail);
            }
            using (var fixture = new Fixture())
            {
                var prior = await new GitFlowQueue().SubmitAsync(fixture.Change("before_legacy")); string main = fixture.Main;
                fixture.Work("push", fixture.Remote, main + ":refs/heads/codex/integration-lock");
                Require((await new GitFlowPublisher().RunAsync(fixture.Options)).State == "failed" && fixture.Main == main && (string)fixture.Row(prior.Id)["status"] == "queued", "Native publisher competed with legacy coordination.");
                fixture.Work("switch", "main"); GitFlowSubmission blockedSource = fixture.Change("legacy_rejected"); bool refused = false;
                try { await new GitFlowQueue().SubmitAsync(blockedSource); } catch (InvalidOperationException) { refused = true; }
                Require(refused && fixture.Work("ls-remote", fixture.Remote, GitFlowGit.RequestPrefix + "gf_" + blockedSource.Source).Length == 0 && fixture.Ref("refs/heads/codex/integration-lock") == main, "Native handoff changed legacy state or bypassed migration.");
            }
            using (var fixture = new Fixture())
            {
                GitFlowSubmission source = fixture.Change("global_hook"); string main = fixture.Main;
                string xdg = Path.Combine(fixture.Root, "xdg"), hooks = Path.Combine(fixture.Root, "required-hooks"); Directory.CreateDirectory(Path.Combine(xdg, "git")); Directory.CreateDirectory(hooks);
                File.WriteAllText(Path.Combine(hooks, "pre-push"), "#!/bin/sh\necho required_global_hook_rejection >&2\nexit 1\n", new UTF8Encoding(false));
                string config = Path.Combine(xdg, "git/config"); File.WriteAllText(config, "[core]\n hooksPath = " + hooks.Replace('\\', '/') + "\n askPass = must_never_launch_askpass.exe\n", new UTF8Encoding(false));
                string before = File.ReadAllText(config), previous = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"); bool blockedHook = false;
                try
                {
                    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", xdg);
                    Require(new GitFlowGit(fixture.Worker, fixture.Remote).Run(default, "config", "--get", "core.askPass").Trim().Length == 0, "Interactive askpass was not suppressed independently from required hooks.");
                    try { await new GitFlowQueue().SubmitAsync(source); } catch (IOException error) { blockedHook = error.ToString().Contains("required_global_hook_rejection"); }
                }
                finally { Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previous); }
                Require(blockedHook && File.ReadAllText(config) == before && fixture.Main == main && fixture.Work("ls-remote", fixture.Remote, GitFlowGit.RequestPrefix + "gf_" + source.Source).Length == 0, "Normal global Git hooks were hidden, changed or bypassed.");
            }
        }

        /// <summary>Cancels a real stalled check, contains its process and retains original request/candidate/version evidence.</summary>
        private static async Task Cancellation()
        {
            using (var fixture = new Fixture("slow"))
            {
                var request = await new GitFlowQueue().SubmitAsync(fixture.Change("cancel")); string main = fixture.Main;
                string started = Path.Combine(fixture.Root, "gate-started.txt"); Environment.SetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_STARTED_FILE", started);
                try
                {
                    using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(40)))
                    {
                        var run = new GitFlowPublisher().RunAsync(fixture.Options, false, stop.Token);
                        while (!File.Exists(started) && !run.IsCompleted) { stop.Token.ThrowIfCancellationRequested(); await Task.Delay(500); }
                        stop.Cancel(); var result = await run; Require(result.State == "failed" && fixture.Main == main, "Cancellation published main.");
                    }
                }
                finally { Environment.SetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_STARTED_FILE", null); }
                JObject row = fixture.Row(request.Id); Require((string)row["version"] == "1.0.1" && File.Exists(Path.Combine(fixture.Options.root, "failure-" + request.Id + ".json")), "Cancellation lost retained original version/evidence.");
            }
        }

        /// <summary>Exercises serial queue-owned versions and ensures retained processor failure never marks an unrelated worker failed.</summary>
        private static async Task SerialAndRetained()
        {
            using (var fixture = new Fixture())
            {
                var first = await new GitFlowQueue().SubmitAsync(fixture.Change("serial_one"));
                fixture.Work("switch", "main"); var second = await new GitFlowQueue().SubmitAsync(fixture.Change("serial_two"));
                // Independent files merge cleanly so this tests assignment rather than conflict recovery.
                // The second source has a genuine text overlap; preserve and expose that conflict instead of inventing a version.
                var run = await new GitFlowPublisher().RunAsync(fixture.Options); Require(run.Delivered == 1 && run.State == "failed", "Conflicted second task should retain its original integration failure. " + run.Detail);
                Require((string)fixture.Row(first.Id)["version"] == "1.0.1" && (string)fixture.Row(second.Id)["status"] == "failed", "Incorrect first delivery/second conflict.");
                fixture.Work("switch", "main"); var third = await new GitFlowQueue().SubmitAsync(fixture.Change("serial_three"));
                var stopped = await new GitFlowPublisher().RunAsync(fixture.Options); Require(stopped.State == "failed" && (string)fixture.Row(third.Id)["status"] == "queued", "Retained processor work was attributed to a new worker.");
            }
            using (var fixture = new Fixture())
            {
                var first = await new GitFlowQueue().SubmitAsync(fixture.Change("version_one"));
                fixture.Work("switch", "main"); GitFlowGit.Write(Path.Combine(fixture.Worker, "independent.txt"), "independent\r\n"); fixture.Work("add", "independent.txt"); fixture.Work("commit", "-m", "second independent source");
                var second = await new GitFlowQueue().SubmitAsync(new GitFlowSubmission(fixture.Worker, fixture.Work("rev-parse", "HEAD").Trim(), fixture.Base, "patch", "version two", "Independent source; base dependency; owning fixture evidence/environment; GUI unverified, human review next.", dependencies: new[] { first.Id }));
                var run = await new GitFlowPublisher().RunAsync(fixture.Options); Require(run.Delivered == 1 && (string)fixture.Row(second.Id)["status"] == "queued", "Declared dependency was not respected. " + run.Detail);
                run = await new GitFlowPublisher().RunAsync(fixture.Options); Require(run.Delivered == 1, run.Detail);
                Require((string)fixture.Row(first.Id)["version"] == "1.0.1" && (string)fixture.Row(second.Id)["version"] == "1.0.2", "Serial tasks reused or skipped their versions.");
            }
        }

        /// <summary>Exercises the real separately compiled host through reviewed configuration, trusted launch, graceful stop and runner hash rejection.</summary>
        private static async Task ReleasedRunner()
        {
            string runner = Environment.GetEnvironmentVariable("GEURTS_GITFLOW_FIXTURE_RUNNER_DIR"); Require(!string.IsNullOrEmpty(runner), "Supply the separately built release runner directory.");
            using (var fixture = new Fixture())
            {
                var request = await new GitFlowQueue().SubmitAsync(fixture.Change("released_runner"));
                var configuration = new GitFlowConfiguration(); configuration.ConfigurePublisher(fixture.Options);
                string manifest = File.ReadAllText(Path.Combine(runner, "RunnerRelease.json"));
                int pid = configuration.StartPublisher(fixture.Options.root, Path.Combine(runner, "GeurtsGitFlow.exe"), manifest);
                using (var ownedHost = Process.GetProcessById(pid))
                {
                try
                {
                string invocation = Directory.GetFiles(fixture.Options.root, "invocation-*.json").Single(); string outcome = invocation + ".outcome.json";
                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(60) && !File.Exists(outcome))
                {
                    if ((string)fixture.Row(request.Id)["status"] == "delivered") { configuration.RequestStop(fixture.Options.root); break; }
                    await Task.Delay(2000);
                }
                while (!File.Exists(outcome) && timeout.Elapsed < TimeSpan.FromSeconds(100)) await Task.Delay(1000);
                Require(File.Exists(outcome), "Independent runner did not acknowledge graceful stop within its bound; PID " + pid);
                JObject result = GitFlowGit.Json(File.ReadAllText(outcome)); Require((string)result["State"] != "failed", result.ToString());
                Require((string)fixture.Row(request.Id)["status"] == "delivered", "Released runner did not deliver its original request.");
                using (var owner = new FileStream(Path.Combine(fixture.Options.root, "owner.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                GitFlowPublisherOptions saved = GitFlowConfiguration.ReadOptions(File.ReadAllText(Path.Combine(fixture.Options.root, "configuration.json"))); Require(string.IsNullOrEmpty(saved.resumeRequestId), "Scoped invocation leaked into saved all-request configuration.");
                string copy = Path.Combine(fixture.Root, "runner_copy"); Directory.CreateDirectory(copy);
                foreach (string path in Directory.GetFiles(runner)) File.Copy(path, Path.Combine(copy, Path.GetFileName(path)));
                File.AppendAllText(Path.Combine(copy, "GeurtsGitFlow.dll"), "tampered"); bool blocked = false;
                try { configuration.StartPublisher(fixture.Options.root, Path.Combine(copy, "GeurtsGitFlow.exe"), manifest); } catch (IOException) { blocked = true; }
                Require(blocked, "Runner replacement bypassed trusted committed release hashes.");
                }
                finally
                {
                    // This fixture created and retained the exact OS process handle; cleanup never targets an unrelated PID/owner.
                    configuration.RequestStop(fixture.Options.root);
                    if (!ownedHost.WaitForExit(45000)) { ownedHost.Kill(entireProcessTree: true); ownedHost.WaitForExit(5000); }
                }
                }
            }
        }

        /// <summary>Compares raw files/attributes including indexes, configuration and branch refs without writing the inspected tree.</summary>
        private static string Snapshot(string root) => string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal).Select(path => path.Substring(root.Length) + ":" + GitFlowGit.Hash(File.ReadAllBytes(path)) + ":" + File.GetAttributes(path)));
        /// <summary>Raises one concrete fixture assertion with retained stack evidence.</summary>
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        /// <summary>Queries console ownership only; never activates a window or injects input.</summary>
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();

        /// <summary>Owns one disposable local remote, worker and configured processor; cleanup never crosses its checked root.</summary>
        private sealed class Fixture : IDisposable
        {
            internal readonly string Root, Worker, Remote;
            internal readonly GitFlowPublisherOptions Options;
            internal string Base;
            internal string Main => Work("ls-remote", Remote, "refs/heads/main").Split('\t')[0];

            /// <summary>Creates a committed generic policy using this independently launched silent console gate.</summary>
            internal Fixture(string mode = "pass")
            {
                Root = Path.Combine(Path.GetTempPath(), "gf_pub_" + Guid.NewGuid().ToString("N")); Worker = Path.Combine(Root, "worker"); Remote = Path.Combine(Root, "remote.git");
                GitFlowGit.CheckPath(Path.Combine(Root, "processor/store.git/objects/pack/pack-" + new string('a', 40) + ".pack")); Directory.CreateDirectory(Worker);
                var git = new GitFlowGit(Worker, Remote); git.Run(default, "init", "--bare", "--quiet", Remote); Work("init", "-b", "main");
                GitFlowGit.Write(Path.Combine(Worker, ".gitattributes"), "* text=auto eol=crlf\r\n");
                GitFlowGit.Write(Path.Combine(Worker, "VERSION"), "1.0.0\r\n"); GitFlowGit.Write(Path.Combine(Worker, "tracked.txt"), "base\r\n"); GitFlowGit.Write(Path.Combine(Worker, "validation.mode"), mode);
                var policy = new JObject { ["schema"] = 1, ["adapter"] = "generic_git", ["publication"] = "atomic_main", ["headless_silent_commands"] = true, ["version_file"] = "VERSION",
                    ["protected_paths"] = new JArray("validation.mode"), ["gates"] = new JArray(new JObject { ["id"] = "required_check", ["executable"] = _executable, ["executable_sha256"] = GitFlowGit.Hash(File.ReadAllBytes(_executable)),
                        ["tool_files"] = GitFlowConfiguration.ToolFiles(_executable), ["arguments"] = new JArray("fixture_gate", "validation.mode"), ["timeout_seconds"] = 90 }) };
                GitFlowGit.Write(Path.Combine(Worker, GitFlowGit.PolicyFile), policy.ToString()); Work("add", "."); Work("commit", "-m", "fixture baseline"); Work("remote", "add", "origin", Remote); Work("push", "origin", "main"); Base = Main;
                Options = new GitFlowPublisherOptions { root = Path.Combine(Root, "processor"), remote = Remote };
            }

            /// <summary>Creates one exact task source without changing queue-owned version/policy metadata.</summary>
            internal GitFlowSubmission Change(string label)
            { Work("switch", "-c", "task_" + label); GitFlowGit.Write(Path.Combine(Worker, "tracked.txt"), label + "\r\n"); Work("add", "tracked.txt"); Work("commit", "-m", label); return new GitFlowSubmission(Worker, Work("rev-parse", "HEAD").Trim(), Base, "patch", label, "Scope/reason: fixture. Dependencies: base. Evidence: owning acceptance. Environment: Windows native console. Unverified: GUI; next step human review."); }
            /// <summary>Runs fixture Git through the exact production native transport.</summary>
            internal string Work(params string[] args) => new GitFlowGit(Worker, Remote).Run(default, args);
            /// <summary>Reads an exact current remote ref using no shared FETCH_HEAD.</summary>
            internal string Ref(string reference) => Work("ls-remote", Remote, reference).Split('\t')[0];
            /// <summary>Creates a separate fixture contender object store.</summary>
            internal GitFlowGit Store(string name) { string path = Path.Combine(Root, name); Directory.CreateDirectory(path); var git = new GitFlowGit(path, Remote); git.Run(default, "init", "--bare", "--quiet"); return git; }
            /// <summary>Reads the committed current request record from a disposable inspection store.</summary>
            internal JObject Row(string id) { var git = Store("read_" + Guid.NewGuid().ToString("N")); string sha = Ref(GitFlowGit.RequestPrefix + id); git.Fetch(sha, default); return git.Record(sha, default); }
            /// <summary>Removes only this fixture after the publisher/lease/native child jobs have ended.</summary>
            public void Dispose() { if (!Path.GetFullPath(Root).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "gf_pub_", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture root."); GitFlowGit.RemoveTemporary(Root); }
        }
    }
}
