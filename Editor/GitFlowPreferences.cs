// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Text.RegularExpressions;
using Geurts.GameForge.God;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Per-brick no-code drafts stored only through God; loading them performs no network, launch or project configuration.</summary>
    [Serializable]
    public sealed class GitFlowPreferences : IValidatedBrickSettings
    {
        public int schema = 1;
        public string repository = "", remoteName = "origin", publisherRoot = "", publisherRemote = "", runnerExecutable = "", versionFile = "VERSION", protectedPaths = "Tools/Validation";
        public GitFlowGatePreference[] gates = Array.Empty<GitFlowGatePreference>();
        /// <summary>Preserves future/malformed settings instead of silently replacing them with defaults.</summary>
        public string ValidateSettings() => schema != 1 ? "Unknown GitFlow settings schema; preserve the file and upgrade its owner." :
            !Regex.IsMatch(remoteName ?? "", "^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,63}$") ? "Select a valid existing remote name." :
            gates == null || gates.Length > 8 || Array.Exists(gates, gate => gate == null) ? "Malformed command draft array; preserve the saved data and use its owning repair process." : null;
    }

    /// <summary>Unity-serializable command draft, separate from immutable committed project policy.</summary>
    [Serializable]
    public sealed class GitFlowGatePreference
    {
        public string id = "required_check", executable = "", arguments = "", additionalToolFiles = "";
        public int timeoutSeconds = 300;
    }

    /// <summary>Explicit typed start request. The manifest must come from the trusted installed package, never from an untrusted runner folder.</summary>
    public sealed class GitFlowHostStart
    {
        public string Root { get; }
        public string Executable { get; }
        public string TrustedManifest { get; }
        public string ResumeId { get; }
        public string ResumeReason { get; }
        /// <summary>Freezes reviewed runner selection and optional finite original-ID operator continuation.</summary>
        public GitFlowHostStart(string root, string executable, string trustedManifest, string resumeId = "", string resumeReason = "")
        { Root = root; Executable = executable; TrustedManifest = trustedManifest; ResumeId = resumeId; ResumeReason = resumeReason; }
    }

    /// <summary>Explicit root selection for a graceful stop request; receipt observation remains separate.</summary>
    public sealed class GitFlowHostStop
    {
        public string Root { get; }
        /// <summary>Selects an already configured root without resolving or starting another runner.</summary>
        public GitFlowHostStop(string root) { Root = root; }
    }
}
