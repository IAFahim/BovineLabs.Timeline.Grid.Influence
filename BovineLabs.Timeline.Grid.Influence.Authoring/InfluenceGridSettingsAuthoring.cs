using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Settings;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [SettingsGroup("Grid")]
    public class InfluenceGridSettingsAuthoring : SettingsBase
    {
        [Tooltip("World units per grid cell.")]
        public float CellSize = 1f;

        [Tooltip("Normal of the grid plane.")]
        public Vector3 PlaneNormal = Vector3.up;

        [Tooltip("Chunk edge = 2^this (4 = 16x16).")]
        [Range(1, 8)]
        public int ChunkSizePowerOfTwo = 4;

        [Tooltip("Frames an idle chunk's slot is retained before reuse.")]
        [Min(0)]
        public int ChunkRetentionFrames = 300;

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);
            baker.AddComponent(entity, new InfluenceGridSettings
            {
                CellSize = math.max(0.01f, CellSize),
                PlaneNormal = math.normalizesafe(PlaneNormal, math.up()),
                ChunkSizePowerOfTwo = math.clamp(ChunkSizePowerOfTwo, 1, 8),
                ChunkRetentionFrames = (uint)math.max(0, ChunkRetentionFrames)
            });
        }
    }
}
