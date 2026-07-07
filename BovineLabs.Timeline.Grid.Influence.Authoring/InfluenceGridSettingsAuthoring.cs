using System;
using BovineLabs.Core.Authoring.Settings;
using BovineLabs.Core.Settings;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [SettingsGroup("Grid")]
    public class InfluenceGridSettingsAuthoring : SettingsBase
    {
        [Tooltip("World-space size of one grid cell. Clamped to a minimum of 0.01.")]
        public float CellSize = 1f;

        [Tooltip("Normal of the plane the grid is projected onto.")]
        public Vector3 PlaneNormal = Vector3.up;

        [Min(8)]
        [Tooltip(
            "Row stride alignment (elements) for each field's grid. Floored at 8 and rounded up to the next power of two, so values below 8 are promoted to 8.")]
        public int StrideAlignment = 8;

        public GridFieldSchemaObject[] Fields = Array.Empty<GridFieldSchemaObject>();
        public GridStampSchemaObject[] Stamps = Array.Empty<GridStampSchemaObject>();

        public override void Bake(Baker<SettingsAuthoring> baker)
        {
            var entity = baker.GetEntity(TransformUsageFlags.None);

            baker.AddComponent(entity, new InfluenceGridSettings
            {
                CellSize = math.max(0.01f, CellSize),
                PlaneNormal = math.normalizesafe(PlaneNormal, math.up())
            });

            var buffer = baker.AddBuffer<GridFieldConfigData>(entity);
            foreach (var field in Fields)
            {
                if (field == null) continue;
                baker.DependsOn(field);

                var name = default(FixedString64Bytes);
                if (name.CopyFromTruncated(field.FieldName ?? string.Empty) == CopyError.Truncation)
                    Debug.LogWarning(
                        $"InfluenceGridSettingsAuthoring: field name '{field.FieldName}' exceeds the 61-byte FixedString64Bytes limit and was truncated.",
                        field);

                buffer.Add(new GridFieldConfigData
                {
                    Key = field.Id,
                    Name = name,
                    ChunkPower = math.clamp(field.ChunkPower, 1, 8),
                    RetentionFrames = field.RetentionFrames,
                    DoubleBuffered = field.DoubleBuffered,
                    DecayPerMille = field.DecayPerMille,
                    SpreadDenominator = math.max(1, field.SpreadDenominator),
                    StrideAlignment = StrideAlignment
                });
            }
        }
    }
}