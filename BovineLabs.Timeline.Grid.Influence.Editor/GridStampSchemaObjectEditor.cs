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
            this.elements[property.name] = element;

            if (property.name == nameof(GridStampSchemaObject.Kind))
            {
                element.RegisterCallback<SerializedPropertyChangeEvent>(_ => this.UpdateVisibility());
            }

            return element;
        }

        protected override void PostElementCreation(VisualElement root, bool createdElements)
        {
            base.PostElementCreation(root, createdElements);
            this.UpdateVisibility();
        }

        private void UpdateVisibility()
        {
            if (!this.elements.TryGetValue(nameof(GridStampSchemaObject.Kind), out _))
            {
                return;
            }

            var kind = (ShapeKind)this.serializedObject.FindProperty(nameof(GridStampSchemaObject.Kind)).intValue;

            this.SetVisible(nameof(GridStampSchemaObject.RectMin), kind is ShapeKind.SolidRect or ShapeKind.RectShell);
            this.SetVisible(nameof(GridStampSchemaObject.RectSize), kind is ShapeKind.SolidRect or ShapeKind.RectShell);
            this.SetVisible(nameof(GridStampSchemaObject.ShellThickness), kind == ShapeKind.RectShell);

            this.SetVisible(nameof(GridStampSchemaObject.DiscCenter), kind == ShapeKind.Disc);
            this.SetVisible(nameof(GridStampSchemaObject.DiscRadius), kind == ShapeKind.Disc);

            this.SetVisible(nameof(GridStampSchemaObject.AnnulusCenter), kind == ShapeKind.Annulus);
            this.SetVisible(nameof(GridStampSchemaObject.AnnulusOuterRadius), kind == ShapeKind.Annulus);
            this.SetVisible(nameof(GridStampSchemaObject.AnnulusInnerRadius), kind == ShapeKind.Annulus);

            this.SetVisible(nameof(GridStampSchemaObject.CapsuleStart), kind == ShapeKind.Capsule);
            this.SetVisible(nameof(GridStampSchemaObject.CapsuleEnd), kind == ShapeKind.Capsule);
            this.SetVisible(nameof(GridStampSchemaObject.CapsuleRadius), kind == ShapeKind.Capsule);

            this.SetVisible(nameof(GridStampSchemaObject.EllipseCenter), kind == ShapeKind.Ellipse);
            this.SetVisible(nameof(GridStampSchemaObject.EllipseRadii), kind == ShapeKind.Ellipse);

            this.SetVisible(nameof(GridStampSchemaObject.RoundedRectMin), kind == ShapeKind.RoundedRect);
            this.SetVisible(nameof(GridStampSchemaObject.RoundedRectSize), kind == ShapeKind.RoundedRect);
            this.SetVisible(nameof(GridStampSchemaObject.RoundedRectRadius), kind == ShapeKind.RoundedRect);

            this.SetVisible(nameof(GridStampSchemaObject.ThickLineStart), kind == ShapeKind.ThickLine);
            this.SetVisible(nameof(GridStampSchemaObject.ThickLineEnd), kind == ShapeKind.ThickLine);
            this.SetVisible(nameof(GridStampSchemaObject.ThickLineRadius), kind == ShapeKind.ThickLine);
        }

        private void SetVisible(string propertyName, bool visible)
        {
            if (this.elements.TryGetValue(propertyName, out var element))
            {
                ElementUtility.SetVisible(element, visible);
            }
        }
    }
}
