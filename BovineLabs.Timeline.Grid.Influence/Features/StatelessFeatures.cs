using Unity.Entities;
using Unity.Mathematics;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Fields;

namespace BovineLabs.Timeline.Grid.Influence.Features
{
    // ---- TERRITORY / FRONTLINE -------------------------------------------------------
    public struct TerritoryField : IComponentData { public FieldId Id; }
    public static class TerritoryReader
    {
        public static int Controller(in FieldRegistry r, FieldId id, int2 cell)
        { 
            int v = r.Front(id).AsReader().ReadCell(cell); 
            return v == 0 ? 0 : (v > 0 ? 1 : -1); 
        }
        
        public static bool IsFrontline(in FieldRegistry r, FieldId id, int2 cell, int band)
            => math.abs(r.Front(id).AsReader().ReadCell(cell)) <= band;
    }

    // ---- VISION / STEALTH ------------------------------------------------------------
    public struct VisionField : IComponentData { public FieldId Id; }
    public static class VisionReader
    {
        public static bool IsSeen(in FieldRegistry r, FieldId id, int2 cell)
            => r.Front(id).AsReader().ReadCell(cell) > 0;
            
        public static bool InShadow(in FieldRegistry r, FieldId id, int2 cell)
            => r.Front(id).AsReader().ReadCell(cell) == 0;
    }

    // ---- COVERAGE / PATROL HEATMAP ---------------------------------------------------
    public struct CoverageField : IComponentData { public FieldId Id; }

    // ---- CAPTURE-POINT / OBJECTIVE SCORING -------------------------------------------
    public static class CaptureScoring
    {
        public static int Score(in FieldRegistry r, FieldId presenceId, int2 min, int2 size)
        {
            var reader = r.Front(presenceId).AsReader();
            int total = 0;
            for (int y = 0; y < size.y; y++) 
            for (int x = 0; x < size.x; x++)
                total += reader.ReadCell(new int2(min.x + x, min.y + y));
            return total;
        }
    }

    // ---- FLOW-FIELD STEERING ---------------------------------------------------------
    public static class FlowSteering
    {
        public static int2 Direction(in FieldRegistry r, FieldId potentialId, int2 cell)
        {
            var rd = r.Front(potentialId).AsReader();
            int gx = rd.ReadCell(cell + new int2(1, 0)) - rd.ReadCell(cell + new int2(-1, 0));
            int gy = rd.ReadCell(cell + new int2(0, 1)) - rd.ReadCell(cell + new int2(0, -1));
            return new int2(-gx, -gy);
        }
    }

    // ---- PLACEMENT / SPAWN VALIDITY --------------------------------------------------
    public static class PlacementQuery
    {
        public static bool IsValid(in FieldRegistry r, FieldId threatId, int2 min, int2 size, int maxThreatTolerance)
        {
            var rd = r.Front(threatId).AsReader();
            for (int y = 0; y < size.y; y++) 
            for (int x = 0; x < size.x; x++)
            {
                if (rd.ReadCell(new int2(min.x + x, min.y + y)) > maxThreatTolerance)
                    return false;
            }
            return true;
        }
    }
}
