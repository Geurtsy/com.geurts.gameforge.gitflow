// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Native batch-only version and compilation adapters for the isolated owned publisher.</summary>
    public static class GitFlowNativeGate
    {
        private static readonly List<CompilerMessage> _messages = new List<CompilerMessage>();
        private static string _evidence, _run, _candidate;
        private static int _assemblies;
        private static bool _compilationStarted;

        /// <summary>Forces full script compilation so a warm Library cannot hide previously cached compiler warnings.</summary>
        public static void BeginCompilation()
        {
            RequireBatch();
            if (Argument("-gitFlowCompilationProtocol") != "1") throw new ArgumentException("Unsupported full compilation protocol.");
            _evidence = Argument("-gitFlowEvidence"); _run = Argument("-gitFlowRun"); _candidate = Argument("-gitFlowCandidate");
            Directory.CreateDirectory(_evidence); _messages.Clear(); _assemblies = 0; _compilationStarted = false;
            CompilationPipeline.compilationStarted += StartCompilation;
            CompilationPipeline.assemblyCompilationFinished += CollectCompilation;
            CompilationPipeline.compilationFinished += FinishCompilation;
            CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
        }

        /// <summary>Records the requested pipeline start before accepting its later completion callback.</summary>
        private static void StartCompilation(object context) { _compilationStarted = true; }

        /// <summary>Retains every compiler message emitted by each assembly in this explicit full pass.</summary>
        private static void CollectCompilation(string assembly, CompilerMessage[] messages)
        {
            _assemblies++; _messages.AddRange(messages);
        }

        /// <summary>Writes the exact-run diagnostic result only after the full compilation pipeline finishes.</summary>
        private static void FinishCompilation(object context)
        {
            CompilationPipeline.compilationStarted -= StartCompilation;
            CompilationPipeline.assemblyCompilationFinished -= CollectCompilation;
            CompilationPipeline.compilationFinished -= FinishCompilation;
            int warnings = _messages.Count(item => item.type == CompilerMessageType.Warning);
            int errors = _messages.Count(item => item.type == CompilerMessageType.Error) + (!_compilationStarted ? 1 : 0);
            int expectedAssemblies = CompilationPipeline.GetAssemblies().Length;
            File.WriteAllText(Path.Combine(_evidence, "diagnostics.json"), JsonConvert.SerializeObject(new
            {
                complete_matching_diagnostics = _messages.Select(item => item.file + "(" + item.line + "," + item.column + "): " + item.type + " " + item.message).ToArray(),
                observed_assembly_callbacks = _assemblies, full_script_rebuild_requested = true
            }));
            File.WriteAllText(Path.Combine(_evidence, "compilation.json"), JsonConvert.SerializeObject(new
            {
                run = _run, candidate = _candidate, status = "native_pipeline_completed", observed_warnings = warnings, observed_errors = errors,
                observed_assembly_callbacks = _assemblies, expected_assemblies = expectedAssemblies,
                pipeline_started = _compilationStarted, full_script_rebuild_requested = true, unity = UnityEngine.Application.unityVersion
            }));
            EditorApplication.Exit(errors == 0 && warnings == 0 && expectedAssemblies > 0 ? 0 : 2);
        }

        /// <summary>Writes the assigned player version through Unity's supported API; never rewrites serialized YAML.</summary>
        public static void SynchronizeVersion()
        {
            RequireBatch();
            string version = Argument("-gitFlowVersion");
            if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new ArgumentException("Invalid assigned version.");
            PlayerSettings.bundleVersion = version;
            AssetDatabase.SaveAssets();
            if (!Environment.GetCommandLineArgs().Contains("-gitFlowVersionProofProtocol")) return;
            if (Argument("-gitFlowVersionProofProtocol") != "1") throw new ArgumentException("Unsupported native version proof protocol.");
            string evidence = Argument("-gitFlowEvidence");
            Directory.CreateDirectory(evidence);
            if (PlayerSettings.bundleVersion != version) throw new InvalidOperationException("Unity did not retain the assigned player version.");
            File.WriteAllText(Path.Combine(evidence, "version-proof.json"), JsonConvert.SerializeObject(new
            {
                protocol = 1, version, project = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..")),
                unity = UnityEngine.Application.unityVersion, api = "PlayerSettings.bundleVersion"
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
