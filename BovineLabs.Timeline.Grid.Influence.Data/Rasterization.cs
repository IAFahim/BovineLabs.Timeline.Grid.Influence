using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public static class Rasterizer
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Emit(Stamp stamp, ref NativeList<WorldRect> output)
        {
            InfluenceShape shape = stamp.Shape;
            int2 origin = stamp.Origin;
            int weight = shape.Weight;

            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                    EmitRect(ref output, origin + shape.RectMin, shape.RectSize, weight);
                    break;

                case ShapeKind.RectShell:
                    EmitShell(ref output, origin + shape.ShellMin, shape.ShellSize, shape.ShellThickness, weight);
                    break;

                case ShapeKind.Disc:
                    EmitDisc(ref output, origin + shape.DiscCenter, shape.DiscRadius, weight);
                    break;

                case ShapeKind.Annulus:
                    EmitAnnulus(ref output, origin + shape.AnnulusCenter, shape.AnnulusOuter, shape.AnnulusInner, weight);
                    break;

                case ShapeKind.Capsule:
                    EmitCapsule(ref output, origin + shape.CapsuleA, origin + shape.CapsuleB, shape.CapsuleRadius, weight);
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void EmitRect(ref NativeList<WorldRect> output, int2 min, int2 size, int weight)
        {
            if (size.x <= 0 || size.y <= 0) return;
            output.Add(new WorldRect(new AlignedRect(min, min + size), weight));
        }

        static void EmitShell(ref NativeList<WorldRect> output, int2 min, int2 size, int thickness, int weight)
        {
            if (size.x <= 0 || size.y <= 0 || thickness <= 0) return;
            EmitRect(ref output, min, size, weight);
            int2 innerMin = min + new int2(thickness, thickness);
            int2 innerSize = size - new int2(2 * thickness, 2 * thickness);
            EmitRect(ref output, innerMin, innerSize, -weight);
        }

        static void EmitAnnulus(ref NativeList<WorldRect> output, int2 center, int outerRadius, int innerRadius, int weight)
        {
            if (outerRadius < 0 || innerRadius >= outerRadius) return;

            EmitDisc(ref output, center, outerRadius, weight);

            if (innerRadius >= 0) EmitDisc(ref output, center, innerRadius, -weight);
        }

        static void EmitDisc(ref NativeList<WorldRect> output, int2 center, int radius, int weight)
        {
            if (radius < 0) return;

            long radiusSquared = (long)radius * radius;

            for (int dy = -radius; dy <= radius; dy++)
            {
                long dySquared = (long)dy * dy;
                int half = IntegerMath.FloorSqrt(radiusSquared - dySquared);
                int y = center.y + dy;

                output.Add(new WorldRect(
                    new AlignedRect(
                        new int2(center.x - half, y),
                        new int2(center.x + half + 1, y + 1)),
                    weight));
            }
        }

        static void EmitCapsule(ref NativeList<WorldRect> output, int2 a, int2 b, int radius, int weight)
        {
            if (radius < 0) return;

            if (math.all(a == b))
            {
                EmitDisc(ref output, a, radius, weight);
                return;
            }

            int xMin = math.min(a.x, b.x) - radius;
            int xMax = math.max(a.x, b.x) + radius;
            int yMin = math.min(a.y, b.y) - radius;
            int yMax = math.max(a.y, b.y) + radius;
            long radiusSquared = (long)radius * radius;

            for (int y = yMin; y <= yMax; y++)
            {
                bool covered = false;
                int xStart = 0;
                int xEnd = 0;

                for (int x = xMin; x <= xMax; x++)
                {
                    bool inside = PointSegmentDistanceLessOrEqualRadiusSquared(new int2(x, y), a, b, radiusSquared);

                    if (!inside)
                    {
                        if (covered) break;
                        continue;
                    }

                    if (!covered)
                    {
                        xStart = x;
                        covered = true;
                    }

                    xEnd = x;
                }

                if (!covered) continue;

                output.Add(new WorldRect(
                    new AlignedRect(new int2(xStart, y), new int2(xEnd + 1, y + 1)),
                    weight));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool PointSegmentDistanceLessOrEqualRadiusSquared(int2 p, int2 a, int2 b, long radiusSquared)
        {
            long abx = (long)b.x - a.x;
            long aby = (long)b.y - a.y;
            long apx = (long)p.x - a.x;
            long apy = (long)p.y - a.y;
            long lenSquared = abx * abx + aby * aby;
            long dot = apx * abx + apy * aby;

            if (dot <= 0)
            {
                return SquaredLengthLessOrEqual(apx, apy, radiusSquared);
            }

            if (dot >= lenSquared)
            {
                long bpx = (long)p.x - b.x;
                long bpy = (long)p.y - b.y;
                return SquaredLengthLessOrEqual(bpx, bpy, radiusSquared);
            }

            long cross = abx * apy - aby * apx;
            return SquareLessOrEqualProduct(cross, radiusSquared, lenSquared);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool SquaredLengthLessOrEqual(long x, long y, long radiusSquared)
        {
            return x * x + y * y <= radiusSquared;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool SquareLessOrEqualProduct(long value, long a, long b)
        {
            long absValue = value < 0 ? -value : value;

            // In normal gameplay ranges this remains entirely integer-exact. The fallback
            // keeps extreme authored coordinates from wrapping silently.
            if (WouldMultiplyOverflow(absValue, absValue) || WouldMultiplyOverflow(a, b))
            {
                return (double)absValue * absValue <= (double)a * b;
            }

            return absValue * absValue <= a * b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool WouldMultiplyOverflow(long a, long b)
        {
            if (a == 0 || b == 0) return false;
            return a > long.MaxValue / b;
        }
    }
}
