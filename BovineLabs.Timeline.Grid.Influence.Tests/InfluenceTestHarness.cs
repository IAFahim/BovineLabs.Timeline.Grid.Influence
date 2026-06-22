using System.Numerics;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    internal static unsafe class InfluenceTestHarness
    {
        private const int RayScale = 1024;

        internal static int[,] Run(in GridSpec spec, Stamp[] stamps, int2 boxMin, int2 boxSize)
        {
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var array = new NativeArray<Stamp>(stamps, Allocator.TempJob);
            field.Schedule(array, default).Complete();

            var reader = field.AsReader();
            var grid = new int[boxSize.x, boxSize.y];
            for (var x = 0; x < boxSize.x; x++)
            for (var y = 0; y < boxSize.y; y++)
                grid[x, y] = reader.ReadCell(new int2(boxMin.x + x, boxMin.y + y));

            array.Dispose();
            field.Dispose();
            return grid;
        }

        internal static long[,] Oracle(Stamp[] stamps, int2 boxMin, int2 boxSize)
        {
            var grid = new long[boxSize.x, boxSize.y];
            for (var x = 0; x < boxSize.x; x++)
            for (var y = 0; y < boxSize.y; y++)
            {
                var cell = new int2(boxMin.x + x, boxMin.y + y);
                long value = 0;
                for (var s = 0; s < stamps.Length; s++) value += Contribution(stamps[s].Shape, stamps[s].Origin, cell);

                grid[x, y] = value;
            }

            return grid;
        }

        internal static (int2 Min, int2 Size) PaddedBox(Stamp[] stamps, in GridSpec spec, int extraCells)
        {
            var any = false;
            var min = int2.zero;
            var max = new int2(1, 1);

            for (var i = 0; i < stamps.Length; i++)
            {
                var bounds = Rasterizer.Bounds(stamps[i].Shape, stamps[i].Origin);
                if (bounds.IsEmpty) continue;

                if (!any)
                {
                    min = bounds.Min;
                    max = bounds.Max;
                    any = true;
                }
                else
                {
                    min = math.min(min, bounds.Min);
                    max = math.max(max, bounds.Max);
                }
            }

            var pad = spec.ChunkSize + extraCells;
            min -= pad;
            max += pad;
            return (min, max - min);
        }

        internal static long Contribution(in InfluenceShape shape, int2 origin, int2 cell)
        {
            switch (shape.Kind)
            {
                case ShapeKind.SolidRect:
                    return InRect(origin + shape.RectMin, shape.RectSize, cell) ? shape.Weight : 0;

                case ShapeKind.RectShell:
                {
                    if (shape.ShellSize.x <= 0 || shape.ShellSize.y <= 0 || shape.ShellThickness <= 0) return 0;

                    long value = InRect(origin + shape.ShellMin, shape.ShellSize, cell) ? shape.Weight : 0;
                    var innerMin = origin + shape.ShellMin + new int2(shape.ShellThickness, shape.ShellThickness);
                    var innerSize = shape.ShellSize - new int2(2 * shape.ShellThickness, 2 * shape.ShellThickness);
                    if (InRect(innerMin, innerSize, cell)) value -= shape.Weight;

                    return value;
                }

                case ShapeKind.Disc:
                    return shape.DiscRadius >= 0 && InDisc(origin + shape.DiscCenter, shape.DiscRadius, cell)
                        ? shape.Weight
                        : 0;

                case ShapeKind.Annulus:
                {
                    if (shape.AnnulusOuterRadius < 0 || shape.AnnulusInnerRadius >= shape.AnnulusOuterRadius) return 0;

                    var center = origin + shape.AnnulusCenter;
                    long value = InDisc(center, shape.AnnulusOuterRadius, cell) ? shape.Weight : 0;
                    if (shape.AnnulusInnerRadius >= 0 && InDisc(center, shape.AnnulusInnerRadius, cell))
                        value -= shape.Weight;

                    return value;
                }

                case ShapeKind.Capsule:
                    return shape.CapsuleRadius >= 0 && CapsuleContains(origin + shape.CapsuleStart,
                        origin + shape.CapsuleEnd, shape.CapsuleRadius, cell)
                        ? shape.Weight
                        : 0;

                case ShapeKind.Ellipse:
                    return InEllipse(origin + shape.EllipseCenter, shape.EllipseRadii, cell)
                        ? shape.Weight
                        : 0;

                case ShapeKind.RoundedRect:
                    return InRoundedRect(origin + shape.RoundedRectMin, shape.RoundedRectSize, shape.RoundedRectRadius,
                        cell)
                        ? shape.Weight
                        : 0;

                case ShapeKind.ThickLine:
                    return shape.ThickLineRadius >= 0 &&
                           CapsuleContains(origin + shape.ThickLineStart, origin + shape.ThickLineEnd,
                               shape.ThickLineRadius, cell)
                        ? shape.Weight
                        : 0;

                case ShapeKind.Sector:
                    return InSector(origin + shape.SectorCenter, shape.SectorRadius, shape.SectorDir0,
                        shape.SectorDir1, cell)
                        ? shape.Weight
                        : 0;

                default:
                    return 0;
            }
        }

        internal static bool InRect(int2 min, int2 size, int2 cell)
        {
            return size.x > 0 && size.y > 0 &&
                   cell.x >= min.x && cell.x < min.x + size.x &&
                   cell.y >= min.y && cell.y < min.y + size.y;
        }

        internal static bool InDisc(int2 center, int radius, int2 cell)
        {
            long dx = cell.x - center.x;
            long dy = cell.y - center.y;
            return dx * dx + dy * dy <= (long)radius * radius;
        }

        internal static bool InSector(int2 center, int radius, int2 dir0, int2 dir1, int2 cell)
        {
            if (radius < 0) return false;

            long qx = cell.x - center.x;
            long qy = cell.y - center.y;
            if (qx * qx + qy * qy > (long)radius * radius) return false;

            var cross0 = dir0.x * qy - dir0.y * qx;
            var cross1 = dir1.x * qy - dir1.y * qx;
            return cross0 >= 0 && cross1 <= 0;
        }

        internal static bool InEllipse(int2 center, int2 radii, int2 cell)
        {
            if (radii.x < 0 || radii.y < 0) return false;

            var dx = cell.x - center.x;
            var dy = cell.y - center.y;

            if (radii.x == 0 && radii.y == 0) return dx == 0 && dy == 0;

            if (radii.x == 0) return dx == 0 && math.abs(dy) <= radii.y;

            if (radii.y == 0) return dy == 0 && math.abs(dx) <= radii.x;

            var rx2 = (BigInteger)radii.x * radii.x;
            var ry2 = (BigInteger)radii.y * radii.y;
            var dx2 = (BigInteger)dx * dx;
            var dy2 = (BigInteger)dy * dy;

            return dx2 * ry2 + dy2 * rx2 <= rx2 * ry2;
        }

        internal static bool InRoundedRect(int2 min, int2 size, int radius, int2 cell)
        {
            if (size.x <= 0 || size.y <= 0 || radius < 0 || !InRect(min, size, cell)) return false;

            var maxRadius = (math.min(size.x, size.y) - 1) >> 1;
            var r = math.min(radius, maxRadius);

            if (r <= 0) return true;

            var maxX = min.x + size.x - 1;
            var maxY = min.y + size.y - 1;

            var cx = math.clamp(cell.x, min.x + r, maxX - r);
            var cy = math.clamp(cell.y, min.y + r, maxY - r);

            long dx = cell.x - cx;
            long dy = cell.y - cy;

            return dx * dx + dy * dy <= (long)r * r;
        }

        internal static bool CapsuleContains(int2 a, int2 b, int radius, int2 p)
        {
            if (radius < 0) return false;

            var abx = (BigInteger)b.x - a.x;
            var aby = (BigInteger)b.y - a.y;
            var apx = (BigInteger)p.x - a.x;
            var apy = (BigInteger)p.y - a.y;

            var axisLengthSquared = abx * abx + aby * aby;
            var radiusSquared = (BigInteger)radius * radius;
            var projection = apx * abx + apy * aby;
            var distanceToStart = apx * apx + apy * apy;

            if (axisLengthSquared.IsZero || projection.Sign <= 0) return distanceToStart <= radiusSquared;

            if (projection >= axisLengthSquared)
            {
                var bpx = (BigInteger)p.x - b.x;
                var bpy = (BigInteger)p.y - b.y;
                return bpx * bpx + bpy * bpy <= radiusSquared;
            }

            return distanceToStart * axisLengthSquared - projection * projection <= radiusSquared * axisLengthSquared;
        }

        internal static WeightedRect[] Emit(in Stamp stamp, int capacity, out int count)
        {
            var buffer = new NativeArray<WeightedRect>(math.max(1, capacity), Allocator.Temp);
            var sink = new SpanSink((WeightedRect*)buffer.GetUnsafePtr(), capacity);
            Rasterizer.Emit(stamp, ref sink);
            count = sink.Count;

            var managed = new WeightedRect[count];
            for (var i = 0; i < count; i++) managed[i] = buffer[i];

            buffer.Dispose();
            return managed;
        }

        internal static Stamp RandomStamp(ref Random rng)
        {
            var origin = rng.NextInt2(new int2(-30, -30), new int2(31, 31));
            var weight = rng.NextInt(-5, 6);
            if (weight == 0) weight = 1;

            switch (rng.NextInt(0, 9))
            {
                case 0:
                    return new Stamp(InfluenceShape.SolidRect(
                        rng.NextInt2(new int2(-10, -10), new int2(11, 11)),
                        rng.NextInt2(new int2(1, 1), new int2(16, 16)),
                        weight), origin);

                case 1:
                {
                    var size = rng.NextInt2(new int2(2, 2), new int2(16, 16));
                    var thickness = rng.NextInt(1, math.max(2, math.min(size.x, size.y)));
                    return new Stamp(InfluenceShape.RectShell(
                        rng.NextInt2(new int2(-10, -10), new int2(11, 11)), size, thickness, weight), origin);
                }

                case 2:
                    return new Stamp(InfluenceShape.Disc(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)), rng.NextInt(0, 12), weight), origin);

                case 3:
                {
                    var outer = rng.NextInt(1, 12);
                    var inner = rng.NextInt(-1, outer);
                    return new Stamp(InfluenceShape.Annulus(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)), outer, inner, weight), origin);
                }

                case 4:
                    return new Stamp(InfluenceShape.Capsule(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt(0, 10), weight), origin);

                case 5:
                    return new Stamp(InfluenceShape.Ellipse(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt2(new int2(0, 0), new int2(12, 8)),
                        weight), origin);

                case 6:
                    return new Stamp(InfluenceShape.RoundedRect(
                        rng.NextInt2(new int2(-10, -10), new int2(11, 11)),
                        rng.NextInt2(new int2(1, 1), new int2(18, 18)),
                        rng.NextInt(0, 6),
                        weight), origin);

                case 7:
                {
                    var facing = rng.NextFloat(0f, 360f);
                    var halfAngle = rng.NextFloat(1f, 90f);
                    return new Stamp(InfluenceShape.Sector(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt(0, 10),
                        Ray(facing - halfAngle),
                        Ray(facing + halfAngle),
                        weight), origin);
                }

                default:
                    return new Stamp(InfluenceShape.ThickLine(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt(0, 8), weight), origin);
            }
        }

        private static int2 Ray(float degrees)
        {
            math.sincos(math.radians(degrees), out var sin, out var cos);
            return new int2((int)math.round(RayScale * cos), (int)math.round(RayScale * sin));
        }
    }
}