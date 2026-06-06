using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Flows
{
    public static class FieldGradient
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 Ascent(FieldReader field, int2 cell)
        {
            var dx = field.ReadCell(cell + new int2(1, 0)) - field.ReadCell(cell - new int2(1, 0));
            var dy = field.ReadCell(cell + new int2(0, 1)) - field.ReadCell(cell - new int2(0, 1));
            return new int2(dx, dy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 Descent(FieldReader field, int2 cell)
        {
            return -Ascent(field, cell);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 Step(int2 gradient)
        {
            return new int2(math.clamp(gradient.x, -1, 1), math.clamp(gradient.y, -1, 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 Normalized(int2 gradient)
        {
            float2 g = gradient;
            var lengthSq = math.lengthsq(g);
            return lengthSq > 0f ? g * math.rsqrt(lengthSq) : float2.zero;
        }
    }
}