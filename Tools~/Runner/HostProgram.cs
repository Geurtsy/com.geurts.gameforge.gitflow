// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Geurts.GameForge.GitFlow.Editor;
using Newtonsoft.Json;

namespace Geurts.GameForge.GitFlow.Runner
{
    /// <summary>Committed independent host. Workers use submit once; only an explicitly configured operator starts serve/once.</summary>
    internal static class HostProgram
    {
        /// <summary>Runs one explicit supported mode, recording errors to a requested file without windows or interactive credentials.</summary>
        private static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length != 3 || (args[0] != "once" && args[0] != "serve")) throw new ArgumentException("Help: GeurtsGitFlow once|serve <reviewed-options.json> <outcome.json>. Use the Editor's Publisher setup controls. No automatic configuration, runner replacement or repair is performed.");
                var json = GitFlowGit.Json(File.ReadAllText(args[1]));
                string[] fields = { "schema", "root", "remote", "originalCheckout", "resumeRequestId", "resumeReason" };
                foreach (var property in json.Properties()) if (Array.IndexOf(fields, property.Name) < 0) throw new InvalidDataException("Unknown host option: " + property.Name);
                GitFlowPublisherOptions options = json.ToObject<GitFlowPublisherOptions>();
                var result = await new GitFlowPublisher().RunAsync(options, args[0] == "serve");
                GitFlowGit.Write(args[2], JsonConvert.SerializeObject(result, Formatting.Indented));
                return result.State == "failed" || result.State == "partial" ? 1 : 0;
            }
            catch (Exception error)
            {
                if (args.Length >= 3) GitFlowGit.Write(args[args.Length - 1], JsonConvert.SerializeObject(new { state = "failed", error = error.GetType().Name + ": " + error.Message }, Formatting.Indented));
                return 1;
            }
        }
    }
}
