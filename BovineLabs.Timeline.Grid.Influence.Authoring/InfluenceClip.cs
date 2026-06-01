using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    public sealed class InfluenceClip : DOTSClip, ITimelineClipAsset
    {
        [UnityEngine.Header("Shape Configuration")]
        [UnityEngine.Tooltip("The type of shape to apply to the grid.")]
        public ShapeKind kind = ShapeKind.Disc;

        [UnityEngine.Tooltip("Weight for this influence stamp. Positive values add, negative subtract.")]
        public int weight = 1;

        [UnityEngine.Header("Solid Rect / Rect Shell")]
        [UnityEngine.Tooltip("Minimum corner of the rectangle (grid coordinates).")]
        public UnityEngine.Vector2Int rectMin = new UnityEngine.Vector2Int(-5, -5);

        [UnityEngine.Tooltip("Size of the rectangle in grid cells.")]
        public UnityEngine.Vector2Int rectSize = new UnityEngine.Vector2Int(10, 10);

        [UnityEngine.Tooltip("Thickness of the shell for RectShell shapes.")]
        public int shellThickness = 1;

        [UnityEngine.Header("Disc / Annulus")]
        [UnityEngine.Tooltip("Center of the circle (grid coordinates).")]
        public UnityEngine.Vector2Int circleCenter = UnityEngine.Vector2Int.zero;

        [UnityEngine.Tooltip("Outer radius in grid cells.")]
        public int outerRadius = 5;

        [UnityEngine.Tooltip("Inner radius for annulus shapes (must be less than outerRadius).")]
        public int innerRadius = 3;

        [UnityEngine.Header("Capsule")]
        [UnityEngine.Tooltip("First endpoint of the capsule (grid coordinates).")]
        public UnityEngine.Vector2Int capsuleA = new UnityEngine.Vector2Int(-3, 0);

        [UnityEngine.Tooltip("Second endpoint of the capsule (grid coordinates).")]
        public UnityEngine.Vector2Int capsuleB = new UnityEngine.Vector2Int(3, 5);

        [UnityEngine.Header("Transform")]
        [UnityEngine.Tooltip("Local offset from the entity's world position.")]
        public UnityEngine.Vector3 localOffset;

        public override double duration => 1.0;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            InfluenceShape shape = default;

            switch (kind)
            {
                case ShapeKind.SolidRect:
                    shape = InfluenceShape.SolidRect(
                        new int2(rectMin.x, rectMin.y),
                        new int2(rectSize.x, rectSize.y),
                        weight);
                    break;

                case ShapeKind.RectShell:
                    shape = InfluenceShape.RectShell(
                        new int2(rectMin.x, rectMin.y),
                        new int2(rectSize.x, rectSize.y),
                        shellThickness,
                        weight);
                    break;

                case ShapeKind.Disc:
                    shape = InfluenceShape.Disc(
                        new int2(circleCenter.x, circleCenter.y),
                        outerRadius,
                        weight);
                    break;

                case ShapeKind.Annulus:
                    shape = InfluenceShape.Annulus(
                        new int2(circleCenter.x, circleCenter.y),
                        outerRadius,
                        innerRadius,
                        weight);
                    break;

                case ShapeKind.Capsule:
                    shape = InfluenceShape.Capsule(
                        new int2(capsuleA.x, capsuleA.y),
                        new int2(capsuleB.x, capsuleB.y),
                        outerRadius,
                        weight);
                    break;
            }

            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                Shape = shape,
                LocalOffset = localOffset
            });

            base.Bake(clipEntity, context);
        }
    }
}
