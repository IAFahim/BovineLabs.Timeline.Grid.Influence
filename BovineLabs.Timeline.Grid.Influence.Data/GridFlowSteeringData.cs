using BovineLabs.Timeline.Data;
using BovineLabs.Timeline.Physics;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Properties;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct GridFlowSteeringData
    {
        public ushort FieldKey;
        public InfluenceShape SamplerShape;
        public int Polarity;
        public PhysicsForceMode Mode;
        public float Strength;
    }

    public struct GridFlowSteeringAnimated : IAnimatedComponent<GridFlowSteeringData>
    {
        public GridFlowSteeringData AuthoredData;
        [CreateProperty] public GridFlowSteeringData Value { get; set; }
    }

    public struct ActiveGridFlowSteering : IComponentData, IEnableableComponent
    {
        public GridFlowSteeringData Config;
    }

    public struct GridFlowSteeringState : IComponentData
    {
        public bool Fired;
    }

    public readonly struct GridFlowSteeringMixer : IMixer<GridFlowSteeringData>
    {
        public GridFlowSteeringData Lerp(in GridFlowSteeringData a, in GridFlowSteeringData b, in float s)
        {
            return new GridFlowSteeringData
            {
                FieldKey = s < 0.5f ? a.FieldKey : b.FieldKey,
                SamplerShape = s < 0.5f ? a.SamplerShape : b.SamplerShape,
                Polarity = s < 0.5f ? a.Polarity : b.Polarity,
                Mode = s < 0.5f ? a.Mode : b.Mode,
                Strength = math.lerp(a.Strength, b.Strength, s)
            };
        }

        public GridFlowSteeringData Add(in GridFlowSteeringData a, in GridFlowSteeringData b)
        {
            return new GridFlowSteeringData
            {
                FieldKey = a.FieldKey,
                SamplerShape = a.SamplerShape,
                Polarity = a.Polarity,
                Mode = a.Mode,
                Strength = a.Strength + b.Strength
            };
        }
    }
}
