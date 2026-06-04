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
    }
}