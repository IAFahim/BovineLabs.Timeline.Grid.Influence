using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public readonly struct CellRect
    {
        public readonly int2 Min;
        public readonly int2 Max;

        public CellRect(int2 min, int2 max)
        {
            Min = min;
            Max = max;
        }

        public static CellRect Empty => new(int2.zero, int2.zero);

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Max.x <= Min.x || Max.y <= Min.y;
        }
    }

    public readonly struct ChunkRange
    {
        public readonly int2 Min;
        public readonly int2 Max;

        public ChunkRange(int2 min, int2 max)
        {
            Min = min;
            Max = max;
        }
    }

    public readonly struct WeightedRect
    {
        public readonly CellRect Bounds;
        public readonly int Weight;

        public WeightedRect(CellRect bounds, int weight)
        {
            Bounds = bounds;
            Weight = weight;
        }

        public static WeightedRect Empty => new(CellRect.Empty, 0);

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Bounds.IsEmpty;
        }
    }

    public static class IntegerMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorSqrt(long value)
        {
            if (value <= 0) return 0;

            var root = (long)math.sqrt((double)value);
            while ((root + 1) * (root + 1) <= value) root++;

            while (root * root > value) root--;

            return root > int.MaxValue ? int.MaxValue : (int)root;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ClampToInt(long value)
        {
            if (value <= 0) return 0;

            return value > int.MaxValue ? int.MaxValue : (int)value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SaturatingAdd(int a, int b)
        {
            return ClampToInt((long)a + b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FloorDiv(long a, long b)
        {
            var q = a / b;
            if (a % b != 0 && a < 0 != b < 0) q--;
            return q;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long CeilDiv(long a, long b)
        {
            var q = a / b;
            if (a % b != 0 && a < 0 == b < 0) q++;
            return q;
        }
    }
}