// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Geurts.GameForge.GitFlow.Editor;
using NUnit.Framework;

namespace Geurts.GameForge.GitFlow.Tests
{
    /// <summary>Checks local tool selection and safe failure boundaries without executing any discovered file.</summary>
    public sealed class GitFlowToolDiscoveryTests
    {
        private string _root;

        /// <summary>Creates only disposable empty executable-shaped files; no program is launched.</summary>
        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "gitflow_discovery_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        /// <summary>Removes only this fixture's exact temporary folder.</summary>
        [TearDown]
        public void TearDown()
        {
            Assert.That(Path.GetFullPath(_root).StartsWith(Path.Combine(Path.GetTempPath(), "gitflow_discovery_test_"), StringComparison.OrdinalIgnoreCase));
            Directory.Delete(_root, true);
        }

        /// <summary>A valid explicit manual override wins over a different discovered tool.</summary>
        [Test]
        public void KeepsExistingManualOverride()
        {
            string manual = FileAt("manual.exe"), discovered = FileAt("discovered.exe");
            Assert.That(GitFlowToolDiscovery.Resolve(manual, new[] { discovered }, CancellationToken.None), Is.EqualTo(manual));
        }

        /// <summary>Paths with spaces work, while missing files and script wrappers are skipped.</summary>
        [Test]
        public void FindsNativeFileAfterMissingAndWrapperCandidates()
        {
            string wrapper = FileAt("codex.cmd"), native = FileAt("tool with spaces.exe");
            Assert.That(GitFlowToolDiscovery.Resolve("", new[] { Path.Combine(_root, "missing.exe"), wrapper, native }, CancellationToken.None), Is.EqualTo(native));
            Assert.That(GitFlowToolDiscovery.Resolve("", new[] { wrapper }, CancellationToken.None), Is.Empty);
        }

        /// <summary>Discovery must not interpret current-directory, network or Store-alias paths as local tools.</summary>
        [Test]
        public void RejectsRelativeNetworkAndStoreAliasPaths()
        {
            Assert.That(GitFlowToolDiscovery.IsNativeFile("python.exe"), Is.False);
            Assert.That(GitFlowToolDiscovery.IsNativeFile("C:python.exe"), Is.False);
            Assert.That(GitFlowToolDiscovery.IsNativeFile(@"\\server\share\python.exe"), Is.False);
            Assert.That(GitFlowToolDiscovery.IsNativeFile("//server/share/python.exe"), Is.False);
            string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "python.exe");
            Assert.That(GitFlowToolDiscovery.IsNativeFile(alias), Is.False);
        }

        /// <summary>Already cancelled requests do not retain or search for another tool.</summary>
        [Test]
        public void CancellationStopsBeforeKeepingAnExistingSelection()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => GitFlowToolDiscovery.Resolve(FileAt("selected.exe"), Array.Empty<string>(), cancellation.Token));
            }
        }

        /// <summary>Malformed or unexpectedly large candidate streams cannot trigger an unbounded scan.</summary>
        [Test]
        public void CandidateSearchIsBounded()
        {
            string late = FileAt("late.exe");
            Assert.That(GitFlowToolDiscovery.Resolve("", Enumerable.Repeat("", 256).Concat(new[] { late }), CancellationToken.None), Is.Empty);
        }

        /// <summary>Missing model-discovery prerequisites provide a safe useful correction before process launch.</summary>
        [Test]
        public void MissingSelectionNamesTheToolAndDiscoveryAction()
        {
            var error = Assert.Throws<ArgumentException>(() => GitFlowToolDiscovery.RequireSelected("", "Python"));
            StringAssert.Contains("Python", error.Message);
            StringAssert.Contains("Find Python and Codex executables", error.Message);
            GitFlowToolDiscovery.RequireSelected(FileAt("installed.exe"), "Codex");
        }

        /// <summary>Writes a harmless discovery candidate inside this test's owned root.</summary>
        private string FileAt(string name)
        {
            string path = Path.Combine(_root, name); File.WriteAllBytes(path, Array.Empty<byte>()); return path;
        }
    }
}
