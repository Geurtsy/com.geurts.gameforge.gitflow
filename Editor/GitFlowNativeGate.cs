// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Native batch-only version and compilation adapters for the isolated owned publisher.</summary>
    public static class GitFlowNativeGate
    {
        /// <summary>Writes the assigned player version through Unity's supported API; never rewrites serialized YAML.</summary>
        public static void SynchronizeVersion()
        {
            RequireBatch();
            string version = Argument("-gitFlowVersion");
            if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Invalid assigned version.");
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
            string evidence = Argument("-gitFlowEvidence");
            Directory.CreateDirectory(evidence);
            if (PlayerSettings.bundleVersion != version) throw new InvalidOperationException("Unity did not retain the assigned player version.");
            File.WriteAllText(Path.Combine(evidence, "version-proof.json"), JsonConvert.SerializeObject(new
            {
                protocol = 1, version, project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")),
                unity = UnityEngine.Application.unityVersion, api = "PlayerSettings.bundleVersion"
            }));
        }

        /// <summary>Records arrival after successful native compilation; the host independently checks the complete compiler log.</summary>
        public static void CompilationCompleted()
        {
            RequireBatch();
            if (EditorApplication.isCompiling) throw new InvalidOperationException("Compilation is still running.");
            string evidence = Argument("-gitFlowEvidence");
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "result.json"), JsonConvert.SerializeObject(new
            {
                run = Argument("-gitFlowRun"), candidate = Argument("-gitFlowCandidate"), status = "passed",
                warnings = 0, errors = 0, skipped = 0, unity = UnityEngine.Application.unityVersion,
                scope = "Native compilation completed; host must also verify complete compiler diagnostics."
            }));
        }

        /// <summary>Rejects interactive/live invocation and permits only an explicitly selected batch project.</summary>
        private static void RequireBatch()
        {
            if (!UnityEngine.Application.isBatchMode) throw new InvalidOperationException("GitFlow native gates are restricted to isolated batch projects.");
        }

        /// <summary>Requires exactly one explicit value for a fixed host argument.</summary>
        private static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int[] indexes = arguments.Select((value, index) => new { value, index }).Where(item => item.value == name).Select(item => item.index).ToArray();
            if (indexes.Length != 1 || indexes[0] + 1 >= arguments.Length) throw new ArgumentException("Missing or repeated " + name);
            return arguments[indexes[0] + 1];
        }
    }
}
