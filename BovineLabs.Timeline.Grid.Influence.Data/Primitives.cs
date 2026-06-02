using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public readonly struct AlignedRect
    {
        public readonly int2 Min;
        public readonly int2 Max;

        public AlignedRect(int2 min, int2 max)
        {
            Min = min;
            Max = max;
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Max.x <= Min.x || Max.y <= Min.y;
        }
    }

    public readonly struct WorldRect
    {
        public readonly AlignedRect Bounds;
        public readonly int Weight;

        public WorldRect(AlignedRect bounds, int weight)
        {
            Bounds = bounds;
            Weight = weight;
        }
    }

    public static class IntegerMath
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int FloorSqrt(long value)
        {
            if (value <= 0) return 0;

            long root = (long)math.sqrt((double)value);
            while ((root + 1) * (root + 1) <= value) root++;
            while (root * root > value) root--;
            return root > int.MaxValue ? int.MaxValue : (int)root;
        }
    }
}