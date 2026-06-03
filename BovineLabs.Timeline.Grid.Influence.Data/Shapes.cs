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
        ThickLine
    }

    public readonly struct InfluenceShape
    {
        public readonly ShapeKind Kind;
        public readonly int Weight;
        readonly int2 _a;
        readonly int2 _b;
        readonly int _p;
        readonly int _q;

        InfluenceShape(ShapeKind kind, int weight, int2 a, int2 b, int p, int q)
        {
            Kind = kind;
            Weight = weight;
            _a = a;
            _b = b;
            _p = p;
            _q = q;
        }

        public int2 RectMin => _a;
        public int2 RectSize => _b;

        public int2 ShellMin => _a;
        public int2 ShellSize => _b;
        public int ShellThickness => _p;

        public int2 DiscCenter => _a;
        public int DiscRadius => _p;

        public int2 AnnulusCenter => _a;
        public int AnnulusOuterRadius => _p;
        public int AnnulusInnerRadius => _q;

        public int2 CapsuleStart => _a;
        public int2 CapsuleEnd => _b;
        public int CapsuleRadius => _p;

        public int2 EllipseCenter => _a;
        public int2 EllipseRadii => _b;

        public int2 RoundedRectMin => _a;
        public int2 RoundedRectSize => _b;
        public int RoundedRectRadius => _p;

        public int2 ThickLineStart => _a;
        public int2 ThickLineEnd => _b;
        public int ThickLineRadius => _p;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public InfluenceShape WithWeight(int weight) => new(Kind, weight, _a, _b, _p, _q);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape SolidRect(int2 min, int2 size, int weight)
            => new(ShapeKind.SolidRect, weight, min, size, 0, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape RectShell(int2 min, int2 size, int thickness, int weight)
            => new(ShapeKind.RectShell, weight, min, size, thickness, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Disc(int2 center, int radius, int weight)
            => new(ShapeKind.Disc, weight, center, int2.zero, radius, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Annulus(int2 center, int outerRadius, int innerRadius, int weight)
            => new(ShapeKind.Annulus, weight, center, int2.zero, outerRadius, innerRadius);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Capsule(int2 start, int2 end, int radius, int weight)
            => new(ShapeKind.Capsule, weight, start, end, radius, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape Ellipse(int2 center, int2 radii, int weight)
            => new(ShapeKind.Ellipse, weight, center, radii, 0, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape RoundedRect(int2 min, int2 size, int radius, int weight)
            => new(ShapeKind.RoundedRect, weight, min, size, radius, 0);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static InfluenceShape ThickLine(int2 start, int2 end, int radius, int weight)
            => new(ShapeKind.ThickLine, weight, start, end, radius, 0);
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
        public Stamp Negated() => new(Shape.WithWeight(-Shape.Weight), Origin);
    }
}
