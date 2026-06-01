using Unity.Entities;
using Unity.Jobs;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct InfluenceGridDependency : IComponentData
    {
        public JobHandle Value;
    }
}
