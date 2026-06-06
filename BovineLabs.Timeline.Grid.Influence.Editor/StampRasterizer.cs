using System.Collections.Generic;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    public static class StampRasterizer
    {
        public static bool TryAccumulate(IReadOnlyList<InfluenceShape> shapes, out int[] grid, out int2 min,
            out int2 size)
        {
            grid = null;
            min = default;
            size = default;

            if (shapes == null || shapes.Count == 0)
                return false;

            var any = false;
            int2 lo = default;
            int2 hi = default;

            foreach (var shape in shapes)
            {
                var b = Rasterizer.Bounds(shape, int2.zero);
                if (b.IsEmpty)
                    continue;

                if (!any)
                {
                    lo = b.Min;
                    hi = b.Max;
                    any = true;
                }
                else
                {
                    lo = math.min(lo, b.Min);
                    hi = math.max(hi, b.Max);
                }
            }

            if (!any)
                return false;

            size = hi - lo;
            if (size.x <= 0 || size.y <= 0)
                return false;

            min = lo;
            grid = new int[size.x * size.y];

            foreach (var shape in shapes)
                Accumulate(shape, lo, size, grid);

            return true;
        }

        public static bool TryAccumulate(in InfluenceShape shape, out int[] grid, out int2 min, out int2 size)
        {
            return TryAccumulate(new[] { shape }, out grid, out min, out size);
        }

        private static unsafe void Accumulate(in InfluenceShape shape, int2 min, int2 size, int[] grid)
        {
            var capacity = Rasterizer.EstimateSpanCount(shape);
            if (capacity <= 0)
                return;

            var spans = new NativeArray<WeightedRect>(capacity, Allocator.Temp);
            try
            {
                var sink = new SpanSink((WeightedRect*)spans.GetUnsafePtr(), capacity);
                Rasterizer.Emit(new Stamp(shape, int2.zero), ref sink);

                for (var i = 0; i < sink.Count; i++)
                {
                    var span = spans[i];
                    if (span.IsEmpty)
                        continue;

                    var x0 = math.max(span.Bounds.Min.x, min.x);
                    var y0 = math.max(span.Bounds.Min.y, min.y);
                    var x1 = math.min(span.Bounds.Max.x, min.x + size.x);
                    var y1 = math.min(span.Bounds.Max.y, min.y + size.y);

                    for (var y = y0; y < y1; y++)
                    for (var x = x0; x < x1; x++)
                        grid[x - min.x + (y - min.y) * size.x] += span.Weight;
                }
            }
            finally
            {
                spans.Dispose();
            }
        }
    }
}