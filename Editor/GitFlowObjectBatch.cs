// IMPORTANT: This script must comply with GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsTechnicalTechnique.md and folder placement rules in GeurtsGameForgeCommandments/GeurtsTechniques/GeurtsFolderStructureTechnique.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>One exact Git object query outcome, including a missing object without inventing content.</summary>
    internal sealed class GitFlowBlobResult
    {
        internal string Text { get; }
        internal string Error { get; }
        /// <summary>Retains the observed content or bounded error for its original query.</summary>
        internal GitFlowBlobResult(string text, string error) { Text = text; Error = error; }
    }

    /// <summary>Decodes bounded cat-file batches by byte lengths, preserving UTF-8 and missing-object coverage.</summary>
    internal static class GitFlowObjectBatch
    {
        /// <summary>Reads each requested blob once through the existing owned process transport.</summary>
        internal static Dictionary<string, GitFlowBlobResult> Read(string[] specifications, Func<string, string> execute)
        {
            var result = new Dictionary<string, GitFlowBlobResult>(StringComparer.Ordinal);
            if (specifications.Length == 0) return result;
            byte[] data = new UTF8Encoding(false, true).GetBytes(execute(string.Join("\n", specifications) + "\n"));
            int offset = 0;
            foreach (string specification in specifications)
            {
                int newline = Array.IndexOf(data, (byte)10, offset);
                if (newline < offset) throw new InvalidDataException("Incomplete Git object batch header.");
                string header = Encoding.ASCII.GetString(data, offset, newline - offset); offset = newline + 1;
                if (header == specification + " missing") { result.Add(specification, new GitFlowBlobResult(null, "Exact object/path is unavailable locally.")); continue; }
                var match = Regex.Match(header, "^[a-f0-9]{40} blob (?<bytes>[0-9]+)$", RegexOptions.CultureInvariant);
                if (!match.Success || !int.TryParse(match.Groups["bytes"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int length) || length > GitFlowInspection.MaximumJsonBytes || length > data.Length - offset - 1 || data[offset + length] != 10)
                    throw new InvalidDataException("Invalid, oversized or incomplete Git object batch body.");
                result.Add(specification, new GitFlowBlobResult(new UTF8Encoding(false, true).GetString(data, offset, length), null)); offset += length + 1;
            }
            if (offset != data.Length) throw new InvalidDataException("Unexpected extra Git batch response.");
            return result;
        }
    }
}
