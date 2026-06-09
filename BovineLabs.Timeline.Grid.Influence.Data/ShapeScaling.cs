using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public static class ShapeScaling
    {
        public static InfluenceShape Inset(this InfluenceShape s, int d)
        {
            if (d <= 0)
                return s;

            var two = new int2(2 * d, 2 * d);
            var one = new int2(d, d);

            switch (s.Kind)
            {
                case ShapeKind.SolidRect:
                    return InfluenceShape.SolidRect(s.RectMin + one, s.RectSize - two, s.Weight);
                case ShapeKind.RectShell:
                    return InfluenceShape.RectShell(s.ShellMin + one, s.ShellSize - two, s.ShellThickness, s.Weight);
                case ShapeKind.RoundedRect:
                    return InfluenceShape.RoundedRect(s.RoundedRectMin + one, s.RoundedRectSize - two,
                        math.max(0, s.RoundedRectRadius - d), s.Weight);
                case ShapeKind.Disc:
                    return InfluenceShape.Disc(s.DiscCenter, s.DiscRadius - d, s.Weight);
                case ShapeKind.Annulus:
                    return InfluenceShape.Annulus(s.AnnulusCenter, s.AnnulusOuterRadius - d, s.AnnulusInnerRadius,
                        s.Weight);
                case ShapeKind.Capsule:
                    return InfluenceShape.Capsule(s.CapsuleStart, s.CapsuleEnd, s.CapsuleRadius - d, s.Weight);
                case ShapeKind.Ellipse:
                    return InfluenceShape.Ellipse(s.EllipseCenter, s.EllipseRadii - one, s.Weight);
                case ShapeKind.ThickLine:
                    return InfluenceShape.ThickLine(s.ThickLineStart, s.ThickLineEnd, s.ThickLineRadius - d, s.Weight);
                case ShapeKind.Sector:
                    return InfluenceShape.Sector(s.SectorCenter, s.SectorRadius - d, s.SectorDir0, s.SectorDir1,
                        s.Weight);
                default:
                    return s;
            }
        }
    }
}