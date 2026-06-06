using BovineLabs.Core.EntityCommands;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Mathematics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridFlowSteeringBuilder
    {
        public ushort FieldKey;
        public float3 LocalOffset;
        public FlowBias Bias;
        public float MaxSpeed;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new GridFlowSteeringData
            {
                FieldKey = FieldKey,
                LocalOffset = LocalOffset,
                Bias = Bias,
                MaxSpeed = MaxSpeed
            });
        }
    }
}