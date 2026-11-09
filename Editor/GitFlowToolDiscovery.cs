// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using Microsoft.Win32;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Finds existing local native tools on explicit request, without running or installing them.</summary>
    internal static class GitFlowToolDiscovery
    {
        /// <summary>Preserves an existing manual selection, otherwise checks bounded installation candidates.</summary>
        internal static string Resolve(string selected, IEnumerable<string> candidates, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (IsNativeFile(selected)) return Path.GetFullPath(selected);
            foreach (string candidate in candidates.Take(256))
            {
                cancellation.ThrowIfCancellationRequested();
                if (IsNativeFile(candidate)) return Path.GetFullPath(candidate);
            }
            return "";
        }

        /// <summary>Accepts absolute local executables, excluding Store execution aliases and shell wrappers.</summary>
        internal static bool IsNativeFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 259) return false;
            try
            {
                if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
                    !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return false;
                string absolute = Path.GetFullPath(path);
                string aliases = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps") + Path.DirectorySeparatorChar;
                return !absolute.StartsWith(aliases, StringComparison.OrdinalIgnoreCase) && File.Exists(absolute);
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is NotSupportedException || error is UnauthorizedAccessException || error is SecurityException)
            { return false; }
        }

        /// <summary>Explains missing selections before process launch rather than leaking a generic path exception.</summary>
        internal static void RequireSelected(string path, string tool)
        {
            if (!IsNativeFile(path)) throw new ArgumentException(tool + " needs an existing local native .exe. Click Find Python and Codex executables, or choose its full path under Local tools, then retry. Shell wrappers and Windows Store aliases are unsupported.");
        }

        /// <summary>Checks PATH and standard Python registration/install locations; never searches a whole drive.</summary>
        internal static IEnumerable<string> PythonCandidates()
        {
            foreach (string path in PathCandidates("python.exe", "python3.exe")) yield return path;
            foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                foreach (string path in RegisteredPython(hive)) yield return path;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string folder in ChildDirectories(Path.Combine(local, "Programs", "Python"), "Python*")) yield return Path.Combine(folder, "python.exe");
        }

        /// <summary>Checks native CLI locations and bounded npm/Desktop package roots, without invoking wrappers.</summary>
        internal static IEnumerable<string> CodexCandidates()
        {
            foreach (string path in PathCandidates("codex.exe")) yield return path;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(local, "Programs", "OpenAI", "Codex", "bin", "codex.exe");
            string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai");
            foreach (string package in new[] { "codex", "codex-win32-x64", "codex-win32-arm64" })
                foreach (string target in new[] { "x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc" })
                    yield return Path.Combine(npm, package, "vendor", target, "codex", "codex.exe");
            foreach (string folder in ChildDirectories(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"), "OpenAI.Codex_*"))
                yield return Path.Combine(folder, "app", "resources", "codex.exe");
        }

        /// <summary>Enumerates only absolute local PATH entries, retaining paths containing spaces.</summary>
        private static IEnumerable<string> PathCandidates(params string[] names)
        {
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Take(128))
            {
                string folder = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                string[] paths;
                try
                {
                    if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal) || folder.StartsWith("//", StringComparison.Ordinal)) continue;
                    paths = names.Select(name => Path.Combine(folder, name)).ToArray();
                }
                catch (ArgumentException) { continue; }
                foreach (string path in paths) yield return path;
            }
        }

        /// <summary>Reads PythonCore registration only; inaccessible or absent roots remain unavailable.</summary>
        private static IEnumerable<string> RegisteredPython(RegistryKey hive)
        {
            var paths = new List<string>();
            try
            {
                using (RegistryKey root = hive.OpenSubKey(@"Software\Python\PythonCore"))
                {
                    if (root == null) return paths;
                    foreach (string version in root.GetSubKeyNames().OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase).Take(32))
                        using (RegistryKey install = root.OpenSubKey(version + @"\InstallPath"))
                        {
                            if (install?.GetValue("ExecutablePath") is string executable) paths.Add(executable);
                            else if (install?.GetValue("") is string folder) paths.Add(Path.Combine(folder, "python.exe"));
                        }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException) { }
            return paths;
        }

        /// <summary>Reads at most sixteen direct matching installation folders, never recursive or network trees.</summary>
        private static string[] ChildDirectories(string root, string pattern)
        {
            try { return Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly).Take(16).OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException) { return Array.Empty<string>(); }
        }
    }
}
