using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [System.Serializable]
    public sealed class InfluenceClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Shape")]
        public ShapeKind Kind = ShapeKind.Disc;
        public int Weight = 1;

        [Header("Solid Rect / Rect Shell")]
        public Vector2Int RectMin = new(-5, -5);
        public Vector2Int RectSize = new(10, 10);
        public int ShellThickness = 1;

        [Header("Disc")]
        public Vector2Int DiscCenter = Vector2Int.zero;
        public int DiscRadius = 5;

        [Header("Annulus")]
        public Vector2Int AnnulusCenter = Vector2Int.zero;
        public int AnnulusOuterRadius = 5;
        public int AnnulusInnerRadius = 3;

        [Header("Capsule")]
        public Vector2Int CapsuleStart = new(-3, 0);
        public Vector2Int CapsuleEnd = new(3, 5);
        public int CapsuleRadius = 5;

        [Header("Transform")]
        public Vector3 LocalOffset;

        public override double duration => 1.0;

        public ClipCaps clipCaps => ClipCaps.None;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                Shape = BuildShape(),
                LocalOffset = LocalOffset
            });

            base.Bake(clipEntity, context);
        }

        InfluenceShape BuildShape()
        {
            int2 rectMin = new(RectMin.x, RectMin.y);
            int2 rectSize = new(RectSize.x, RectSize.y);
            int2 discCenter = new(DiscCenter.x, DiscCenter.y);
            int2 annulusCenter = new(AnnulusCenter.x, AnnulusCenter.y);
            int2 capsuleStart = new(CapsuleStart.x, CapsuleStart.y);
            int2 capsuleEnd = new(CapsuleEnd.x, CapsuleEnd.y);

            return Kind switch
            {
                ShapeKind.SolidRect => InfluenceShape.SolidRect(rectMin, rectSize, Weight),
                ShapeKind.RectShell => InfluenceShape.RectShell(rectMin, rectSize, ShellThickness, Weight),
                ShapeKind.Disc => InfluenceShape.Disc(discCenter, DiscRadius, Weight),
                ShapeKind.Annulus => InfluenceShape.Annulus(annulusCenter, AnnulusOuterRadius, AnnulusInnerRadius, Weight),
                ShapeKind.Capsule => InfluenceShape.Capsule(capsuleStart, capsuleEnd, CapsuleRadius, Weight),
                _ => InfluenceShape.Disc(discCenter, DiscRadius, Weight)
            };
        }

        void OnValidate()
        {
            RectSize = new Vector2Int(math.max(0, RectSize.x), math.max(0, RectSize.y));
            ShellThickness = math.max(1, ShellThickness);
            DiscRadius = math.max(0, DiscRadius);
            AnnulusOuterRadius = math.max(0, AnnulusOuterRadius);
            AnnulusInnerRadius = math.clamp(AnnulusInnerRadius, -1, AnnulusOuterRadius - 1);
            CapsuleRadius = math.max(0, CapsuleRadius);
        }
    }
}
