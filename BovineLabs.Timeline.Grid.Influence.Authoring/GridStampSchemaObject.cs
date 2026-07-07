using System;
using System.Collections.Generic;
using BovineLabs.Core.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [AutoRef(nameof(InfluenceGridSettingsAuthoring), nameof(InfluenceGridSettingsAuthoring.Stamps), "GridStamp",
        "Schemas/GridStamps")]
    [CreateAssetMenu(menuName = "BovineLabs/Grid/Stamp Schema")]
    public class GridStampSchemaObject : ScriptableObject, IUID
    {
        private const int RayScale = 1024;

        // Extents (radii, rect/canvas sizes) are capped to bound per-frame chunk allocation; a huge radius
        // activates millions of chunks on the single-threaded prepare job. Endpoint coordinates are capped to
        // keep bounds sane (matching the PaintMin clamp).
        private const int MaxExtent = 512;
        private const int MaxCoord = 4096;

        [SerializeField] [InspectorReadOnly] private int id;

        public ShapeKind Kind = ShapeKind.Disc;

        [Tooltip(
            "Per-cell weight added to the field (plain integer, no x100 fixed point). Prefer >= 8-10: smaller weights quantize to 0 during clip blending and pop in/out.")]
        public int BaseWeight = 1;

        [Header("Solid Rect / Rect Shell")] public Vector2Int RectMin = new(-5, -5);

        [Tooltip("Rect size in cells. Clamped to 0..512 per axis to bound chunk allocation.")]
        public Vector2Int RectSize = new(10, 10);

        [Tooltip("Shell thickness in cells. Clamped to 1..512.")]
        public int ShellThickness = 1;

        [Header("Disc")] public Vector2Int DiscCenter = Vector2Int.zero;

        [Tooltip("Disc radius in cells. Clamped to 0..512 to bound chunk allocation.")]
        public int DiscRadius = 5;

        [Header("Annulus")] public Vector2Int AnnulusCenter = Vector2Int.zero;

        [Tooltip("Outer radius in cells. Clamped to 0..512 to bound chunk allocation.")]
        public int AnnulusOuterRadius = 5;

        public int AnnulusInnerRadius = 3;

        [Header("Capsule")]
        [Tooltip("Endpoint in cells, relative to the stamp origin. Clamped to +/-4096.")]
        public Vector2Int CapsuleStart = new(-3, 0);

        [Tooltip("Endpoint in cells, relative to the stamp origin. Clamped to +/-4096.")]
        public Vector2Int CapsuleEnd = new(3, 5);

        [Tooltip("Capsule radius in cells. Clamped to 0..512 to bound chunk allocation.")]
        public int CapsuleRadius = 5;

        [Header("Ellipse")] public Vector2Int EllipseCenter = Vector2Int.zero;

        [Tooltip("Ellipse radii in cells. Clamped to 0..512 per axis to bound chunk allocation.")]
        public Vector2Int EllipseRadii = new(5, 3);

        [Header("Rounded Rect")] public Vector2Int RoundedRectMin = new(-5, -3);

        [Tooltip("Rect size in cells. Clamped to 0..512 per axis to bound chunk allocation.")]
        public Vector2Int RoundedRectSize = new(10, 6);

        [Tooltip("Corner radius in cells. Clamped to 0..512.")]
        public int RoundedRectRadius = 2;

        [Header("Thick Line")]
        [Tooltip("Endpoint in cells, relative to the stamp origin. Clamped to +/-4096.")]
        public Vector2Int ThickLineStart = new(-5, 0);

        [Tooltip("Endpoint in cells, relative to the stamp origin. Clamped to +/-4096.")]
        public Vector2Int ThickLineEnd = new(5, 0);

        [Tooltip("Line radius in cells. Clamped to 0..512 to bound chunk allocation.")]
        public int ThickLineRadius = 1;

        [Header("Sector")] public Vector2Int SectorCenter = Vector2Int.zero;

        [Tooltip("Sector radius in cells. Clamped to 0..512 to bound chunk allocation.")]
        public int SectorRadius = 6;
        public float SectorFacingDegrees = 90f;
        [Range(1f, 90f)] public float SectorHalfAngleDegrees = 30f;

        [Header("Painted (freeform)")]
        [Tooltip("Bottom-left cell of the paint canvas, in cells, relative to the stamp origin.")]
        public Vector2Int PaintMin = new(-8, -8);

        [Tooltip("Paint canvas size in cells. Changing this clears the canvas.")]
        public Vector2Int PaintSize = new(16, 16);

        [Tooltip("Weight applied by a left-drag. Right-drag erases (0). Negative paints subtractive weight.")]
        public int PaintBrushWeight = 1;

        [SerializeField] [HideInInspector] private int[] paintWeights = Array.Empty<int>();
        [SerializeField] [HideInInspector] private int paintWidth;
        [SerializeField] [HideInInspector] private int paintHeight;

        public ushort Id => (ushort)id;

        public int[] PaintWeights => paintWeights;

        private void OnValidate()
        {
            RectSize = ClampExtent(RectSize);
            ShellThickness = math.clamp(ShellThickness, 1, MaxExtent);
            DiscRadius = math.clamp(DiscRadius, 0, MaxExtent);
            AnnulusOuterRadius = math.clamp(AnnulusOuterRadius, 0, MaxExtent);
            AnnulusInnerRadius = math.clamp(AnnulusInnerRadius, -1, AnnulusOuterRadius - 1);
            CapsuleRadius = math.clamp(CapsuleRadius, 0, MaxExtent);
            CapsuleStart = ClampCoord(CapsuleStart);
            CapsuleEnd = ClampCoord(CapsuleEnd);
            EllipseRadii = ClampExtent(EllipseRadii);
            RoundedRectSize = ClampExtent(RoundedRectSize);
            RoundedRectRadius = math.clamp(RoundedRectRadius, 0, MaxExtent);
            ThickLineRadius = math.clamp(ThickLineRadius, 0, MaxExtent);
            ThickLineStart = ClampCoord(ThickLineStart);
            ThickLineEnd = ClampCoord(ThickLineEnd);
            SectorRadius = math.clamp(SectorRadius, 0, MaxExtent);
            SectorHalfAngleDegrees = math.clamp(SectorHalfAngleDegrees, 1f, 90f);
            PaintMin = ClampCoord(PaintMin);
            EnsurePaintBuffer();
        }

        private static Vector2Int ClampExtent(Vector2Int v)
        {
            return new Vector2Int(math.clamp(v.x, 0, MaxExtent), math.clamp(v.y, 0, MaxExtent));
        }

        private static Vector2Int ClampCoord(Vector2Int v)
        {
            return new Vector2Int(math.clamp(v.x, -MaxCoord, MaxCoord), math.clamp(v.y, -MaxCoord, MaxCoord));
        }

        int IUID.ID
        {
            get => id;
            set => id = value;
        }

        public void EnsurePaintBuffer()
        {
            var sx = math.clamp(PaintSize.x, 1, 64);
            var sy = math.clamp(PaintSize.y, 1, 64);
            PaintSize = new Vector2Int(sx, sy);

            var needed = sx * sy;

            if ((paintWidth == 0 || paintHeight == 0) && paintWeights != null && paintWeights.Length == needed)
            {
                paintWidth = sx;
                paintHeight = sy;
                return;
            }

            if (paintWeights != null && paintWeights.Length == needed && paintWidth == sx && paintHeight == sy)
                return;

            var old = paintWeights;
            var resized = new int[needed];
            if (old != null && paintWidth > 0 && paintHeight > 0 && old.Length == paintWidth * paintHeight)
            {
                var copyW = math.min(paintWidth, sx);
                var copyH = math.min(paintHeight, sy);
                for (var y = 0; y < copyH; y++)
                for (var x = 0; x < copyW; x++)
                    resized[x + y * sx] = old[x + y * paintWidth];
            }

            paintWeights = resized;
            paintWidth = sx;
            paintHeight = sy;
        }

        public void BuildShapes(float weightMultiplier, List<InfluenceShape> into)
        {
            if (Kind == ShapeKind.Painted)
            {
                BuildPainted(weightMultiplier, into);
                return;
            }

            into.Add(BuildShape(weightMultiplier));
        }

        private void BuildPainted(float weightMultiplier, List<InfluenceShape> into)
        {
            EnsurePaintBuffer();

            var sx = PaintSize.x;
            var sy = PaintSize.y;
            int2 min = new(PaintMin.x, PaintMin.y);

            for (var y = 0; y < sy; y++)
            {
                var x = 0;
                while (x < sx)
                {
                    var raw = paintWeights[x + y * sx];
                    if (raw == 0)
                    {
                        x++;
                        continue;
                    }

                    var runStart = x;
                    x++;
                    while (x < sx && paintWeights[x + y * sx] == raw)
                        x++;

                    var weight = (int)math.round(raw * weightMultiplier);
                    if (weight != 0)
                        into.Add(InfluenceShape.SolidRect(
                            new int2(min.x + runStart, min.y + y), new int2(x - runStart, 1), weight));
                }
            }
        }

        public InfluenceShape BuildShape(float weightMultiplier)
        {
            var weight = (int)math.round(BaseWeight * weightMultiplier);

            int2 rectMin = new(RectMin.x, RectMin.y);
            int2 rectSize = new(RectSize.x, RectSize.y);
            int2 discCenter = new(DiscCenter.x, DiscCenter.y);
            int2 annulusCenter = new(AnnulusCenter.x, AnnulusCenter.y);
            int2 capsuleStart = new(CapsuleStart.x, CapsuleStart.y);
            int2 capsuleEnd = new(CapsuleEnd.x, CapsuleEnd.y);
            int2 ellipseCenter = new(EllipseCenter.x, EllipseCenter.y);
            int2 ellipseRadii = new(EllipseRadii.x, EllipseRadii.y);
            int2 roundedRectMin = new(RoundedRectMin.x, RoundedRectMin.y);
            int2 roundedRectSize = new(RoundedRectSize.x, RoundedRectSize.y);
            int2 thickLineStart = new(ThickLineStart.x, ThickLineStart.y);
            int2 thickLineEnd = new(ThickLineEnd.x, ThickLineEnd.y);
            int2 sectorCenter = new(SectorCenter.x, SectorCenter.y);

            return Kind switch
            {
                ShapeKind.SolidRect => InfluenceShape.SolidRect(rectMin, rectSize, weight),
                ShapeKind.RectShell => InfluenceShape.RectShell(rectMin, rectSize, ShellThickness, weight),
                ShapeKind.Disc => InfluenceShape.Disc(discCenter, DiscRadius, weight),
                ShapeKind.Annulus => InfluenceShape.Annulus(annulusCenter, AnnulusOuterRadius, AnnulusInnerRadius,
                    weight),
                ShapeKind.Capsule => InfluenceShape.Capsule(capsuleStart, capsuleEnd, CapsuleRadius, weight),
                ShapeKind.Ellipse => InfluenceShape.Ellipse(ellipseCenter, ellipseRadii, weight),
                ShapeKind.RoundedRect => InfluenceShape.RoundedRect(roundedRectMin, roundedRectSize, RoundedRectRadius,
                    weight),
                ShapeKind.ThickLine => InfluenceShape.ThickLine(thickLineStart, thickLineEnd, ThickLineRadius, weight),
                ShapeKind.Sector => InfluenceShape.Sector(sectorCenter, SectorRadius,
                    Ray(SectorFacingDegrees - SectorHalfAngleDegrees),
                    Ray(SectorFacingDegrees + SectorHalfAngleDegrees), weight),

                ShapeKind.Painted => InfluenceShape.SolidRect(
                    new int2(PaintMin.x, PaintMin.y),
                    new int2(math.max(0, PaintSize.x), math.max(0, PaintSize.y)), weight),
                _ => InfluenceShape.Disc(discCenter, DiscRadius, weight)
            };
        }

        private static int2 Ray(float degrees)
        {
            math.sincos(math.radians(degrees), out var sin, out var cos);
            return new int2((int)math.round(RayScale * cos), (int)math.round(RayScale * sin));
        }
    }
}