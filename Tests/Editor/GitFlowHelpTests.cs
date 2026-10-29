// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using Geurts.GameForge.GitFlow.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Tests
{
    /// <summary>Checks that installed help is available through the actual owned Unity view rather than an external opener.</summary>
    public sealed class GitFlowHelpTests
    {
        /// <summary>Builds the native help view and verifies it exposes the entire installed guide without queue actions.</summary>
        [Test]
        public void HelpViewDisplaysInstalledGuideInsideUnity()
        {
            var window = ScriptableObject.CreateInstance<GitFlowHelp>();
            try
            {
                window.CreateGUI();
                var field = window.rootVisualElement.Q<TextField>("gitflow-help-content");
                Assert.That(field, Is.Not.Null);
                Assert.That(field.isReadOnly, Is.True);
                Assert.That(field.value, Is.EqualTo(GitFlowHelp.ReadGuide()));
                Assert.That(field.value, Does.Contain("Queue local changes"));
                Assert.That(field.value, Does.Contain("Queued does not mean delivered"));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
