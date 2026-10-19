// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Exact immutable worker handoff. Validation claims are retained as claims; the publisher runs required gates itself.</summary>
    public sealed class GitFlowSubmission
    {
        public string Repository { get; }
        public string Remote { get; }
        public string Source { get; }
        public string Base { get; }
        public string Increment { get; }
        public string Title { get; }
        public string Handoff { get; }

        /// <summary>Describes one handoff including scope/reason, evidence/environment, dependencies and unverified checks with next steps.</summary>
        public GitFlowSubmission(string repository, string source, string integratedBase, string increment, string title, string handoff, string remote = "origin")
        { Repository = repository; Source = source; Base = integratedBase; Increment = increment; Title = title; Handoff = handoff; Remote = remote; }
    }

    /// <summary>Confirmed submit-once result. A stable ID is pending integration, never a delivery claim.</summary>
    public sealed class GitFlowSubmissionResult
    {
        public string Id { get; }
        public string ControlCommit { get; }
        public string State { get; }
        public bool Reused { get; }
        /// <summary>Captures verified committed handoff identity and its currently recorded phase.</summary>
        internal GitFlowSubmissionResult(string id, string commit, string state, bool reused) { Id = id; ControlCommit = commit; State = state; Reused = reused; }
    }

    /// <summary>Shared API/no-code submission service; caller indexes, branches and files are never written and no publisher is launched.</summary>
    public sealed class GitFlowQueue
    {
        /// <summary>Submits an exact source once within a bounded operation, or returns its existing original request without waiting.</summary>
        public Task<GitFlowSubmissionResult> SubmitAsync(GitFlowSubmission request, CancellationToken cancellation = default) => Task.Run(() => Submit(request, cancellation), cancellation);

        /// <summary>Uses a temporary bare object store; only a verified request ref creation is an outbound write.</summary>
        private static GitFlowSubmissionResult Submit(GitFlowSubmission request, CancellationToken cancellation)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            GitFlowGit.RequireSha(request.Source); GitFlowGit.RequireSha(request.Base); GitFlowPolicy.NextVersion("0.0.0", request.Increment);
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 160 || string.IsNullOrWhiteSpace(request.Handoff) || request.Handoff.Length > 32768)
                throw new ArgumentException("Supply a title and complete handoff (scope/reason, dependencies, evidence/environment and unverified checks/next steps), at most 32 KiB.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Remote ?? "", "^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,63}$")) throw new ArgumentException("Select an existing remote name.");
            string root = Path.Combine(Path.GetTempPath(), "ggf_submit_" + Guid.NewGuid().ToString("N"));
            GitFlowGit.CheckPath(root + "/objects/pack/pack-" + new string('a', 40) + ".pack");
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(90));
                try
                {
                    var source = new GitFlowGit(Path.GetFullPath(request.Repository), Path.GetFullPath(request.Repository));
                    string remote = source.Run(budget.Token, "config", "--get", "remote." + request.Remote + ".url").Trim(); GitFlowGit.CheckRemote(remote);
                    Directory.CreateDirectory(root); var git = new GitFlowGit(root, remote); git.Run(budget.Token, "init", "--bare", "--quiet");
                    string id = "gf_" + request.Source, reference = GitFlowGit.RequestPrefix + id;
                    var refs = git.Refs(budget.Token, reference, "refs/heads/main");
                    // An existing exact snapshot always keeps its ID, original handoff, failure, version and receipt.
                    if (refs.TryGetValue(reference, out string existing)) return Existing(git, request, id, existing, budget.Token);
                    if (!refs.TryGetValue("refs/heads/main", out string main)) throw new InvalidDataException("Remote main is absent. No request was created.");
                    git.Fetch(main, budget.Token);
                    git.Run(budget.Token, "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", Path.GetFullPath(request.Repository), request.Source);
                    git.Run(budget.Token, "merge-base", "--is-ancestor", request.Base, request.Source);
                    git.Run(budget.Token, "merge-base", "--is-ancestor", request.Base, main);
                    if (request.Source == request.Base) throw new InvalidDataException("The source contains no update beyond its integrated base.");
                    var policy = new GitFlowPolicy(git.Run(budget.Token, "show", main + ":" + GitFlowGit.PolicyFile));
                    CheckScope(git, policy, request.Base, request.Source, budget.Token);
                    var record = new JObject { ["schema"] = 1, ["kind"] = "request", ["adapter"] = "generic_git", ["id"] = id, ["source"] = request.Source,
                        ["base"] = request.Base, ["status"] = "queued", ["increment"] = request.Increment, ["title"] = request.Title, ["handoff"] = request.Handoff,
                        ["size"] = git.Run(budget.Token, "diff", "--shortstat", request.Base, request.Source).Trim(), ["policy_sha256"] = policy.Fingerprint, ["submitted_utc"] = DateTime.UtcNow.ToString("O") };
                    string commit = git.Commit(record, new[] { request.Source }, budget.Token);
                    try { git.Run(budget.Token, "push", "--porcelain", remote, commit + ":" + reference); }
                    catch (IOException)
                    {
                        // A losing concurrent create may reuse only the winning exact identity, never create a second ID.
                        var raced = git.Refs(budget.Token, reference);
                        if (!raced.TryGetValue(reference, out string winner)) throw;
                        return Existing(git, request, id, winner, budget.Token);
                    }
                    git.Verify(reference, commit, budget.Token); return new GitFlowSubmissionResult(id, commit, "queued", false);
                }
                finally { if (Directory.Exists(root)) GitFlowGit.RemoveTemporary(root); }
            }
        }

        /// <summary>Returns the original request while refusing a source/base/increment identity conflict.</summary>
        private static GitFlowSubmissionResult Existing(GitFlowGit git, GitFlowSubmission request, string id, string commit, CancellationToken cancellation)
        {
            git.Fetch(commit, cancellation); JObject data = git.Record(commit, cancellation);
            if ((int?)data["schema"] != 1 || (string)data["kind"] != "request" || (string)data["id"] != id || (string)data["source"] != request.Source ||
                (string)data["base"] != request.Base || (string)data["increment"] != request.Increment) throw new InvalidDataException("Existing request has incompatible metadata. Preserve its original ID and ask the owning operator to review it.");
            return new GitFlowSubmissionResult(id, commit, (string)data["status"], true);
        }

        /// <summary>Prevents worker edits to project-owned controllers, queue metadata and queue-assigned versions.</summary>
        internal static void CheckScope(GitFlowGit git, GitFlowPolicy policy, string integratedBase, string source, CancellationToken cancellation)
        {
            string[] paths = git.Run(cancellation, "-c", "core.quotePath=false", "diff", "--name-only", "--no-renames", integratedBase, source).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string path in paths) { GitFlowPolicy.Relative(path); if (policy.Protected(path)) throw new InvalidDataException("Worker changed protected controller/version path: " + path + ". Submit through its owning policy-maintenance process."); }
        }
    }
}
