#if UNITY_EDITOR || BL_DEBUG
using BovineLabs.Core;
using BovineLabs.Quill;
using BovineLabs.Timeline.Core.Debug;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Debug
{
    // Shared camera-frustum culling for the debug systems: builds a world-space AABB from a grid-space rect (with a
    // one-cell margin) and tests it against the active camera. Burst-callable, no managed state.
    internal static class GridDebugCulling
    {
        public static bool RectVisible(in GridBasis basis, in CameraCulling cameraCulling, float cellSize,
            float2 minGrid, float2 maxGrid, float heightOffset)
        {
            var p0 = basis.ToWorldSpace(minGrid, heightOffset);
            var p1 = basis.ToWorldSpace(new float2(maxGrid.x, minGrid.y), heightOffset);
            var p2 = basis.ToWorldSpace(maxGrid, heightOffset);
            var p3 = basis.ToWorldSpace(new float2(minGrid.x, maxGrid.y), heightOffset);

            var min = math.min(math.min(p0, p1), math.min(p2, p3));
            var max = math.max(math.max(p0, p1), math.max(p2, p3));

            var margin = math.max(0.1f, cellSize);

            var aabb = new AABB
            {
                Center = (min + max) * 0.5f,
                Extents = (max - min) * 0.5f + new float3(margin),
            };

            return cameraCulling.AnyIntersect(aabb);
        }

        public static bool ChunkVisible(in GridBasis basis, in CameraCulling cameraCulling, float cellSize,
            int2 coord, int chunkSize, float heightOffset)
        {
            var origin = new float2(coord.x * chunkSize, coord.y * chunkSize) * cellSize;
            var edge = chunkSize * cellSize;
            return RectVisible(basis, cameraCulling, cellSize, origin, origin + new float2(edge, edge), heightOffset);
        }
    }
}
#endif
