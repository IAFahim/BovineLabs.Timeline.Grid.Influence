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

        [UnityEngine.Tooltip("Capsule radius in grid cells.")]
        public int capsuleRadius = 5;

        [UnityEngine.Header("Transform")]
        [UnityEngine.Tooltip("Local offset from the entity's world position.")]
        public UnityEngine.Vector3 localOffset;

        public override double duration => 1.0;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                Shape = BuildShape(),
                LocalOffset = localOffset
            });

            base.Bake(clipEntity, context);
        }

        InfluenceShape BuildShape()
        {
            int2 rectMinCell = new int2(rectMin.x, rectMin.y);
            int2 rectSizeCell = new int2(rectSize.x, rectSize.y);
            int2 circleCenterCell = new int2(circleCenter.x, circleCenter.y);
            int2 capsuleACell = new int2(capsuleA.x, capsuleA.y);
            int2 capsuleBCell = new int2(capsuleB.x, capsuleB.y);

            switch (kind)
            {
                case ShapeKind.SolidRect:
                    return InfluenceShape.SolidRect(rectMinCell, rectSizeCell, weight);

                case ShapeKind.RectShell:
                    return InfluenceShape.RectShell(rectMinCell, rectSizeCell, shellThickness, weight);

                case ShapeKind.Disc:
                    return InfluenceShape.Disc(circleCenterCell, outerRadius, weight);

                case ShapeKind.Annulus:
                    return InfluenceShape.Annulus(circleCenterCell, outerRadius, innerRadius, weight);

                case ShapeKind.Capsule:
                    return InfluenceShape.Capsule(capsuleACell, capsuleBCell, capsuleRadius, weight);

                default:
                    return InfluenceShape.Disc(circleCenterCell, outerRadius, weight);
            }
        }
    }
}