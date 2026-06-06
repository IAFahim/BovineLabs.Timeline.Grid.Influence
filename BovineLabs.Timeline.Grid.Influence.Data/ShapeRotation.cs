using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public enum Quarter : byte
    {
        R0,
        R90,
        R180,
        R270
    }

    public static class ShapeRotation
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 RotatePoint(int2 p, Quarter q)
        {
            return q switch
            {
                Quarter.R90 => new int2(p.y, -p.x),
                Quarter.R180 => new int2(-p.x, -p.y),
                Quarter.R270 => new int2(-p.y, p.x),
                _ => p
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 RotateExtent(int2 size, Quarter q)
        {
            return q is Quarter.R90 or Quarter.R270 ? new int2(size.y, size.x) : size;
        }

        public static InfluenceShape Rotated(this InfluenceShape s, Quarter q)
        {
            if (q == Quarter.R0)
                return s;

            switch (s.Kind)
            {
                case ShapeKind.SolidRect:
                {
                    var (min, size) = RotateRect(s.RectMin, s.RectSize, q);
                    return InfluenceShape.SolidRect(min, size, s.Weight);
                }
                case ShapeKind.RectShell:
                {
                    var (min, size) = RotateRect(s.ShellMin, s.ShellSize, q);
                    return InfluenceShape.RectShell(min, size, s.ShellThickness, s.Weight);
                }
                case ShapeKind.RoundedRect:
                {
                    var (min, size) = RotateRect(s.RoundedRectMin, s.RoundedRectSize, q);
                    return InfluenceShape.RoundedRect(min, size, s.RoundedRectRadius, s.Weight);
                }
                case ShapeKind.Disc:
                    return InfluenceShape.Disc(RotatePoint(s.DiscCenter, q), s.DiscRadius, s.Weight);
                case ShapeKind.Annulus:
                    return InfluenceShape.Annulus(RotatePoint(s.AnnulusCenter, q), s.AnnulusOuterRadius,
                        s.AnnulusInnerRadius, s.Weight);
                case ShapeKind.Capsule:
                    return InfluenceShape.Capsule(RotatePoint(s.CapsuleStart, q), RotatePoint(s.CapsuleEnd, q),
                        s.CapsuleRadius, s.Weight);
                case ShapeKind.Ellipse:
                    return InfluenceShape.Ellipse(RotatePoint(s.EllipseCenter, q), RotateExtent(s.EllipseRadii, q),
                        s.Weight);
                case ShapeKind.ThickLine:
                    return InfluenceShape.ThickLine(RotatePoint(s.ThickLineStart, q), RotatePoint(s.ThickLineEnd, q),
                        s.ThickLineRadius, s.Weight);
                default:
                    return s;
            }
        }

        private static (int2 Min, int2 Size) RotateRect(int2 min, int2 size, Quarter q)
        {
            var maxInclusive = min + size - new int2(1, 1);
            var a = RotatePoint(min, q);
            var b = RotatePoint(maxInclusive, q);
            var newMin = math.min(a, b);
            var newMax = math.max(a, b);
            return (newMin, newMax - newMin + new int2(1, 1));
        }
    }
}