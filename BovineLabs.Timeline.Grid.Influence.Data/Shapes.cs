using Unity.Burst;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public enum ShapeKind : byte
    {
        SolidRect,
        RectShell,
        Disc,
        Annulus,
        Capsule
    }
    
    [BurstCompile]
    public readonly struct InfluenceShape
    {
        public readonly ShapeKind Kind;
        public readonly int Weight;
        readonly int2 _v0;
        readonly int2 _v1;
        readonly int _i0;
        readonly int _i1;

        InfluenceShape(ShapeKind kind, int weight, int2 v0, int2 v1, int i0, int i1)
        {
            Kind = kind;
            Weight = weight;
            _v0 = v0;
            _v1 = v1;
            _i0 = i0;
            _i1 = i1;
        }

        public int2 RectMin => _v0;
        public int2 RectSize => _v1;

        public int2 ShellMin => _v0;
        public int2 ShellSize => _v1;
        public int ShellThickness => _i0;

        public int2 DiscCenter => _v0;
        public int DiscRadius => _i0;

        public int2 AnnulusCenter => _v0;
        public int AnnulusOuter => _i0;
        public int AnnulusInner => _i1;

        public int2 CapsuleA => _v0;
        public int2 CapsuleB => _v1;
        public int CapsuleRadius => _i0;

        public InfluenceShape WithWeight(int weight)
            => new InfluenceShape(Kind, weight, _v0, _v1, _i0, _i1);

        public static InfluenceShape SolidRect(int2 min, int2 size, int weight)
            => new InfluenceShape(ShapeKind.SolidRect, weight, min, size, 0, 0);

        public static InfluenceShape RectShell(int2 min, int2 size, int thickness, int weight)
            => new InfluenceShape(ShapeKind.RectShell, weight, min, size, thickness, 0);

        public static InfluenceShape Disc(int2 center, int radius, int weight)
            => new InfluenceShape(ShapeKind.Disc, weight, center, int2.zero, radius, 0);

        public static InfluenceShape Annulus(int2 center, int outerRadius, int innerRadius, int weight)
            => new InfluenceShape(ShapeKind.Annulus, weight, center, int2.zero, outerRadius, innerRadius);

        public static InfluenceShape Capsule(int2 a, int2 b, int radius, int weight)
            => new InfluenceShape(ShapeKind.Capsule, weight, a, b, radius, 0);
    }

    [BurstCompile]
    public readonly struct Stamp
    {
        public readonly InfluenceShape Shape;
        public readonly int2 Origin;

        public Stamp(InfluenceShape shape, int2 origin)
        {
            Shape = shape;
            Origin = origin;
        }

        public Stamp Negated() => new Stamp(Shape.WithWeight(-Shape.Weight), Origin);
    }
}