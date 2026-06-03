using System.Numerics;
using System.Reflection;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Tests
{
    internal static unsafe class InfluenceTestHarness
    {
        internal static int[,] Run(in GridSpec spec, Stamp[] stamps, int2 boxMin, int2 boxSize)
        {
            var field = InfluenceField.Create(spec, Allocator.Persistent);
            var array = new NativeArray<Stamp>(stamps, Allocator.TempJob);
            field.Schedule(array, default).Complete();

            var reader = field.AsReader();
            var grid = new int[boxSize.x, boxSize.y];
            for (int x = 0; x < boxSize.x; x++)
            {
                for (int y = 0; y < boxSize.y; y++)
                {
                    grid[x, y] = reader.ReadCell(new int2(boxMin.x + x, boxMin.y + y));
                }
            }

            array.Dispose();
            field.Dispose();
            return grid;
        }

        internal static long[,] Oracle(Stamp[] stamps, int2 boxMin, int2 boxSize)
        {
            var grid = new long[boxSize.x, boxSize.y];
            for (int x = 0; x < boxSize.x; x++)
            {
                for (int y = 0; y < boxSize.y; y++)
                {
                    int2 cell = new int2(boxMin.x + x, boxMin.y + y);
                    long value = 0;
                    for (int s = 0; s < stamps.Length; s++)
                    {
                        value += Contribution(stamps[s].Shape, stamps[s].Origin, cell);
                    }

                    grid[x, y] = value;
                }
            }

            return grid;
        }

        internal static (int2 Min, int2 Size) PaddedBox(Stamp[] stamps, in GridSpec spec, int extraCells)
        {
            bool any = false;
            int2 min = int2.zero;
            int2 max = new int2(1, 1);

            for (int i = 0; i < stamps.Length; i++)
            {
                CellRect bounds = Rasterizer.Bounds(stamps[i].Shape, stamps[i].Origin);
                if (bounds.IsEmpty)
                {
                    continue;
                }

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

            int pad = spec.ChunkSize + extraCells;
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
                    if (shape.ShellSize.x <= 0 || shape.ShellSize.y <= 0 || shape.ShellThickness <= 0)
                    {
                        return 0;
                    }

                    long value = InRect(origin + shape.ShellMin, shape.ShellSize, cell) ? shape.Weight : 0;
                    int2 innerMin = origin + shape.ShellMin + new int2(shape.ShellThickness, shape.ShellThickness);
                    int2 innerSize = shape.ShellSize - new int2(2 * shape.ShellThickness, 2 * shape.ShellThickness);
                    if (InRect(innerMin, innerSize, cell))
                    {
                        value -= shape.Weight;
                    }

                    return value;
                }

                case ShapeKind.Disc:
                    return shape.DiscRadius >= 0 && InDisc(origin + shape.DiscCenter, shape.DiscRadius, cell) ? shape.Weight : 0;

                case ShapeKind.Annulus:
                {
                    if (shape.AnnulusOuterRadius < 0 || shape.AnnulusInnerRadius >= shape.AnnulusOuterRadius)
                    {
                        return 0;
                    }

                    int2 center = origin + shape.AnnulusCenter;
                    long value = InDisc(center, shape.AnnulusOuterRadius, cell) ? shape.Weight : 0;
                    if (shape.AnnulusInnerRadius >= 0 && InDisc(center, shape.AnnulusInnerRadius, cell))
                    {
                        value -= shape.Weight;
                    }

                    return value;
                }

                case ShapeKind.Capsule:
                    return shape.CapsuleRadius >= 0 && CapsuleContains(origin + shape.CapsuleStart, origin + shape.CapsuleEnd, shape.CapsuleRadius, cell)
                        ? shape.Weight
                        : 0;

                default:
                    return 0;
            }
        }

        internal static bool InRect(int2 min, int2 size, int2 cell)
            => size.x > 0 && size.y > 0 &&
               cell.x >= min.x && cell.x < min.x + size.x &&
               cell.y >= min.y && cell.y < min.y + size.y;

        internal static bool InDisc(int2 center, int radius, int2 cell)
        {
            long dx = cell.x - center.x;
            long dy = cell.y - center.y;
            return dx * dx + dy * dy <= (long)radius * radius;
        }

        internal static bool CapsuleContains(int2 a, int2 b, int radius, int2 p)
        {
            if (radius < 0)
            {
                return false;
            }

            BigInteger abx = (BigInteger)b.x - a.x;
            BigInteger aby = (BigInteger)b.y - a.y;
            BigInteger apx = (BigInteger)p.x - a.x;
            BigInteger apy = (BigInteger)p.y - a.y;

            BigInteger axisLengthSquared = abx * abx + aby * aby;
            BigInteger radiusSquared = (BigInteger)radius * radius;
            BigInteger projection = apx * abx + apy * aby;
            BigInteger distanceToStart = apx * apx + apy * apy;

            if (axisLengthSquared.IsZero || projection.Sign <= 0)
            {
                return distanceToStart <= radiusSquared;
            }

            if (projection >= axisLengthSquared)
            {
                BigInteger bpx = (BigInteger)p.x - b.x;
                BigInteger bpy = (BigInteger)p.y - b.y;
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
            for (int i = 0; i < count; i++)
            {
                managed[i] = buffer[i];
            }

            buffer.Dispose();
            return managed;
        }

        internal static Stamp RandomStamp(ref Random rng)
        {
            int2 origin = rng.NextInt2(new int2(-30, -30), new int2(31, 31));
            int weight = rng.NextInt(-5, 6);
            if (weight == 0)
            {
                weight = 1;
            }

            switch (rng.NextInt(0, 5))
            {
                case 0:
                    return new Stamp(InfluenceShape.SolidRect(
                        rng.NextInt2(new int2(-10, -10), new int2(11, 11)),
                        rng.NextInt2(new int2(1, 1), new int2(16, 16)),
                        weight), origin);

                case 1:
                {
                    int2 size = rng.NextInt2(new int2(2, 2), new int2(16, 16));
                    int thickness = rng.NextInt(1, math.max(2, math.min(size.x, size.y)));
                    return new Stamp(InfluenceShape.RectShell(
                        rng.NextInt2(new int2(-10, -10), new int2(11, 11)), size, thickness, weight), origin);
                }

                case 2:
                    return new Stamp(InfluenceShape.Disc(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)), rng.NextInt(0, 12), weight), origin);

                case 3:
                {
                    int outer = rng.NextInt(1, 12);
                    int inner = rng.NextInt(-1, outer);
                    return new Stamp(InfluenceShape.Annulus(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)), outer, inner, weight), origin);
                }

                default:
                    return new Stamp(InfluenceShape.Capsule(
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt2(new int2(-8, -8), new int2(9, 9)),
                        rng.NextInt(0, 10), weight), origin);
            }
        }

        internal static void SetPrivate<T>(ref InfluenceField field, string name, T value)
        {
            object box = field;
            typeof(InfluenceField).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(box, value);
            field = (InfluenceField)box;
        }

        internal static T GetPrivate<T>(in InfluenceField field, string name)
            => (T)typeof(InfluenceField).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(field);
    }
}
