using BovineLabs.Core.Editor.Inspectors;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using UnityEditor;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [CustomEditor(typeof(GridFieldSchemaObject))]
    [CanEditMultipleObjects]
    public class GridFieldSchemaObjectEditor : ElementEditor
    {
    }
}