using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe ref struct SpanSink
    {
        private readonly WeightedRect* _spans;
        private readonly int _capacity;

        public SpanSink(WeightedRect* spans, int capacity)
        {
            _spans = spans;
            _capacity = capacity;
            Count = 0;
        }

        public int Count { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(in WeightedRect span)
        {
            if ((uint)Count < (uint)_capacity)
            {
                _spans[Count] = span;
                Count++;
            }
        }

        public void SealRemaining()
        {
            while (Count < _capacity)
            {
                _spans[Count] = WeightedRect.Empty;
                Count++;
            }
        }
    }

    public static class Rasterizer
    {
        public static CellRect Bounds(in InfluenceShape shape, int2 origin)
        {
            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                {
                    if (shape.RectSize.x <= 0 || shape.RectSize.y <= 0) return CellRect.Empty;

                    var min = origin + shape.RectMin;
                    return new CellRect(min, min + shape.RectSize);
                }

                case ShapeKind.RectShell:
                {
                    if (shape.ShellSize.x <= 0 || shape.ShellSize.y <= 0 || shape.ShellThickness <= 0)
                        return CellRect.Empty;

                    var min = origin + shape.ShellMin;
                    return new CellRect(min, min + shape.ShellSize);
                }

                case ShapeKind.Disc:
                {
                    if (shape.DiscRadius < 0) return CellRect.Empty;

                    var center = origin + shape.DiscCenter;
                    var r = shape.DiscRadius;
                    return new CellRect(center - new int2(r, r), center + new int2(r + 1, r + 1));
                }

                case ShapeKind.Annulus:
                {
                    if (shape.AnnulusOuterRadius < 0 || shape.AnnulusInnerRadius >= shape.AnnulusOuterRadius)
                        return CellRect.Empty;

                    var center = origin + shape.AnnulusCenter;
                    var r = shape.AnnulusOuterRadius;
                    return new CellRect(center - new int2(r, r), center + new int2(r + 1, r + 1));
                }

                case ShapeKind.Capsule:
                {
                    if (shape.CapsuleRadius < 0) return CellRect.Empty;

                    var r = shape.CapsuleRadius;
                    var a = origin + shape.CapsuleStart;
                    var b = origin + shape.CapsuleEnd;
                    return new CellRect(math.min(a, b) - new int2(r, r), math.max(a, b) + new int2(r + 1, r + 1));
                }

                case ShapeKind.Ellipse:
                {
                    if (shape.EllipseRadii.x < 0 || shape.EllipseRadii.y < 0) return CellRect.Empty;

                    var center = origin + shape.EllipseCenter;
                    var radii = shape.EllipseRadii;
                    return new CellRect(center - radii, center + radii + new int2(1, 1));
                }

                case ShapeKind.RoundedRect:
                {
                    if (shape.RoundedRectSize.x <= 0 || shape.RoundedRectSize.y <= 0 || shape.RoundedRectRadius < 0)
                        return CellRect.Empty;

                    var min = origin + shape.RoundedRectMin;
                    return new CellRect(min, min + shape.RoundedRectSize);
                }

                case ShapeKind.ThickLine:
                {
                    if (shape.ThickLineRadius < 0) return CellRect.Empty;

                    var r = shape.ThickLineRadius;
                    var a = origin + shape.ThickLineStart;
                    var b = origin + shape.ThickLineEnd;
                    return new CellRect(math.min(a, b) - new int2(r, r), math.max(a, b) + new int2(r + 1, r + 1));
                }

                case ShapeKind.Sector:
                {
                    if (shape.SectorRadius < 0) return CellRect.Empty;

                    var center = origin + shape.SectorCenter;
                    var r = shape.SectorRadius;
                    return new CellRect(center - new int2(r, r), center + new int2(r + 1, r + 1));
                }

                default:
                    return CellRect.Empty;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EstimateSpanCount(in InfluenceShape shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                    return shape.RectSize.x > 0 && shape.RectSize.y > 0 ? 1 : 0;

                case ShapeKind.RectShell:
                {
                    if (shape.ShellSize.x <= 0 || shape.ShellSize.y <= 0 || shape.ShellThickness <= 0) return 0;

                    var inner = shape.ShellSize - new int2(2 * shape.ShellThickness, 2 * shape.ShellThickness);
                    return inner.x > 0 && inner.y > 0 ? 2 : 1;
                }

                case ShapeKind.Disc:
                    return DiscRowCount(shape.DiscRadius);

                case ShapeKind.Annulus:
                {
                    if (shape.AnnulusOuterRadius < 0 || shape.AnnulusInnerRadius >= shape.AnnulusOuterRadius) return 0;

                    var inner = shape.AnnulusInnerRadius >= 0 ? 2 * shape.AnnulusInnerRadius + 1 : 0;
                    return IntegerMath.SaturatingAdd(2 * shape.AnnulusOuterRadius + 1, inner);
                }

                case ShapeKind.Capsule:
                {
                    if (shape.CapsuleRadius < 0) return 0;

                    var spanY = math.abs((long)shape.CapsuleEnd.y - shape.CapsuleStart.y);
                    return IntegerMath.ClampToInt(spanY + 2L * shape.CapsuleRadius + 3L);
                }

                case ShapeKind.Ellipse:
                {
                    if (shape.EllipseRadii.x < 0 || shape.EllipseRadii.y < 0) return 0;

                    return IntegerMath.ClampToInt(2L * shape.EllipseRadii.y + 1L);
                }

                case ShapeKind.RoundedRect:
                {
                    if (shape.RoundedRectSize.x <= 0 || shape.RoundedRectSize.y <= 0 ||
                        shape.RoundedRectRadius < 0) return 0;

                    return shape.RoundedRectSize.y;
                }

                case ShapeKind.ThickLine:
                {
                    if (shape.ThickLineRadius < 0) return 0;

                    var spanY = math.abs((long)shape.ThickLineEnd.y - shape.ThickLineStart.y);
                    return IntegerMath.ClampToInt(spanY + 2L * shape.ThickLineRadius + 3L);
                }

                case ShapeKind.Sector:
                    return shape.SectorRadius < 0 ? 0 : IntegerMath.ClampToInt(2L * shape.SectorRadius + 1L);

                default:
                    return 0;
            }
        }

        public static void Emit(in Stamp stamp, ref SpanSink sink)
        {
            var shape = stamp.Shape;
            var origin = stamp.Origin;
            var weight = shape.Weight;

            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                    EmitRect(ref sink, origin + shape.RectMin, shape.RectSize, weight);
                    break;

                case ShapeKind.RectShell:
                    EmitShell(ref sink, origin + shape.ShellMin, shape.ShellSize, shape.ShellThickness, weight);
                    break;

                case ShapeKind.Disc:
                    EmitDisc(ref sink, origin + shape.DiscCenter, shape.DiscRadius, weight);
                    break;

                case ShapeKind.Annulus:
                    EmitDisc(ref sink, origin + shape.AnnulusCenter, shape.AnnulusOuterRadius, weight);
                    if (shape.AnnulusInnerRadius >= 0)
                        EmitDisc(ref sink, origin + shape.AnnulusCenter, shape.AnnulusInnerRadius, -weight);

                    break;

                case ShapeKind.Capsule:
                    EmitCapsule(ref sink, origin + shape.CapsuleStart, origin + shape.CapsuleEnd, shape.CapsuleRadius,
                        weight);
                    break;

                case ShapeKind.Ellipse:
                    EmitEllipse(ref sink, origin + shape.EllipseCenter, shape.EllipseRadii, weight);
                    break;

                case ShapeKind.RoundedRect:
                    EmitRoundedRect(ref sink, origin + shape.RoundedRectMin, shape.RoundedRectSize,
                        shape.RoundedRectRadius, weight);
                    break;

                case ShapeKind.ThickLine:
                    EmitCapsule(ref sink, origin + shape.ThickLineStart, origin + shape.ThickLineEnd,
                        shape.ThickLineRadius, weight);
                    break;

                case ShapeKind.Sector:
                    EmitSector(ref sink, origin + shape.SectorCenter, shape.SectorRadius,
                        shape.SectorDir0, shape.SectorDir1, weight);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DiscRowCount(int radius)
        {
            return radius < 0 ? 0 : IntegerMath.ClampToInt(2L * radius + 1L);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitRect(ref SpanSink sink, int2 min, int2 size, int weight)
        {
            if (size.x <= 0 || size.y <= 0) return;

            sink.Push(new WeightedRect(new CellRect(min, min + size), weight));
        }

        private static void EmitShell(ref SpanSink sink, int2 min, int2 size, int thickness, int weight)
        {
            if (size.x <= 0 || size.y <= 0 || thickness <= 0) return;

            EmitRect(ref sink, min, size, weight);
            var innerMin = min + new int2(thickness, thickness);
            var innerSize = size - new int2(2 * thickness, 2 * thickness);
            EmitRect(ref sink, innerMin, innerSize, -weight);
        }

        private static void EmitDisc(ref SpanSink sink, int2 center, int radius, int weight)
        {
            if (radius < 0) return;
            var r2 = (long)radius * radius;
            var half = 0;
            for (var dy = -radius; dy <= 0; dy++)
            {
                var rem = r2 - (long)dy * dy;
                while ((long)(half + 1) * (half + 1) <= rem) half++;
                var y = center.y + dy;
                sink.Push(new WeightedRect(
                    new CellRect(new int2(center.x - half, y), new int2(center.x + half + 1, y + 1)),
                    weight));
            }

            for (var dy = 1; dy <= radius; dy++)
            {
                var rem = r2 - (long)dy * dy;
                while ((long)half * half > rem) half--;
                var y = center.y + dy;
                sink.Push(new WeightedRect(
                    new CellRect(new int2(center.x - half, y), new int2(center.x + half + 1, y + 1)),
                    weight));
            }
        }

        private static void EmitCapsule(ref SpanSink sink, int2 a, int2 b, int radius, int weight)
        {
            if (radius < 0) return;

            var axis = b - a;
            var axisLengthSquared = (long)axis.x * axis.x + (long)axis.y * axis.y;
            var radiusSquared = (long)radius * radius;

            var loLocalY = math.min(0, axis.y) - radius;
            var hiLocalY = math.max(0, axis.y) + radius;
            var loLocalX = math.min(0, axis.x) - radius;
            var hiLocalX = math.max(0, axis.x) + radius;

            for (var localY = loLocalY; localY <= hiLocalY; localY++)
            {
                var seedX = math.clamp(SeedLocalX(localY, axis), loLocalX, hiLocalX);

                if (!Covered(seedX, localY, axis, axisLengthSquared, radiusSquared)) continue;

                var leftX = LeftmostCovered(loLocalX, seedX, localY, axis, axisLengthSquared, radiusSquared);
                var rightX = RightmostCovered(seedX, hiLocalX, localY, axis, axisLengthSquared, radiusSquared);

                var worldY = a.y + localY;
                sink.Push(new WeightedRect(
                    new CellRect(new int2(a.x + leftX, worldY), new int2(a.x + rightX + 1, worldY + 1)),
                    weight));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int SeedLocalX(int localY, int2 axis)
        {
            var loY = math.min(0, axis.y);
            var hiY = math.max(0, axis.y);

            if (axis.y != 0 && localY >= loY && localY <= hiY) return (int)((long)axis.x * localY / axis.y);

            return math.abs(localY) <= math.abs(localY - axis.y) ? 0 : axis.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Covered(int px, int py, int2 axis, long axisLengthSquared, long radiusSquared)
        {
            var projection = (long)px * axis.x + (long)py * axis.y;
            var distanceToStart = (long)px * px + (long)py * py;

            if (axisLengthSquared == 0 || projection <= 0) return distanceToStart <= radiusSquared;

            if (projection >= axisLengthSquared)
            {
                long qx = px - axis.x;
                long qy = py - axis.y;
                return qx * qx + qy * qy <= radiusSquared;
            }

            return distanceToStart * axisLengthSquared - projection * projection <= radiusSquared * axisLengthSquared;
        }

        private static int LeftmostCovered(int lo, int hi, int localY, int2 axis, long axisLengthSquared,
            long radiusSquared)
        {
            while (lo < hi)
            {
                var mid = lo + ((hi - lo) >> 1);
                if (Covered(mid, localY, axis, axisLengthSquared, radiusSquared))
                    hi = mid;
                else
                    lo = mid + 1;
            }

            return lo;
        }

        private static int RightmostCovered(int lo, int hi, int localY, int2 axis, long axisLengthSquared,
            long radiusSquared)
        {
            while (lo < hi)
            {
                var mid = lo + ((hi - lo + 1) >> 1);
                if (Covered(mid, localY, axis, axisLengthSquared, radiusSquared))
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return lo;
        }

        private static void EmitEllipse(ref SpanSink sink, int2 center, int2 radii, int weight)
        {
            if (radii.x < 0 || radii.y < 0) return;

            var rx = radii.x;
            var ry = radii.y;

            for (var dy = -ry; dy <= ry; dy++)
            {
                var half = EllipseHalfWidth(rx, ry, dy);
                var y = center.y + dy;

                sink.Push(new WeightedRect(
                    new CellRect(
                        new int2(center.x - half, y),
                        new int2(center.x + half + 1, y + 1)),
                    weight));
            }
        }

        private static int EllipseHalfWidth(int rx, int ry, int dy)
        {
            if (rx <= 0) return 0;

            if (ry <= 0) return rx;

            var rx2 = (long)rx * rx;
            var ry2 = (long)ry * ry;
            var dy2 = (long)dy * dy;
            var rhs = rx2 * ry2;

            var lo = 0;
            var hi = rx;

            while (lo < hi)
            {
                var mid = lo + ((hi - lo + 1) >> 1);
                var lhs = (long)mid * mid * ry2 + dy2 * rx2;

                if (lhs <= rhs)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return lo;
        }

        private static void EmitRoundedRect(ref SpanSink sink, int2 min, int2 size, int radius, int weight)
        {
            if (size.x <= 0 || size.y <= 0 || radius < 0) return;

            var maxRadius = (math.min(size.x, size.y) - 1) >> 1;
            var r = math.min(radius, maxRadius);

            if (r <= 0)
            {
                EmitRect(ref sink, min, size, weight);
                return;
            }

            var r2 = (long)r * r;

            var leftCenter = min.x + r;
            var rightCenter = min.x + size.x - r - 1;
            var bottomCenter = min.y + r;
            var topCenter = min.y + size.y - r - 1;

            for (var ly = 0; ly < size.y; ly++)
            {
                var y = min.y + ly;
                var bottomBand = ly < r;
                var topBand = ly >= size.y - r;

                var x0 = min.x;
                var x1 = min.x + size.x;

                if (bottomBand || topBand)
                {
                    var centerY = bottomBand ? bottomCenter : topCenter;
                    var dy = y - centerY;
                    var half = IntegerMath.FloorSqrt(r2 - (long)dy * dy);

                    x0 = leftCenter - half;
                    x1 = rightCenter + half + 1;
                }

                sink.Push(new WeightedRect(
                    new CellRect(new int2(x0, y), new int2(x1, y + 1)),
                    weight));
            }
        }

        private static void EmitSector(ref SpanSink sink, int2 center, int radius, int2 d0, int2 d1, int weight)
        {
            if (radius < 0) return;

            var r2 = (long)radius * radius;

            for (var dy = -radius; dy <= radius; dy++)
            {
                var rem = r2 - (long)dy * dy;
                if (rem < 0) continue;

                var half = IntegerMath.FloorSqrt(rem);
                var xlo = -half;
                var xhi = half;

                var c0 = (long)d0.x * dy;
                if (d0.y > 0) xhi = math.min(xhi, (int)IntegerMath.FloorDiv(c0, d0.y));
                else if (d0.y < 0) xlo = math.max(xlo, (int)IntegerMath.CeilDiv(c0, d0.y));
                else if (c0 < 0) continue;

                var c1 = (long)d1.x * dy;
                if (d1.y > 0) xlo = math.max(xlo, (int)IntegerMath.CeilDiv(c1, d1.y));
                else if (d1.y < 0) xhi = math.min(xhi, (int)IntegerMath.FloorDiv(c1, d1.y));
                else if (-(long)dy * d1.x < 0) continue;

                if (xlo > xhi) continue;

                var y = center.y + dy;
                sink.Push(new WeightedRect(
                    new CellRect(new int2(center.x + xlo, y), new int2(center.x + xhi + 1, y + 1)),
                    weight));
            }
        }
    }
}