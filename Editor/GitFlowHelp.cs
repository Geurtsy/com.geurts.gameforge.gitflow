// IMPORTANT: Follow the manifest-selected Geurts Technical and Folder Structure techniques.
using System;
using System.IO;
using Geurts.GameForge.God.Editor;
using UnityEditor;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Geurts.GameForge.GitFlow.Editor
{
    /// <summary>Package-owned tutorial displayed inside Unity without an external document application.</summary>
    public sealed class GitFlowHelp : EditorWindow
    {
        /// <summary>Opens the same local tutorial from either the queue window or Unity's menu.</summary>
        [MenuItem("Tools/Geurts Game Forge/GitFlow Help")]
        public static void Open() => ForgeEditorTheme.OpenWindow<GitFlowHelp>(new Vector2(420, 320), "GitFlow Help");

        /// <summary>Reads the installed guide through Unity's package location, with no network or queue action.</summary>
        public static string ReadGuide()
        {
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GitFlowHelp).Assembly)?.resolvedPath;
            if (string.IsNullOrEmpty(package)) throw new InvalidOperationException("GitFlow is not resolved by Unity Package Manager.");
            string guide = File.ReadAllText(Path.Combine(package, "Documentation~", "QueueLocalChanges.md"));
            return Regex.Replace(guide, @"(?s)<!-- GEURTS-RELEASE-CONTEXT:BEGIN -->.*?<!-- GEURTS-RELEASE-CONTEXT:END -->", "").Trim();
        }

        /// <summary>Renders the full selectable help text in a wrapping, scrollable owned Unity view.</summary>
        public void CreateGUI()
        {
            var root = rootVisualElement; root.Clear(); ForgeEditorTheme.ApplyToolkit(root);
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 12;
            var title = new Label("Queue local changes"); title.style.fontSize = 20; root.Add(title);
            var scroll = new ScrollView(); scroll.style.flexGrow = 1; root.Add(scroll);
            try
            {
                var content = new TextField { name = "gitflow-help-content", value = ReadGuide(), multiline = true, isReadOnly = true };
                content.style.whiteSpace = WhiteSpace.Normal;
                scroll.Add(content);
            }
            catch (Exception error) { scroll.Add(new HelpBox("The installed local guide is unavailable: " + error.Message, HelpBoxMessageType.Error)); }
        }
    }
}
