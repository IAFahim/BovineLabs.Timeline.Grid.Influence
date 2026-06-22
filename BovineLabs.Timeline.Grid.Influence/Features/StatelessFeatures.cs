using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Features
{
    public struct TerritoryField : IComponentData
    {
        public FieldId Id;
    }

    public static class TerritoryReader
    {
        public static int Controller(ref FieldRegistry r, FieldId id, int2 cell)
        {
            var v = r.Front(id).AsReader().ReadCell(cell);
            return v == 0 ? 0 : v > 0 ? 1 : -1;
        }

        public static bool IsFrontline(ref FieldRegistry r, FieldId id, int2 cell, int band)
        {
            return math.abs(r.Front(id).AsReader().ReadCell(cell)) <= band;
        }
    }

    public struct VisionField : IComponentData
    {
        public FieldId Id;
    }

    public static class VisionReader
    {
        public static bool IsSeen(ref FieldRegistry r, FieldId id, int2 cell)
        {
            return r.Front(id).AsReader().ReadCell(cell) > 0;
        }

        public static bool InShadow(ref FieldRegistry r, FieldId id, int2 cell)
        {
            return r.Front(id).AsReader().ReadCell(cell) == 0;
        }
    }

    public struct CoverageField : IComponentData
    {
        public FieldId Id;
    }

    public static class CaptureScoring
    {
        public static int Score(ref FieldRegistry r, FieldId presenceId, int2 min, int2 size)
        {
            var reader = r.Front(presenceId).AsReader();
            var total = 0;
            for (var y = 0; y < size.y; y++)
            for (var x = 0; x < size.x; x++)
                total += reader.ReadCell(new int2(min.x + x, min.y + y));
            return total;
        }
    }

    public static class FlowSteering
    {
        public static int2 Direction(ref FieldRegistry r, FieldId potentialId, int2 cell)
        {
            var rd = r.Front(potentialId).AsReader();
            var gx = rd.ReadCell(cell + new int2(1, 0)) - rd.ReadCell(cell + new int2(-1, 0));
            var gy = rd.ReadCell(cell + new int2(0, 1)) - rd.ReadCell(cell + new int2(0, -1));
            return new int2(-gx, -gy);
        }
    }

    public static class PlacementQuery
    {
        public static bool IsValid(ref FieldRegistry r, FieldId threatId, int2 min, int2 size, int maxThreatTolerance)
        {
            var rd = r.Front(threatId).AsReader();
            for (var y = 0; y < size.y; y++)
            for (var x = 0; x < size.x; x++)
                if (rd.ReadCell(new int2(min.x + x, min.y + y)) > maxThreatTolerance)
                    return false;
            return true;
        }
    }
}