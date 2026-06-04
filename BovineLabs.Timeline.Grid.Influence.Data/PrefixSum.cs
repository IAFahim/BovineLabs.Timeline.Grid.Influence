using static Unity.Burst.Intrinsics.X86;
using static Unity.Burst.Intrinsics.Arm;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public static unsafe class PrefixSum
    {
        public static void Run(int* field, in GridSpec spec)
        {
            HorizontalPass(field, spec.Stride, spec.Dimension);
            VerticalPass(field, spec.Stride, spec.Dimension);
        }

        private static void HorizontalPass(int* field, int stride, int dimension)
        {
            if (Avx2.IsAvx2Supported && dimension >= 8)
                HorizontalPassAvx2(field, stride, dimension);
            else
                HorizontalPassScalar(field, stride, dimension);
        }

        private static void HorizontalPassScalar(int* field, int stride, int dimension)
        {
            for (var y = 0; y < dimension; y++)
            {
                var row = field + y * stride;
                var running = 0;
                for (var x = 0; x < dimension; x++)
                {
                    running += row[x];
                    row[x] = running;
                }
            }
        }

        private static void HorizontalPassAvx2(int* field, int stride, int dimension)
        {
            var broadcastLane3 = Avx.mm256_set1_epi32(3);
            var highHalfMask = Avx.mm256_setr_epi32(0, 0, 0, 0, -1, -1, -1, -1);
            var dim8 = dimension & ~7;

            for (var y = 0; y < dimension; y++)
            {
                var row = field + y * stride;
                var carry = 0;
                var x = 0;
                for (; x < dim8; x += 8)
                {
                    var v = Avx.mm256_loadu_si256(row + x);
                    v = Avx2.mm256_add_epi32(v, Avx2.mm256_slli_si256(v, 4));
                    v = Avx2.mm256_add_epi32(v, Avx2.mm256_slli_si256(v, 8));
                    var lowTotal = Avx2.mm256_permutevar8x32_epi32(v, broadcastLane3);
                    v = Avx2.mm256_add_epi32(v, Avx2.mm256_and_si256(lowTotal, highHalfMask));
                    v = Avx2.mm256_add_epi32(v, Avx.mm256_set1_epi32(carry));
                    Avx.mm256_storeu_si256(row + x, v);
                    carry = row[x + 7];
                }

                var running = carry;
                for (; x < dimension; x++)
                {
                    running += row[x];
                    row[x] = running;
                }
            }
        }

        private static void VerticalPass(int* field, int stride, int dimension)
        {
            if (Avx2.IsAvx2Supported)
                VerticalPassAvx2(field, stride, dimension);
            else if (Neon.IsNeonSupported)
                VerticalPassNeon(field, stride, dimension);
            else
                VerticalPassScalar(field, stride, dimension);
        }

        private static void VerticalPassAvx2(int* field, int stride, int dimension)
        {
            for (var y = 1; y < dimension; y++)
            {
                var above = field + (y - 1) * stride;
                var current = field + y * stride;
                for (var x = 0; x < stride; x += 8)
                {
                    var a = Avx.mm256_loadu_si256(above + x);
                    var c = Avx.mm256_loadu_si256(current + x);
                    Avx.mm256_storeu_si256(current + x, Avx2.mm256_add_epi32(c, a));
                }
            }
        }

        private static void VerticalPassNeon(int* field, int stride, int dimension)
        {
            for (var y = 1; y < dimension; y++)
            {
                var above = field + (y - 1) * stride;
                var current = field + y * stride;
                for (var x = 0; x < stride; x += 4)
                {
                    var a = Neon.vld1q_s32(above + x);
                    var c = Neon.vld1q_s32(current + x);
                    Neon.vst1q_s32(current + x, Neon.vaddq_s32(c, a));
                }
            }
        }

        private static void VerticalPassScalar(int* field, int stride, int dimension)
        {
            for (var y = 1; y < dimension; y++)
            {
                var above = field + (y - 1) * stride;
                var current = field + y * stride;
                for (var x = 0; x < dimension; x++) current[x] += above[x];
            }
        }
    }
}