using BovineLabs.Core.EntityCommands;
using BovineLabs.Timeline.Physics;

namespace BovineLabs.Timeline.Grid.Influence.Data.Builders
{
    public struct GridFlowSteeringBuilder
    {
        public ushort FieldKey;
        public InfluenceShape SamplerShape;
        public int Polarity;
        public PhysicsForceMode Mode;
        public float Strength;

        public void ApplyTo<T>(ref T builder)
            where T : struct, IEntityCommands
        {
            builder.AddComponent(new GridFlowSteeringAnimated
            {
                AuthoredData = new GridFlowSteeringData
                {
                    FieldKey = FieldKey,
                    SamplerShape = SamplerShape,
                    Polarity = Polarity,
                    Mode = Mode,
                    Strength = Strength
                }
            });
        }
    }
}