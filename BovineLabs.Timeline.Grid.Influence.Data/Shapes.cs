using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public enum ShapeKind : byte
    {
        SolidRect,
        RectShell,
        Disc,
        Annulus,
        Capsule,

        Ellipse,
        RoundedRect,
        ThickLine,

        Sector
    }

    public readonly struct InfluenceShape
    {
        public readonly ShapeKind Kind;
        public readonly int Weight;

        private InfluenceShape(ShapeKind kind, int weight, int2 a, int2 b, int p, int q, int2 aux)
        {
            Kind = kind;
            Weight = weight;
            RectMin = a;
            RectSize = b;
            ShellThickness = p;
            AnnulusInnerRadius = q;
            SectorDir1 = aux;
        }

        public int2 RectMin { get; }

        public int2 RectSize { get; }

        public int2 ShellMin => RectMin;
        public int2 ShellSize => RectSize;
        public int ShellThickness { get; }

        public int2 DiscCenter => RectMin;
        public int DiscRadius => ShellThickness;

        public int2 AnnulusCenter => RectMin;
        public int AnnulusOuterRadius => ShellThickness;
        public int AnnulusInnerRadius { get; }

        public int2 CapsuleStart => RectMin;
        public int2 CapsuleEnd => RectSize;
        public int CapsuleRadius => ShellThickness;

        public int2 EllipseCenter => RectMin;
        public int2 EllipseRadii => RectSize;

        public int2 RoundedRectMin => RectMin;
        public int2 RoundedRectSize => RectSize;
        public int RoundedRectRadius => ShellThickness;

        public int2 ThickLineStart => RectMin;
        public int2 ThickLineEnd => RectSize;
        public int ThickLineRadius => ShellThickness;

        public int2 SectorCenter => RectMin;
        public int2 SectorDir0 => RectSize;
        public int SectorRadius => ShellThickness;
        public int2 SectorDir1 { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InfluenceShape WithWeight(int weight)
        {
            return new InfluenceShape(Kind, weight, RectMin, RectSize, ShellThickness, AnnulusInnerRadius, SectorDir1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape SolidRect(int2 min, int2 size, int weight)
        {
            return new InfluenceShape(ShapeKind.SolidRect, weight, min, size, 0, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape RectShell(int2 min, int2 size, int thickness, int weight)
        {
            return new InfluenceShape(ShapeKind.RectShell, weight, min, size, thickness, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Disc(int2 center, int radius, int weight)
        {
            return new InfluenceShape(ShapeKind.Disc, weight, center, int2.zero, radius, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Annulus(int2 center, int outerRadius, int innerRadius, int weight)
        {
            return new InfluenceShape(ShapeKind.Annulus, weight, center, int2.zero, outerRadius, innerRadius,
                int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Capsule(int2 start, int2 end, int radius, int weight)
        {
            return new InfluenceShape(ShapeKind.Capsule, weight, start, end, radius, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Ellipse(int2 center, int2 radii, int weight)
        {
            return new InfluenceShape(ShapeKind.Ellipse, weight, center, radii, 0, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape RoundedRect(int2 min, int2 size, int radius, int weight)
        {
            return new InfluenceShape(ShapeKind.RoundedRect, weight, min, size, radius, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape ThickLine(int2 start, int2 end, int radius, int weight)
        {
            return new InfluenceShape(ShapeKind.ThickLine, weight, start, end, radius, 0, int2.zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Sector(int2 center, int radius, int2 dir0, int2 dir1, int weight)
        {
            return new InfluenceShape(ShapeKind.Sector, weight, center, dir0, radius, 0, dir1);
        }
    }

    public readonly struct Stamp
    {
        public readonly InfluenceShape Shape;
        public readonly int2 Origin;

        public Stamp(InfluenceShape shape, int2 origin)
        {
            Shape = shape;
            Origin = origin;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Stamp Negated()
        {
            return new Stamp(Shape.WithWeight(-Shape.Weight), Origin);
        }
    }
}