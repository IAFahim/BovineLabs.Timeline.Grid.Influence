using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public unsafe ref struct SpanSink
    {
        readonly WeightedRect* _spans;
        readonly int _capacity;
        int _count;

        public SpanSink(WeightedRect* spans, int capacity)
        {
            _spans = spans;
            _capacity = capacity;
            _count = 0;
        }

        public int Count => _count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(in WeightedRect span)
        {
            if ((uint)_count < (uint)_capacity)
            {
                _spans[_count] = span;
                _count++;
            }
        }

        public void SealRemaining()
        {
            while (_count < _capacity)
            {
                _spans[_count] = WeightedRect.Empty;
                _count++;
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
                    if (shape.RectSize.x <= 0 || shape.RectSize.y <= 0)
                    {
                        return CellRect.Empty;
                    }

                    int2 min = origin + shape.RectMin;
                    return new CellRect(min, min + shape.RectSize);
                }

                case ShapeKind.RectShell:
                {
                    if (shape.ShellSize.x <= 0 || shape.ShellSize.y <= 0 || shape.ShellThickness <= 0)
                    {
                        return CellRect.Empty;
                    }

                    int2 min = origin + shape.ShellMin;
                    return new CellRect(min, min + shape.ShellSize);
                }

                case ShapeKind.Disc:
                {
                    if (shape.DiscRadius < 0)
                    {
                        return CellRect.Empty;
                    }

                    int2 center = origin + shape.DiscCenter;
                    int r = shape.DiscRadius;
                    return new CellRect(center - new int2(r, r), center + new int2(r + 1, r + 1));
                }

                case ShapeKind.Annulus:
                {
                    if (shape.AnnulusOuterRadius < 0 || shape.AnnulusInnerRadius >= shape.AnnulusOuterRadius)
                    {
                        return CellRect.Empty;
                    }

                    int2 center = origin + shape.AnnulusCenter;
                    int r = shape.AnnulusOuterRadius;
                    return new CellRect(center - new int2(r, r), center + new int2(r + 1, r + 1));
                }

                case ShapeKind.Capsule:
                {
                    if (shape.CapsuleRadius < 0)
                    {
                        return CellRect.Empty;
                    }

                    int r = shape.CapsuleRadius;
                    int2 a = origin + shape.CapsuleStart;
                    int2 b = origin + shape.CapsuleEnd;
                    return new CellRect(math.min(a, b) - new int2(r, r), math.max(a, b) + new int2(r + 1, r + 1));
                }

                case ShapeKind.Ellipse:
                {
                    if (shape.EllipseRadii.x < 0 || shape.EllipseRadii.y < 0)
                    {
                        return CellRect.Empty;
                    }

                    int2 center = origin + shape.EllipseCenter;
                    int2 radii = shape.EllipseRadii;
                    return new CellRect(center - radii, center + radii + new int2(1, 1));
                }

                case ShapeKind.RoundedRect:
                {
                    if (shape.RoundedRectSize.x <= 0 || shape.RoundedRectSize.y <= 0 || shape.RoundedRectRadius < 0)
                    {
                        return CellRect.Empty;
                    }

                    int2 min = origin + shape.RoundedRectMin;
                    return new CellRect(min, min + shape.RoundedRectSize);
                }

                case ShapeKind.ThickLine:
                {
                    if (shape.ThickLineRadius < 0)
                    {
                        return CellRect.Empty;
                    }

                    int r = shape.ThickLineRadius;
                    int2 a = origin + shape.ThickLineStart;
                    int2 b = origin + shape.ThickLineEnd;
                    return new CellRect(math.min(a, b) - new int2(r, r), math.max(a, b) + new int2(r + 1, r + 1));
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
                    if (shape.ShellSize.x <= 0 || shape.ShellSize.y <= 0 || shape.ShellThickness <= 0)
                    {
                        return 0;
                    }

                    int2 inner = shape.ShellSize - new int2(2 * shape.ShellThickness, 2 * shape.ShellThickness);
                    return inner.x > 0 && inner.y > 0 ? 2 : 1;
                }

                case ShapeKind.Disc:
                    return DiscRowCount(shape.DiscRadius);

                case ShapeKind.Annulus:
                {
                    if (shape.AnnulusOuterRadius < 0 || shape.AnnulusInnerRadius >= shape.AnnulusOuterRadius)
                    {
                        return 0;
                    }

                    int inner = shape.AnnulusInnerRadius >= 0 ? DiscRowCount(shape.AnnulusInnerRadius) : 0;
                    return IntegerMath.SaturatingAdd(DiscRowCount(shape.AnnulusOuterRadius), inner);
                }

                case ShapeKind.Capsule:
                {
                    if (shape.CapsuleRadius < 0)
                    {
                        return 0;
                    }

                    long spanY = math.abs((long)shape.CapsuleEnd.y - shape.CapsuleStart.y);
                    return IntegerMath.ClampToInt(spanY + 2L * shape.CapsuleRadius + 3L);
                }

                case ShapeKind.Ellipse:
                {
                    if (shape.EllipseRadii.x < 0 || shape.EllipseRadii.y < 0)
                    {
                        return 0;
                    }

                    return IntegerMath.ClampToInt(2L * shape.EllipseRadii.y + 1L);
                }

                case ShapeKind.RoundedRect:
                {
                    if (shape.RoundedRectSize.x <= 0 || shape.RoundedRectSize.y <= 0 || shape.RoundedRectRadius < 0)
                    {
                        return 0;
                    }

                    return shape.RoundedRectSize.y;
                }

                case ShapeKind.ThickLine:
                {
                    if (shape.ThickLineRadius < 0)
                    {
                        return 0;
                    }

                    long spanY = math.abs((long)shape.ThickLineEnd.y - shape.ThickLineStart.y);
                    return IntegerMath.ClampToInt(spanY + 2L * shape.ThickLineRadius + 3L);
                }

                default:
                    return 0;
            }
        }

        public static void Emit(in Stamp stamp, ref SpanSink sink)
        {
            InfluenceShape shape = stamp.Shape;
            int2 origin = stamp.Origin;
            int weight = shape.Weight;

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
                    {
                        EmitDisc(ref sink, origin + shape.AnnulusCenter, shape.AnnulusInnerRadius, -weight);
                    }

                    break;

                case ShapeKind.Capsule:
                    EmitCapsule(ref sink, origin + shape.CapsuleStart, origin + shape.CapsuleEnd, shape.CapsuleRadius, weight);
                    break;

                case ShapeKind.Ellipse:
                    EmitEllipse(ref sink, origin + shape.EllipseCenter, shape.EllipseRadii, weight);
                    break;

                case ShapeKind.RoundedRect:
                    EmitRoundedRect(ref sink, origin + shape.RoundedRectMin, shape.RoundedRectSize, shape.RoundedRectRadius, weight);
                    break;

                case ShapeKind.ThickLine:
                    EmitCapsule(ref sink, origin + shape.ThickLineStart, origin + shape.ThickLineEnd, shape.ThickLineRadius, weight);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int DiscRowCount(int radius) => radius < 0 ? 0 : IntegerMath.ClampToInt(2L * radius + 1L);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void EmitRect(ref SpanSink sink, int2 min, int2 size, int weight)
        {
            if (size.x <= 0 || size.y <= 0)
            {
                return;
            }

            sink.Push(new WeightedRect(new CellRect(min, min + size), weight));
        }

        static void EmitShell(ref SpanSink sink, int2 min, int2 size, int thickness, int weight)
        {
            if (size.x <= 0 || size.y <= 0 || thickness <= 0)
            {
                return;
            }

            EmitRect(ref sink, min, size, weight);
            int2 innerMin = min + new int2(thickness, thickness);
            int2 innerSize = size - new int2(2 * thickness, 2 * thickness);
            EmitRect(ref sink, innerMin, innerSize, -weight);
        }

        static void EmitDisc(ref SpanSink sink, int2 center, int radius, int weight)
        {
            if (radius < 0)
            {
                return;
            }

            long radiusSquared = (long)radius * radius;

            for (int dy = -radius; dy <= radius; dy++)
            {
                int half = IntegerMath.FloorSqrt(radiusSquared - (long)dy * dy);
                int y = center.y + dy;
                sink.Push(new WeightedRect(
                    new CellRect(new int2(center.x - half, y), new int2(center.x + half + 1, y + 1)),
                    weight));
            }
        }

        static void EmitCapsule(ref SpanSink sink, int2 a, int2 b, int radius, int weight)
        {
            if (radius < 0)
            {
                return;
            }

            int2 axis = b - a;
            long axisLengthSquared = (long)axis.x * axis.x + (long)axis.y * axis.y;
            long radiusSquared = (long)radius * radius;

            int loLocalY = math.min(0, axis.y) - radius;
            int hiLocalY = math.max(0, axis.y) + radius;
            int loLocalX = math.min(0, axis.x) - radius;
            int hiLocalX = math.max(0, axis.x) + radius;

            for (int localY = loLocalY; localY <= hiLocalY; localY++)
            {
                int seedX = math.clamp(SeedLocalX(localY, axis), loLocalX, hiLocalX);

                if (!Covered(seedX, localY, axis, axisLengthSquared, radiusSquared))
                {
                    continue;
                }

                int leftX = LeftmostCovered(loLocalX, seedX, localY, axis, axisLengthSquared, radiusSquared);
                int rightX = RightmostCovered(seedX, hiLocalX, localY, axis, axisLengthSquared, radiusSquared);

                int worldY = a.y + localY;
                sink.Push(new WeightedRect(
                    new CellRect(new int2(a.x + leftX, worldY), new int2(a.x + rightX + 1, worldY + 1)),
                    weight));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int SeedLocalX(int localY, int2 axis)
        {
            int loY = math.min(0, axis.y);
            int hiY = math.max(0, axis.y);

            if (axis.y != 0 && localY >= loY && localY <= hiY)
            {
                return (int)((long)axis.x * localY / axis.y);
            }

            return math.abs(localY) <= math.abs(localY - axis.y) ? 0 : axis.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Covered(int px, int py, int2 axis, long axisLengthSquared, long radiusSquared)
        {
            long projection = (long)px * axis.x + (long)py * axis.y;
            long distanceToStart = (long)px * px + (long)py * py;

            if (axisLengthSquared == 0 || projection <= 0)
            {
                return distanceToStart <= radiusSquared;
            }

            if (projection >= axisLengthSquared)
            {
                long qx = px - axis.x;
                long qy = py - axis.y;
                return qx * qx + qy * qy <= radiusSquared;
            }

            return distanceToStart * axisLengthSquared - projection * projection <= radiusSquared * axisLengthSquared;
        }

        static int LeftmostCovered(int lo, int hi, int localY, int2 axis, long axisLengthSquared, long radiusSquared)
        {
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (Covered(mid, localY, axis, axisLengthSquared, radiusSquared))
                {
                    hi = mid;
                }
                else
                {
                    lo = mid + 1;
                }
            }

            return lo;
        }

        static int RightmostCovered(int lo, int hi, int localY, int2 axis, long axisLengthSquared, long radiusSquared)
        {
            while (lo < hi)
            {
                int mid = lo + ((hi - lo + 1) >> 1);
                if (Covered(mid, localY, axis, axisLengthSquared, radiusSquared))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return lo;
        }

        static void EmitEllipse(ref SpanSink sink, int2 center, int2 radii, int weight)
        {
            if (radii.x < 0 || radii.y < 0)
            {
                return;
            }

            int rx = radii.x;
            int ry = radii.y;

            for (int dy = -ry; dy <= ry; dy++)
            {
                int half = EllipseHalfWidth(rx, ry, dy);
                int y = center.y + dy;

                sink.Push(new WeightedRect(
                    new CellRect(
                        new int2(center.x - half, y),
                        new int2(center.x + half + 1, y + 1)),
                    weight));
            }
        }

        static int EllipseHalfWidth(int rx, int ry, int dy)
        {
            if (rx <= 0)
            {
                return 0;
            }

            if (ry <= 0)
            {
                return rx;
            }

            long rx2 = (long)rx * rx;
            long ry2 = (long)ry * ry;
            long dy2 = (long)dy * dy;
            long rhs = rx2 * ry2;

            int lo = 0;
            int hi = rx;

            while (lo < hi)
            {
                int mid = lo + ((hi - lo + 1) >> 1);
                long lhs = (long)mid * mid * ry2 + dy2 * rx2;

                if (lhs <= rhs)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            return lo;
        }

        static void EmitRoundedRect(ref SpanSink sink, int2 min, int2 size, int radius, int weight)
        {
            if (size.x <= 0 || size.y <= 0 || radius < 0)
            {
                return;
            }

            int maxRadius = (math.min(size.x, size.y) - 1) >> 1;
            int r = math.min(radius, maxRadius);

            if (r <= 0)
            {
                EmitRect(ref sink, min, size, weight);
                return;
            }

            long r2 = (long)r * r;

            int leftCenter = min.x + r;
            int rightCenter = min.x + size.x - r - 1;
            int bottomCenter = min.y + r;
            int topCenter = min.y + size.y - r - 1;

            for (int ly = 0; ly < size.y; ly++)
            {
                int y = min.y + ly;
                bool bottomBand = ly < r;
                bool topBand = ly >= size.y - r;

                int x0 = min.x;
                int x1 = min.x + size.x;

                if (bottomBand || topBand)
                {
                    int centerY = bottomBand ? bottomCenter : topCenter;
                    int dy = y - centerY;
                    int half = IntegerMath.FloorSqrt(r2 - (long)dy * dy);

                    x0 = leftCenter - half;
                    x1 = rightCenter + half + 1;
                }

                sink.Push(new WeightedRect(
                    new CellRect(new int2(x0, y), new int2(x1, y + 1)),
                    weight));
            }
        }
    }
}
