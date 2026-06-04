using System.Collections.Generic;
using BovineLabs.Core.Editor.Inspectors;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [CustomEditor(typeof(GridStampSchemaObject))]
    [CanEditMultipleObjects]
    public class GridStampSchemaObjectEditor : ElementEditor
    {
        private readonly Dictionary<string, VisualElement> elements = new();

        protected override VisualElement CreateElement(SerializedProperty property)
        {
            var element = base.CreateElement(property);
            elements[property.name] = element;

            if (property.name == nameof(GridStampSchemaObject.Kind))
                element.RegisterCallback<SerializedPropertyChangeEvent>(_ => UpdateVisibility());

            return element;
        }

        protected override void PostElementCreation(VisualElement root, bool createdElements)
        {
            base.PostElementCreation(root, createdElements);
            UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (!elements.TryGetValue(nameof(GridStampSchemaObject.Kind), out _)) return;

            var kind = (ShapeKind)serializedObject.FindProperty(nameof(GridStampSchemaObject.Kind)).intValue;

            SetVisible(nameof(GridStampSchemaObject.RectMin), kind is ShapeKind.SolidRect or ShapeKind.RectShell);
            SetVisible(nameof(GridStampSchemaObject.RectSize), kind is ShapeKind.SolidRect or ShapeKind.RectShell);
            SetVisible(nameof(GridStampSchemaObject.ShellThickness), kind == ShapeKind.RectShell);

            SetVisible(nameof(GridStampSchemaObject.DiscCenter), kind == ShapeKind.Disc);
            SetVisible(nameof(GridStampSchemaObject.DiscRadius), kind == ShapeKind.Disc);

            SetVisible(nameof(GridStampSchemaObject.AnnulusCenter), kind == ShapeKind.Annulus);
            SetVisible(nameof(GridStampSchemaObject.AnnulusOuterRadius), kind == ShapeKind.Annulus);
            SetVisible(nameof(GridStampSchemaObject.AnnulusInnerRadius), kind == ShapeKind.Annulus);

            SetVisible(nameof(GridStampSchemaObject.CapsuleStart), kind == ShapeKind.Capsule);
            SetVisible(nameof(GridStampSchemaObject.CapsuleEnd), kind == ShapeKind.Capsule);
            SetVisible(nameof(GridStampSchemaObject.CapsuleRadius), kind == ShapeKind.Capsule);

            SetVisible(nameof(GridStampSchemaObject.EllipseCenter), kind == ShapeKind.Ellipse);
            SetVisible(nameof(GridStampSchemaObject.EllipseRadii), kind == ShapeKind.Ellipse);

            SetVisible(nameof(GridStampSchemaObject.RoundedRectMin), kind == ShapeKind.RoundedRect);
            SetVisible(nameof(GridStampSchemaObject.RoundedRectSize), kind == ShapeKind.RoundedRect);
            SetVisible(nameof(GridStampSchemaObject.RoundedRectRadius), kind == ShapeKind.RoundedRect);

            SetVisible(nameof(GridStampSchemaObject.ThickLineStart), kind == ShapeKind.ThickLine);
            SetVisible(nameof(GridStampSchemaObject.ThickLineEnd), kind == ShapeKind.ThickLine);
            SetVisible(nameof(GridStampSchemaObject.ThickLineRadius), kind == ShapeKind.ThickLine);
        }

        private void SetVisible(string propertyName, bool visible)
        {
            if (elements.TryGetValue(propertyName, out var element)) ElementUtility.SetVisible(element, visible);
        }
    }
}