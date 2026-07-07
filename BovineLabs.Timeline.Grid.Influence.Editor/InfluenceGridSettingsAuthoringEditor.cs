using System.Collections.Generic;
using BovineLabs.Core.Editor.Inspectors;
using BovineLabs.Core.Editor.ObjectManagement;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using UnityEditor;
using UnityEngine.UIElements;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [CustomEditor(typeof(InfluenceGridSettingsAuthoring))]
    public class InfluenceGridSettingsAuthoringEditor : ElementEditor
    {
        private readonly List<GridInfluenceValidation.Issue> issues = new();

        protected override VisualElement CreateElement(SerializedProperty property)
        {
            return property.name switch
            {
                nameof(InfluenceGridSettingsAuthoring.Fields) => new AssetCreator<GridFieldSchemaObject>(
                    serializedObject, property).Element,
                nameof(InfluenceGridSettingsAuthoring.Stamps) => new AssetCreator<GridStampSchemaObject>(
                    serializedObject, property).Element,
                _ => base.CreateElement(property)
            };
        }

        protected override void PostElementCreation(VisualElement root, bool createdElements)
        {
            base.PostElementCreation(root, createdElements);

            var summary = new HelpBox(string.Empty, HelpBoxMessageType.None)
            {
                style = { display = DisplayStyle.None },
            };

            root.Add(new Button(() =>
            {
                issues.Clear();
                GridInfluenceValidation.Validate((InfluenceGridSettingsAuthoring)target, issues);
                GridInfluenceValidation.Report(issues, "Grid Influence validation");

                var (errors, warnings) = GridInfluenceValidation.Summarize(issues);
                summary.text = errors == 0 && warnings == 0
                    ? "Validation passed: 0 errors / 0 warnings."
                    : $"{errors} error(s) / {warnings} warning(s) — see Console.";
                summary.messageType = errors > 0
                    ? HelpBoxMessageType.Error
                    : warnings > 0
                        ? HelpBoxMessageType.Warning
                        : HelpBoxMessageType.Info;
                summary.style.display = DisplayStyle.Flex;
            })
            {
                text = "Validate",
            });
            root.Add(summary);
        }
    }
}