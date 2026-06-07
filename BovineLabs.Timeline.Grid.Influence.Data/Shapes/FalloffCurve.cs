using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public enum FalloffCurve : byte
    {
        Constant,
        Linear,
        SmoothStep,
        EaseIn,
        EaseOut,
        Spike
    }

    public readonly struct FalloffProfile
    {
        public readonly FalloffCurve Curve;
        public readonly int Peak;
        public readonly int Levels;

        public FalloffProfile(FalloffCurve curve, int peak, int levels)
        {
            Curve = curve;
            Peak = math.max(0, peak);
            Levels = math.max(1, levels);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CenterWeightAt(int depth)
        {
            var span = math.max(1, Levels - 1);
            var distance01 = 1f - math.saturate(depth / (float)span);
            return (int)math.round(Peak * Evaluate(Curve, distance01));
        }

        public void SampleInto(NativeArray<int> targetPerDepth)
        {
            for (var depth = 0; depth < targetPerDepth.Length; depth++)
                targetPerDepth[depth] = CenterWeightAt(depth);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Evaluate(FalloffCurve curve, float distance01)
        {
            var t = math.saturate(1f - distance01);
            return curve switch
            {
                FalloffCurve.Constant => 1f,
                FalloffCurve.Linear => t,
                FalloffCurve.SmoothStep => t * t * (3f - 2f * t),
                FalloffCurve.EaseIn => t * t,
                FalloffCurve.EaseOut => 1f - (1f - t) * (1f - t),
                FalloffCurve.Spike => t * t * t,
                _ => t
            };
        }
    }
}