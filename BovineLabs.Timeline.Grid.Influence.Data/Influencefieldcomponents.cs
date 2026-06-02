using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Grid.Influence
{
    public struct InfluenceFieldSingleton : IComponentData
    {
        public InfluenceField Field;
    }

    public struct InfluenceFieldDependency : IComponentData
    {
        public JobHandle Value;
    }
}