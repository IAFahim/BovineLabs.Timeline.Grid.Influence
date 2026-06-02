using Unity.Burst;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    [BurstCompile(FloatMode = FloatMode.Fast, OptimizeFor = OptimizeFor.Performance)]
    public static unsafe class PrefixSumResolve
    {
        public static void Run(int* field, int stride, int dimension)
        {
            HorizontalPass(field, stride, dimension);
            VerticalPass(field, stride, dimension);
        }

        static void HorizontalPass(int* field, int stride, int dimension)
        {
            for (int y = 0; y < dimension; y++)
            {
                int* row = field + y * stride;
                int running = 0;

                for (int x = 0; x < dimension; x++)
                {
                    running += row[x];
                    row[x] = running;
                }
            }
        }

        static void VerticalPass(int* field, int stride, int dimension)
        {
            bool avx2 = Avx2.IsAvx2Supported;

            for (int y = 1; y < dimension; y++)
            {
                int* above = field + (y - 1) * stride;
                int* current = field + y * stride;
                int x = 0;

                if (avx2)
                {
                    for (; x <= dimension - 8; x += 8)
                    {
                        v256 a = Avx.mm256_loadu_si256(above + x);
                        v256 c = Avx.mm256_loadu_si256(current + x);
                        Avx.mm256_storeu_si256(current + x, Avx2.mm256_add_epi32(c, a));
                    }
                }

                for (; x < dimension; x++)
                {
                    current[x] += above[x];
                }
            }
        }
    }
}