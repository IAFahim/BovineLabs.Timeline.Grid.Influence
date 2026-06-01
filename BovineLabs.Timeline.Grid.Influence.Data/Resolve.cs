using Unity.Burst;
using Unity.Burst.Intrinsics;
using static Unity.Burst.Intrinsics.X86;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    [BurstCompile(OptimizeFor = OptimizeFor.Performance)]
    public static unsafe class PrefixSumResolve
    {
        public static void Run(int* field, int stride, int dimension)
        {
            HorizontalPass(field, stride, dimension);
            VerticalPass(field, stride, dimension);
        }

        static void HorizontalPass(int* field, int stride, int dimension)
        {
            if (Avx2.IsAvx2Supported)
            {
                v256 zeroLowerLaneMask = Avx.mm256_setr_epi32(0, 0, 0, 0, -1, -1, -1, -1);
                v256 lowerLaneTotalIndex = Avx.mm256_setr_epi32(0, 0, 0, 0, 3, 3, 3, 3);

                for (int y = 0; y < dimension; y++)
                {
                    int* row = field + y * stride;
                    int carry = 0;
                    int x = 0;

                    for (; x <= dimension - 8; x += 8)
                    {
                        v256 scan = Avx.mm256_loadu_si256(row + x);
                        scan = Avx2.mm256_add_epi32(scan, Avx2.mm256_slli_si256(scan, 4));
                        scan = Avx2.mm256_add_epi32(scan, Avx2.mm256_slli_si256(scan, 8));

                        v256 laneBridge = Avx2.mm256_permutevar8x32_epi32(scan, lowerLaneTotalIndex);
                        laneBridge = Avx2.mm256_and_si256(laneBridge, zeroLowerLaneMask);

                        scan = Avx2.mm256_add_epi32(scan, laneBridge);
                        scan = Avx2.mm256_add_epi32(scan, Avx.mm256_set1_epi32(carry));

                        Avx.mm256_storeu_si256(row + x, scan);
                        carry = row[x + 7];
                    }

                    for (; x < dimension; x++)
                    {
                        carry += row[x];
                        row[x] = carry;
                    }
                }

                return;
            }

            for (int y = 0; y < dimension; y++)
            {
                int* row = field + y * stride;
                int carry = 0;
                for (int x = 0; x < dimension; x++)
                {
                    carry += row[x];
                    row[x] = carry;
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
                    for (; x <= stride - 8; x += 8)
                    {
                        v256 a = Avx.mm256_loadu_si256(above + x);
                        v256 c = Avx.mm256_loadu_si256(current + x);
                        Avx.mm256_storeu_si256(current + x, Avx2.mm256_add_epi32(c, a));
                    }
                }

                for (; x < dimension; x++)
                    current[x] += above[x];
            }
        }
    }
}