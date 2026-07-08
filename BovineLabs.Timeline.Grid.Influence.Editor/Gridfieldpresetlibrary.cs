using BovineLabs.Core.Asset;
using System.Collections.Generic;
using System.IO;
using BovineLabs.Timeline.Grid.Influence.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using UnityEditor;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Editor
{
    public static class GridFieldPresetLibrary
    {
        private const string FieldFolder = "Assets/Settings/Schemas/GridFields";
        private const string StampFolder = "Assets/Settings/Schemas/GridStamps";

        private static readonly FieldPreset[] FieldPresets =
        {
            new("ThreatField", 5, 120, true, 60, 4),
            new("VisionField", 5, 1, false, 0, 1),
            new("TerritoryField", 6, 600, true, 5, 8),
            new("ObjectiveField", 4, 300, false, 0, 1),
            new("FlowField", 6, 600, true, 20, 3)
        };

        private static readonly StampPreset[] StampPresets =
        {
            new("SmallDisc", ShapeKind.Disc, 1, 3, 0),
            new("MediumDisc", ShapeKind.Disc, 1, 6, 0),
            new("LargeDisc", ShapeKind.Disc, 1, 12, 0),
            new("ThreatRing", ShapeKind.Annulus, 1, 8, 6),
            new("ObjectivePulse", ShapeKind.Disc, 5, 4, 0)
        };

        [MenuItem("BovineLabs/Grid/Generate Preset Library")]
        public static void Generate()
        {
            EnsureFolder(FieldFolder);
            EnsureFolder(StampFolder);

            foreach (var preset in FieldPresets)
                CreateField(preset);

            foreach (var preset in StampPresets)
                CreateStamp(preset);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Reload to pick up AutoRef/IUID id assignments before registering into settings.
            var fields = new List<GridFieldSchemaObject>();
            foreach (var preset in FieldPresets)
                fields.Add(AssetDatabase.LoadAssetAtPath<GridFieldSchemaObject>($"{FieldFolder}/{preset.Name}.asset"));

            var stamps = new List<GridStampSchemaObject>();
            foreach (var preset in StampPresets)
                stamps.Add(AssetDatabase.LoadAssetAtPath<GridStampSchemaObject>($"{StampFolder}/{preset.Name}.asset"));

            var settings = GridInfluenceValidation.FindFirstSettings();
            if (settings == null)
            {
                Debug.LogWarning(
                    "Grid preset library: created presets but found no InfluenceGridSettingsAuthoring asset to register them into. Create one (BovineLabs/Grid settings) and re-run, or the new fields will silently no-op.");
                return;
            }

            settings.Fields = MergeFields(settings.Fields, fields);
            settings.Stamps = MergeStamps(settings.Stamps, stamps);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            var issues = new List<GridInfluenceValidation.Issue>();
            GridInfluenceValidation.Validate(settings, issues);
            GridInfluenceValidation.Report(issues, "Grid preset library validation");
        }

        private static void CreateField(in FieldPreset preset)
        {
            var path = $"{FieldFolder}/{preset.Name}.asset";
            if (AssetDatabase.LoadAssetAtPath<GridFieldSchemaObject>(path) != null)
                return;

            var asset = ScriptableObject.CreateInstance<GridFieldSchemaObject>();
            asset.FieldName = preset.Name;
            asset.ChunkPower = preset.ChunkPower;
            asset.RetentionFrames = preset.RetentionFrames;
            asset.DoubleBuffered = preset.DoubleBuffered;
            asset.DecayPerMille = preset.DecayPerMille;
            asset.SpreadDenominator = preset.SpreadDenominator;
            AssetDatabase.CreateAsset(asset, path);
        }

        private static void CreateStamp(in StampPreset preset)
        {
            var path = $"{StampFolder}/{preset.Name}.asset";
            if (AssetDatabase.LoadAssetAtPath<GridStampSchemaObject>(path) != null)
                return;

            var asset = ScriptableObject.CreateInstance<GridStampSchemaObject>();
            asset.Kind = preset.Kind;
            asset.BaseWeight = preset.BaseWeight;
            asset.DiscRadius = preset.OuterRadius;
            asset.AnnulusOuterRadius = preset.OuterRadius;
            asset.AnnulusInnerRadius = preset.InnerRadius;
            AssetDatabase.CreateAsset(asset, path);
        }

        private static GridFieldSchemaObject[] MergeFields(GridFieldSchemaObject[] existing,
            List<GridFieldSchemaObject> add)
        {
            var list = new List<GridFieldSchemaObject>();
            if (existing != null)
                foreach (var e in existing)
                    if (e != null && !list.Contains(e))
                        list.Add(e);
            foreach (var a in add)
                if (a != null && !list.Contains(a))
                    list.Add(a);
            return list.ToArray();
        }

        private static GridStampSchemaObject[] MergeStamps(GridStampSchemaObject[] existing,
            List<GridStampSchemaObject> add)
        {
            var list = new List<GridStampSchemaObject>();
            if (existing != null)
                foreach (var e in existing)
                    if (e != null && !list.Contains(e))
                        list.Add(e);
            foreach (var a in add)
                if (a != null && !list.Contains(a))
                    list.Add(a);
            return list.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = Path.GetFileName(path);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        private readonly struct FieldPreset
        {
            public readonly string Name;
            public readonly int ChunkPower;
            public readonly uint RetentionFrames;
            public readonly bool DoubleBuffered;
            public readonly int DecayPerMille;
            public readonly int SpreadDenominator;

            public FieldPreset(string name, int chunkPower, uint retentionFrames, bool doubleBuffered,
                int decayPerMille, int spreadDenominator)
            {
                Name = name;
                ChunkPower = chunkPower;
                RetentionFrames = retentionFrames;
                DoubleBuffered = doubleBuffered;
                DecayPerMille = decayPerMille;
                SpreadDenominator = spreadDenominator;
            }
        }

        private readonly struct StampPreset
        {
            public readonly string Name;
            public readonly ShapeKind Kind;
            public readonly int BaseWeight;
            public readonly int OuterRadius;
            public readonly int InnerRadius;

            public StampPreset(string name, ShapeKind kind, int baseWeight, int outerRadius, int innerRadius)
            {
                Name = name;
                Kind = kind;
                BaseWeight = baseWeight;
                OuterRadius = outerRadius;
                InnerRadius = innerRadius;
            }
        }
    }
}