namespace BovineLabs.Timeline.Grid.Influence.Data
{
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;

    [System.Serializable]
    public struct InfluenceFieldComponent : IComponentData
    {
        public InfluenceField Field;

        public void Create(int chunkSizePowerOfTwo = 4)
        {
            Field = InfluenceField.Create(chunkSizePowerOfTwo, AllocatorManager.Persistent);
        }

        public void Dispose()
        {
            if (Field.IsCreated)
            {
                Field.Dispose();
            }
        }
    }
}
