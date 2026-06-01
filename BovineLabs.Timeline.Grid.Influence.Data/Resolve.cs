using Unity.Burst;

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
            for (int y = 1; y < dimension; y++)
            {
                int* above = field + (y - 1) * stride;
                int* current = field + y * stride;
                for (int x = 0; x < dimension; x++)
                {
                    current[x] += above[x];
                }
            }
        }
    }
}