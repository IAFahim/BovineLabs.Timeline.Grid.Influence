using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using BovineLabs.Timeline.Grid.Influence.Data;

namespace BovineLabs.Timeline.Grid.Influence
{
    [BurstCompile]
    public struct ResizeStampListJob : IJob
    {
        public NativeList<Stamp> List;
        public int MinCapacity;
        
        public void Execute()
        {
            if (List.Capacity < MinCapacity)
            {
                List.Capacity = MinCapacity;
            }
        }
    }
}
