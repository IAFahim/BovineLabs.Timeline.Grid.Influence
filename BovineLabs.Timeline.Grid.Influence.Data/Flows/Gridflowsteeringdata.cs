using Unity.Entities;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Flows
{
    public enum FlowBias : byte
    {
        Descend,
        Ascend
    }

    public static class FlowBiasExtensions
    {
        public static int Sign(this FlowBias bias)
        {
            return bias == FlowBias.Descend ? -1 : 1;
        }
    }

    public struct GridFlowSteeringData : IComponentData
    {
        public ushort FieldKey;
        public float3 LocalOffset;
        public FlowBias Bias;
        public float MaxSpeed;
    }
}