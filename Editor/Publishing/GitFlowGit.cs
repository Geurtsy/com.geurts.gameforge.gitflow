// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Fixed-vector Git transport for the native generic queue; all writes target owned stores or explicit remote refs.</summary>
    internal sealed class GitFlowGit
    {
        internal const string Prefix = "refs/heads/geurts/gitflow/";
        internal const string LeaseRef = Prefix + "lease";
        internal const string RequestPrefix = Prefix + "requests/";
        internal const string PolicyFile = ".geurts-gitflow.json";
        internal readonly string DirectoryPath;
        internal readonly string Remote;

        /// <summary>Binds one object store to one already reviewed local or public HTTPS remote.</summary>
        internal GitFlowGit(string directory, string remote) { CheckPath(directory); CheckRemote(remote); DirectoryPath = directory; Remote = remote; }

        /// <summary>Executes a bounded transport without inherited Git overrides, credentials, hooks changes or shell interpretation.</summary>
        internal string Run(CancellationToken cancellation, params string[] arguments) => RunIndex(null, cancellation, arguments);

        /// <summary>Uses a private temporary index only when constructing immutable control objects.</summary>
        private string RunIndex(string index, CancellationToken cancellation, params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { WorkingDirectory = DirectoryPath, UseShellExecute = false, CreateNoWindow = true };
            string[] prefix = { "-c", "credential.helper=", "-c", "credential.interactive=never", "-c", "protocol.ext.allow=never", "-c", "protocol.ssh.allow=never", "-c", "gc.auto=0" };
            start.Arguments = string.Join(" ", prefix.Concat(arguments).Select(Quote));
            foreach (string key in start.EnvironmentVariables.Keys.Cast<string>().Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.EnvironmentVariables.Remove(key);
            start.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0"; start.EnvironmentVariables["GCM_INTERACTIVE"] = "Never";
            start.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"] = "1"; start.EnvironmentVariables["GIT_CONFIG_GLOBAL"] = "NUL";
            string token = Environment.GetEnvironmentVariable("GEURTS_GITFLOW_TOKEN");
            if (!string.IsNullOrEmpty(token) && Remote.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                start.EnvironmentVariables["GIT_CONFIG_COUNT"] = "1";
                start.EnvironmentVariables["GIT_CONFIG_KEY_0"] = "http." + Remote + ".extraHeader";
                start.EnvironmentVariables["GIT_CONFIG_VALUE_0"] = "Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + token));
            }
            start.EnvironmentVariables.Remove("GEURTS_GITFLOW_TOKEN");
            start.EnvironmentVariables["GIT_AUTHOR_NAME"] = start.EnvironmentVariables["GIT_COMMITTER_NAME"] = "Geurts GitFlow";
            start.EnvironmentVariables["GIT_AUTHOR_EMAIL"] = start.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = "gitflow@example.invalid";
            if (index != null) start.EnvironmentVariables["GIT_INDEX_FILE"] = index;
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(15));
                try { return GitFlowProcess.Run(start, budget.Token); }
                catch (IOException error) { throw new IOException("Git " + arguments[0] + ": " + error.Message, error); }
            }
        }

        /// <summary>Advertises only exact SHA-1 commit refs, rejecting malformed and excessive responses.</summary>
        internal Dictionary<string, string> Refs(CancellationToken cancellation, params string[] patterns)
        {
            string raw = Run(cancellation, new[] { "ls-remote", "--refs", Remote }.Concat(patterns).ToArray());
            var refs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string row in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            { string[] parts = row.Split('\t'); if (parts.Length != 2 || !IsSha(parts[0]) || refs.ContainsKey(parts[1])) throw new InvalidDataException("Malformed/duplicate advertised ref."); refs.Add(parts[1], parts[0]); }
            if (refs.Count > 130) throw new InvalidDataException("Queue exceeds 128 active/control records; archive through the owning project before proceeding.");
            return refs;
        }

        /// <summary>Fetches an advertised exact object without modifying FETCH_HEAD or a shared local branch.</summary>
        internal void Fetch(string sha, CancellationToken cancellation)
        { RequireSha(sha); Run(cancellation, "fetch", "--quiet", "--no-tags", "--no-write-fetch-head", Remote, sha); if (Run(cancellation, "cat-file", "-t", sha).Trim() != "commit") throw new InvalidDataException("Advertised object is not a commit."); }

        /// <summary>Reads bounded immutable control JSON from its exact committed object.</summary>
        internal JObject Record(string sha, CancellationToken cancellation) => Json(Run(cancellation, "show", sha + ":GitFlowRecord.json"));

        /// <summary>Constructs a control commit with ordinary parent lineage and a separate private index.</summary>
        internal string Commit(JObject value, IEnumerable<string> parents, CancellationToken cancellation)
        {
            string token = Guid.NewGuid().ToString("N"), file = Path.Combine(DirectoryPath, "gf_" + token + ".json"), index = Path.Combine(DirectoryPath, "gf_" + token + ".index");
            try
            {
                string json = value.ToString(Formatting.Indented); Json(json); Write(file, json);
                string blob = Run(cancellation, "hash-object", "-w", file).Trim();
                RunIndex(index, cancellation, "read-tree", "--empty"); RunIndex(index, cancellation, "update-index", "--add", "--cacheinfo", "100644," + blob + ",GitFlowRecord.json");
                string tree = RunIndex(index, cancellation, "write-tree").Trim();
                var args = new List<string> { "commit-tree", tree }; foreach (string parent in parents.Where(value => !string.IsNullOrEmpty(value)).Distinct()) { RequireSha(parent); args.Add("-p"); args.Add(parent); }
                args.Add("-m"); args.Add("Geurts GitFlow protocol 1 control"); return Run(cancellation, args.ToArray()).Trim();
            }
            finally { if (File.Exists(file)) File.Delete(file); if (File.Exists(index)) File.Delete(index); }
        }

        /// <summary>Requires the current ref to equal the expected commit and verifies its fetched type/content.</summary>
        internal void Verify(string reference, string expected, CancellationToken cancellation)
        { var refs = Refs(cancellation, reference); if (!refs.TryGetValue(reference, out string actual) || actual != expected) throw new IOException("Remote verification changed/absent: " + reference + ". Stop writes and inspect retained evidence."); Fetch(actual, cancellation); }

        /// <summary>Reads strict bounded JSON, preserving duplicate/future-data failures rather than resetting files.</summary>
        internal static JObject Json(string text)
        {
            if (Encoding.UTF8.GetByteCount(text) > 1024 * 1024) throw new InvalidDataException("JSON exceeds one MiB.");
            using (var reader = new JsonTextReader(new StringReader(text)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
            { JObject data = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); if (reader.Read()) throw new InvalidDataException("Trailing JSON."); return data; }
        }

        /// <summary>Quotes a Windows CRT argument; no shell expansion occurs.</summary>
        internal static string Quote(string value)
        {
            var text = new StringBuilder("\""); int slashes = 0;
            foreach (char character in value ?? "") { if (character == '\\') { slashes++; continue; } text.Append('\\', character == '"' ? 2 * slashes + 1 : slashes).Append(character); slashes = 0; }
            return text.Append('\\', 2 * slashes).Append('"').ToString();
        }

        /// <summary>Hashes raw bytes for portable evidence and tool/configuration identities.</summary>
        internal static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        /// <summary>Writes authored UTF-8 without a BOM using consistent CRLF.</summary>
        internal static void Write(string path, string text) { CheckPath(path); File.WriteAllText(path, text.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false)); }
        /// <summary>Recognizes only full supported object identities.</summary>
        internal static bool IsSha(string value) => value != null && Regex.IsMatch(value, "^[0-9a-f]{40}$");
        /// <summary>Rejects unresolved source names before a mutable operation.</summary>
        internal static void RequireSha(string value) { if (!IsSha(value)) throw new ArgumentException("An exact 40-character commit is required."); }
        /// <summary>Rejects custom transports and embedded secrets; private authentication remains unsupported.</summary>
        internal static void CheckRemote(string remote)
        {
            if (Path.IsPathRooted(remote ?? "")) { CheckPath(remote); return; }
            if (!Uri.TryCreate(remote, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) throw new ArgumentException("Use an absolute local remote or public credential-free HTTPS URL.");
        }
        /// <summary>Checks paths and every existing ancestor before creating or accessing queue-owned content.</summary>
        internal static void CheckPath(string path)
        {
            string full = Path.GetFullPath(path); if (full.Length >= 260) throw new PathTooLongException(full + " (" + full.Length + " characters).");
            for (string part = full; !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
                if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) throw new IOException("Reparse path is unsupported: " + part);
        }
        /// <summary>Removes only a positively owned temporary fixture/store, after all jobs have ended.</summary>
        internal static void RemoveTemporary(string path)
        { CheckPath(path); foreach (string file in System.IO.Directory.GetFiles(path, "*", SearchOption.AllDirectories)) { CheckPath(file); File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly); } System.IO.Directory.Delete(path, true); }
    }
}
