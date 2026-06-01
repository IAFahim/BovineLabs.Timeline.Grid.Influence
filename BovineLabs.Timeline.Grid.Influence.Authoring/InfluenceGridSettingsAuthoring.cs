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
        [Tooltip("Size of a single grid cell in world units.")]
        public float CellSize = 1f;

        [Tooltip("Normal vector of the grid plane.")]
        public Vector3 PlaneNormal = Vector3.up;

        [Tooltip("Power of two exponent for chunk size (e.g., 4 = 16x16 chunks).")]
        [Range(1, 8)]
        public int ChunkSizePowerOfTwo = 4;

        [Tooltip("Number of frames to retain inactive chunks before their slots can be reused.")]
        [Min(0)]
        public int ChunkRetentionFrames = 300;

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);
            baker.AddComponent(entity, new InfluenceGridSettings
            {
                CellSize = math.max(0.01f, CellSize),
                PlaneNormal = math.normalizesafe(PlaneNormal, math.up()),
                ChunkSizePowerOfTwo = ChunkSizePowerOfTwo,
                ChunkRetentionFrames = (uint)math.max(0, ChunkRetentionFrames)
            });
        }
    }
}
