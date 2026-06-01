using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence
{
    [BurstCompile]
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

    [BurstCompile]
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
        public static int FloorSqrt(int value)
        {
            if (value <= 0) return 0;
            int root = (int)math.sqrt((double)value);
            while ((long)(root + 1) * (root + 1) <= value) root++;
            while ((long)root * root > value) root--;
            return root;
        }
    }
}