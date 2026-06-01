
namespace BovineLabs.Timeline.Grid.Influence.Data
{
    using BovineLabs.Timeline.Data;
    using Unity.Mathematics;
    using Unity.Properties;

    public struct InfluenceAnimated : IAnimatedComponent<InfluenceData>
    {
        public InfluenceData AuthoredData;

        [CreateProperty] public InfluenceData Value { get; set; }
    }

    [System.Serializable]
    public readonly struct InfluenceMixer : IMixer<InfluenceData>
    {
        public InfluenceData Lerp(in InfluenceData a, in InfluenceData b, in float s)
        {
            return s >= 0.5f ? b : a;
        }
        
        public InfluenceData Add(in InfluenceData a, in InfluenceData b) => b;
    }
}
