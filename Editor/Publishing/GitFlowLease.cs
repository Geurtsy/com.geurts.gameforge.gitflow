// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Persistent fast-forward lease with monotonic abandoned-owner observation and permanently fenced uncertainty.</summary>
    internal sealed class GitFlowLease : IDisposable
    {
        internal const int OwnerSeconds = 180, RenewalSeconds = 20, WriteReserveSeconds = 120;
        private readonly GitFlowGit _git;
        private readonly object _guard = new object();
        private readonly string _owner = Guid.NewGuid().ToString("N");
        private readonly CancellationTokenSource _heartbeatStop = new CancellationTokenSource();
        private readonly Action<string> _health;
        private readonly Func<double> _clock;
        private string _commit, _observed;
        private double _deadline, _observedAt;
        private bool _fenced, _active;
        private Task _heartbeat;
        internal string Failure { get; private set; }
        internal bool Active { get { lock (_guard) return _active && !_fenced && Now < _deadline; } }

        /// <summary>Binds ownership to one configured store and truthful local status callback.</summary>
        internal GitFlowLease(GitFlowGit git, Action<string> health, Func<double> monotonicClock = null)
        { _git = git; _health = health; _clock = monotonicClock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency); }
        private double Now => _clock();

        /// <summary>Observes compatible records over a complete local owner interval before any automatic takeover.</summary>
        internal bool Acquire(CancellationToken cancellation)
        {
            lock (_guard)
            {
                if (_fenced) throw new IOException("This owner is permanently fenced. " + Failure);
                try
                {
                    var refs = _git.Refs(cancellation, GitFlowGit.LeaseRef);
                    refs.TryGetValue(GitFlowGit.LeaseRef, out string predecessor);
                    if (predecessor != null)
                    {
                        _git.Fetch(predecessor, cancellation); JObject lease = _git.Record(predecessor, cancellation); Validate(lease);
                        if ((string)lease["state"] == "owned")
                        {
                            if (_observed != predecessor) { _observed = predecessor; _observedAt = Now; return false; }
                            if (Now - _observedAt < OwnerSeconds) return false;
                        }
                    }
                    _commit = predecessor;
                    string child = _git.Commit(Record("owned"), new[] { predecessor }, cancellation);
                    double dispatch = Now;
                    try { _git.Run(cancellation, "push", "--atomic", "--porcelain", _git.Remote, child + ":" + GitFlowGit.LeaseRef); }
                    catch (IOException)
                    {
                        // A proven sibling winning the ordinary claim race is contention, never a task failure.
                        var actual = _git.Refs(cancellation, GitFlowGit.LeaseRef);
                        if (actual.TryGetValue(GitFlowGit.LeaseRef, out string winner) && winner != predecessor && winner != child)
                        { _observed = null; return false; }
                        throw;
                    }
                    _git.Verify(GitFlowGit.LeaseRef, child, cancellation);
                    if (Now >= dispatch + OwnerSeconds) throw new IOException("Claim acknowledgement exceeded its monotonic deadline.");
                    _commit = child; _deadline = dispatch + OwnerSeconds; _active = true; _observed = null;
                    _health("owned");
                    _heartbeat = Task.Run(Heartbeat);
                    return true;
                }
                catch { _observed = null; throw; }
            }
        }

        /// <summary>Renews on its own task during checks; every renewal uses the same fencing guard as publication.</summary>
        private async Task Heartbeat()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(RenewalSeconds), _heartbeatStop.Token).ConfigureAwait(false);
                    Publish(new Dictionary<string, string>(), false, _heartbeatStop.Token);
                }
            }
            catch (OperationCanceledException) when (_heartbeatStop.IsCancellationRequested) { }
            catch (Exception error) { lock (_guard) Fence(error); }
        }

        /// <summary>Atomically advances the same lease with every main/result/WIP target and verifies all exact objects.</summary>
        internal void Publish(Dictionary<string, string> targets, bool release, CancellationToken cancellation)
        {
            if (targets.Count > 3 || targets.Keys.Any(reference => reference != "refs/heads/main" && !reference.StartsWith(GitFlowGit.Prefix, StringComparison.Ordinal))) throw new ArgumentException("Unsupported fenced publication targets.");
            lock (_guard)
            {
                try
                {
                    RequireOwner(WriteReserveSeconds);
                    var before = _git.Refs(cancellation, GitFlowGit.LeaseRef);
                    if (!before.TryGetValue(GitFlowGit.LeaseRef, out string current) || current != _commit) throw new IOException("Lease changed before publication.");
                    // Prepare all immutable objects first; reserve the complete bounded transport/verification allowance afterwards.
                    string child = _git.Commit(Record(release ? "released" : "owned"), new[] { _commit }, cancellation);
                    RequireOwner(WriteReserveSeconds);
                    double previousDeadline = _deadline, dispatch = Now;
                    var expected = new Dictionary<string, string>(targets) { [GitFlowGit.LeaseRef] = child };
                    _git.Run(cancellation, new[] { "push", "--atomic", "--porcelain", _git.Remote }.Concat(expected.Select(pair => pair.Value + ":" + pair.Key)).ToArray());
                    var actual = _git.Refs(cancellation, expected.Keys.ToArray());
                    foreach (var pair in expected) if (!actual.TryGetValue(pair.Key, out string value) || value != pair.Value) throw new IOException("Atomic verification changed/absent: " + pair.Key);
                    _git.Run(cancellation, new[] { "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", _git.Remote }.Concat(expected.Values.Distinct()).ToArray());
                    foreach (string value in expected.Values.Distinct()) if (_git.Run(cancellation, "cat-file", "-t", value).Trim() != "commit") throw new IOException("Published object is not a commit.");
                    if (Now >= previousDeadline || Now >= dispatch + OwnerSeconds) throw new IOException("Verification crossed the prior monotonic ownership deadline.");
                    _commit = child; _deadline = dispatch + OwnerSeconds; _active = !release;
                    _health(release ? "released" : "owned");
                }
                catch (Exception error) { Fence(error); throw; }
            }
        }

        /// <summary>Checks the live local deadline; no later acknowledgement can revive an expired owner.</summary>
        private void RequireOwner(int reserve)
        { if (_fenced || !_active || Now + reserve >= _deadline) throw new IOException("Owner has insufficient proven monotonic write allowance; it is fenced. " + Failure); }

        /// <summary>Retains the original ownership failure and stops every subsequent integration write.</summary>
        private void Fence(Exception error) { _fenced = true; _active = false; Failure = error.GetType().Name + ": " + error.Message; try { _health("fenced: " + Failure); } catch { /* Original ownership evidence remains authoritative. */ } }

        /// <summary>Creates the compatible lease contract; UTC is audit-only and never controls takeover.</summary>
        private JObject Record(string state) => new JObject { ["schema"] = 1, ["kind"] = "lease", ["lease_protocol"] = 1, ["owner_seconds"] = OwnerSeconds,
            ["renewal_seconds"] = RenewalSeconds, ["transport_seconds"] = 15, ["write_reserve_seconds"] = WriteReserveSeconds,
            ["owner"] = _owner, ["host"] = Environment.MachineName, ["pid"] = Process.GetCurrentProcess().Id, ["state"] = state, ["updated_utc"] = DateTime.UtcNow.ToString("O") };

        /// <summary>Refuses legacy/future protocols, unknown timings and malformed records rather than assuming abandonment.</summary>
        internal static void Validate(JObject record)
        {
            if (new[] { "schema", "lease_protocol", "owner_seconds", "renewal_seconds", "transport_seconds", "write_reserve_seconds" }.Any(name => record[name]?.Type != JTokenType.Integer) ||
                (int?)record["schema"] != 1 || (string)record["kind"] != "lease" || (int?)record["lease_protocol"] != 1 || (int?)record["owner_seconds"] != OwnerSeconds ||
                (int?)record["renewal_seconds"] != RenewalSeconds || (int?)record["transport_seconds"] != 15 || (int?)record["write_reserve_seconds"] != WriteReserveSeconds ||
                !System.Text.RegularExpressions.Regex.IsMatch((string)record["owner"] ?? "", "^[0-9a-f]{32}$") || !new[] { "owned", "released" }.Contains((string)record["state"]))
                throw new InvalidDataException("Incompatible native lease; preserve it and confirm the old owner stopped before an explicit protocol migration. No timestamp expiry is allowed.");
        }

        /// <summary>Stops renewal, joins its contained work and releases only a still-proven owner through the same atomic fence.</summary>
        public void Dispose()
        {
            _heartbeatStop.Cancel(); try { _heartbeat?.GetAwaiter().GetResult(); } catch { }
            lock (_guard) if (_active && !_fenced) { try { Publish(new Dictionary<string, string>(), true, CancellationToken.None); } catch { /* Fenced status retains the failure. */ } }
            _heartbeatStop.Dispose();
        }
    }
}
