using System.Collections.Generic;
using BovineLabs.Core.Editor.Inspectors;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    [CustomEditor(typeof(GridStampSchemaObject))]
    [CanEditMultipleObjects]
    public class GridStampSchemaObjectEditor : ElementEditor
    {
        private readonly Dictionary<string, VisualElement> elements = new();
        private Texture2D preview;

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
            root.Add(new IMGUIContainer(DrawPreview));
        }

        private void DrawPreview()
        {
            if (target is not GridStampSchemaObject stamp)
                return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Footprint", EditorStyles.boldLabel);

            if (!StampRasterizer.TryAccumulate(stamp.BuildShape(1f), out var grid, out _, out var size))
            {
                EditorGUILayout.HelpBox("Empty footprint for the current parameters.", MessageType.None);
                return;
            }

            BuildTexture(grid, size);

            var available = EditorGUIUtility.currentViewWidth - 40f;
            var cellPixels = math.clamp(available / size.x, 2f, 14f);
            var rect = GUILayoutUtility.GetRect(size.x * cellPixels, size.y * cellPixels, GUILayout.ExpandWidth(false));

            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill, true);

            EditorGUILayout.LabelField($"{size.x} x {size.y} cells", EditorStyles.miniLabel);
        }

        private void BuildTexture(int[] grid, int2 size)
        {
            if (preview == null || preview.width != size.x || preview.height != size.y)
            {
                if (preview != null)
                    DestroyImmediate(preview);

                preview = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            }

            var peak = 1;
            foreach (var value in grid)
                peak = math.max(peak, math.abs(value));

            for (var y = 0; y < size.y; y++)
            for (var x = 0; x < size.x; x++)
            {
                var value = grid[x + y * size.x];
                var intensity = math.abs(value) / (float)peak;
                var color = value == 0
                    ? new Color(0.16f, 0.16f, 0.18f, 1f)
                    : value > 0
                        ? Color.Lerp(new Color(0.2f, 0.18f, 0.1f), new Color(1f, 0.65f, 0.2f), intensity)
                        : Color.Lerp(new Color(0.1f, 0.14f, 0.2f), new Color(0.3f, 0.6f, 1f), intensity);

                preview.SetPixel(x, size.y - 1 - y, color);
            }

            preview.Apply();
        }

        private void UpdateVisibility()
        {
            if (!elements.TryGetValue(nameof(GridStampSchemaObject.Kind), out _))
                return;

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
            if (elements.TryGetValue(propertyName, out var element))
                ElementUtility.SetVisible(element, visible);
        }
    }
}