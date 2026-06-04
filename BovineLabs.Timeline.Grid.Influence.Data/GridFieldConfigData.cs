using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    public struct GridFieldConfigData : IBufferElementData
    {
        public ushort Key;
        public FixedString64Bytes Name;
        public int ChunkPower;
        public uint RetentionFrames;
        public bool DoubleBuffered;
        public int DecayPerMille;
        public int SpreadDenominator;
        public int StrideAlignment;
    }
}