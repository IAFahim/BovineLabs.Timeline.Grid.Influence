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
        public float CellSize = 1f;
        public Vector3 PlaneNormal = Vector3.up;
        public int StrideAlignment = 4;

        public GridFieldSchemaObject[] Fields = System.Array.Empty<GridFieldSchemaObject>();

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);

            baker.AddComponent(entity, new InfluenceGridSettings
            {
                CellSize = math.max(0.01f, CellSize),
                PlaneNormal = math.normalizesafe(PlaneNormal, math.up()),
                StrideAlignment = StrideAlignment
            });

            var buffer = baker.AddBuffer<GridFieldConfigData>(entity);
            foreach (var field in Fields)
            {
                if (field == null) continue;
                baker.DependsOn(field);

                buffer.Add(new GridFieldConfigData
                {
                    Key = field.Id,
                    Name = field.FieldName,
                    ChunkPower = math.clamp(field.ChunkPower, 1, 8),
                    RetentionFrames = field.RetentionFrames,
                    DoubleBuffered = field.DoubleBuffered,
                    DecayPerMille = field.DecayPerMille,
                    SpreadDenominator = field.SpreadDenominator,
                    StrideAlignment = StrideAlignment
                });
            }
        }
    }
}
