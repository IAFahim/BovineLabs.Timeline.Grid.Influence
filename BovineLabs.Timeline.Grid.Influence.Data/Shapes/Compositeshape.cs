using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct CompositeShapeBlob
    {
        public BlobArray<InfluenceShape> Layers;
    }

    public static class CompositeBaker
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MaxInset(in InfluenceShape shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Disc:
                    return math.max(0, shape.DiscRadius);

                case ShapeKind.Annulus:
                    return math.max(0, shape.AnnulusOuterRadius - math.max(0, shape.AnnulusInnerRadius) - 1);

                case ShapeKind.Capsule:
                    return math.max(0, shape.CapsuleRadius);

                case ShapeKind.ThickLine:
                    return math.max(0, shape.ThickLineRadius);

                case ShapeKind.Ellipse:
                    return math.max(0, math.min(shape.EllipseRadii.x, shape.EllipseRadii.y));

                case ShapeKind.SolidRect:
                    return HalfMinExtent(shape.RectSize);

                case ShapeKind.RectShell:
                    return HalfMinExtent(shape.ShellSize);

                case ShapeKind.RoundedRect:
                    return HalfMinExtent(shape.RoundedRectSize);

                default:
                    return 0;
            }
        }

        public static int LayerCount(in InfluenceShape baseShape, NativeArray<int> targetPerDepth)
        {
            return Fill(baseShape, targetPerDepth, default, false);
        }

        public static int Fill(in InfluenceShape baseShape, NativeArray<int> targetPerDepth,
            NativeArray<InfluenceShape> outLayers, bool write)
        {
            var depthLimit = math.min(targetPerDepth.Length, MaxInset(baseShape) + 1);
            var count = 0;
            var carried = 0;

            for (var depth = 0; depth < depthLimit; depth++)
            {
                var delta = targetPerDepth[depth] - carried;
                carried = targetPerDepth[depth];
                if (delta == 0)
                    continue;

                var layer = baseShape.Inset(depth).WithWeight(delta);
                if (Rasterizer.Bounds(layer, int2.zero).IsEmpty)
                    continue;

                if (write)
                    outLayers[count] = layer;
                count++;
            }

            return count;
        }

        public static BlobAssetReference<CompositeShapeBlob> Build(in InfluenceShape baseShape,
            NativeArray<int> targetPerDepth, Allocator allocator)
        {
            using var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<CompositeShapeBlob>();

            var count = LayerCount(baseShape, targetPerDepth);
            var array = builder.Allocate(ref root.Layers, count);

            var written = new NativeArray<InfluenceShape>(math.max(1, count), Allocator.Temp);
            Fill(baseShape, targetPerDepth, written, true);
            for (var i = 0; i < count; i++)
                array[i] = written[i];
            written.Dispose();

            return builder.CreateBlobAssetReference<CompositeShapeBlob>(allocator);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HalfMinExtent(int2 size)
        {
            return math.max(0, (math.min(size.x, size.y) - 1) >> 1);
        }
    }

    public static class CompositeShapeReader
    {
        public static CellRect Bounds(ref CompositeShapeBlob composite, int2 origin)
        {
            ref var layers = ref composite.Layers;
            var any = false;
            var lo = int2.zero;
            var hi = int2.zero;

            for (var i = 0; i < layers.Length; i++)
            {
                var bounds = Rasterizer.Bounds(layers[i], origin);
                if (bounds.IsEmpty)
                    continue;

                if (!any)
                {
                    lo = bounds.Min;
                    hi = bounds.Max;
                    any = true;
                }
                else
                {
                    lo = math.min(lo, bounds.Min);
                    hi = math.max(hi, bounds.Max);
                }
            }

            return any ? new CellRect(lo, hi) : CellRect.Empty;
        }

        public static int EstimateSpanCount(ref CompositeShapeBlob composite)
        {
            ref var layers = ref composite.Layers;
            var total = 0;
            for (var i = 0; i < layers.Length; i++)
                total = IntegerMath.SaturatingAdd(total, Rasterizer.EstimateSpanCount(layers[i]));

            return total;
        }

        public static void Emit(ref CompositeShapeBlob composite, int2 origin, ref SpanSink sink)
        {
            ref var layers = ref composite.Layers;
            for (var i = 0; i < layers.Length; i++)
                Rasterizer.Emit(new Stamp(layers[i], origin), ref sink);
        }
    }
}