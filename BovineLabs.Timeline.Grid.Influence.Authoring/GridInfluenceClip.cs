using System;
using System.Collections.Generic;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public sealed class GridInfluenceClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

        [Header("Schemas")] public GridFieldSchemaObject Field;

        public GridStampSchemaObject Stamp;

        public GridStampSchemaObject[] ExtraStamps = Array.Empty<GridStampSchemaObject>();

        public GridCompositeSchemaObject Composite;

        [Header("Semantics")]
        [Tooltip("Additive adds the stamp/composite weights to the field; Subtractive negates them.")]
        public Polarity Polarity = Polarity.Additive;

        [Header("Footprint")] public Quarter Rotation = Quarter.R0;

        public FalloffMode Falloff = FalloffMode.None;
        [Min(0)] public int FalloffSteps = 3;
        public int FalloffSpacing = 2;

        [Header("Transform")]
        [Tooltip(
            "Scales stamp weights. On composite clips it scales the per-depth CUMULATIVE weights (rounded per layer), so inter-layer deltas can differ slightly from the single-stamp path. Field weights are plain integers: keep the effective |BaseWeight * WeightMultiplier| >= 8-10, as smaller weights quantize to 0 during clip blending and pop in/out.")]
        public float WeightMultiplier = 1.0f;

        public Vector3 LocalOffset;

        [Header("Display")]
        [Tooltip(
            "Editor-only: tints the clip and gizmo for visual grouping. Has no effect on runtime field routing, which is determined solely by the assigned Field schema.")]
        public GridFieldCategory Category = GridFieldCategory.Generic;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (!HasSchemas())
                return;

            context.Baker.DependsOn(Field);
            DependOnStamps(context);
            BindOriginTransform(context);

            WarnCompositePrecedence();

            var compositeBlob = default(BlobAssetReference<CompositeShapeBlob>);
            if (Composite != null && Composite.Base != null)
            {
                context.Baker.DependsOn(Composite);
                if (CompositeBaking.TryBuild(Composite, Polarity.Sign(), WeightMultiplier, Rotation, this, true,
                        out compositeBlob))
                    context.Baker.AddBlobAsset(ref compositeBlob, out _);
                else
                    compositeBlob = default;
            }

            var hasComposite = compositeBlob.IsCreated;

            if (!hasComposite)
                WarnLowPrimaryWeight();

            var primaryShapes = new List<InfluenceShape>(1);
            if (!hasComposite)
                GridInfluenceExpansion.CollectStamp(this, Stamp, primaryShapes);

            var extraShapes = new List<InfluenceShape>(ExtraStamps?.Length ?? 0);
            if (ExtraStamps != null)
                foreach (var extra in ExtraStamps)
                    GridInfluenceExpansion.CollectStamp(this, extra, extraShapes);

            if (primaryShapes.Count == 0 && extraShapes.Count == 0 && !hasComposite)
                return;

            var builder = new GridInfluenceBuilder
            {
                FieldKey = Field.Id,
                Shape = primaryShapes.Count > 0 ? primaryShapes[0] : default,
                Composite = compositeBlob,
                LocalOffset = LocalOffset,
                Origin = EntityLinkAuthoringUtility.BakeRef(context.Baker, originLink, originTarget)
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            var buffer = commands.AddBuffer<InfluenceStampElement>();
            for (var i = 1; i < primaryShapes.Count; i++)
                buffer.Add(new InfluenceStampElement { Shape = primaryShapes[i] });
            foreach (var extra in extraShapes)
                buffer.Add(new InfluenceStampElement { Shape = extra });

            base.Bake(clipEntity, context);
        }

        private void WarnCompositePrecedence()
        {
            if (Composite == null)
                return;

            if (Composite.Base == null)
            {
                Debug.LogWarning(
                    $"GridInfluenceClip '{name}' has a Composite with no Base shape; the composite is ignored and the clip falls back to the Stamp.",
                    this);
                return;
            }

            if (Stamp != null || Falloff != FalloffMode.None)
                Debug.LogWarning(
                    $"GridInfluenceClip '{name}' uses a Composite base: the primary Stamp is not stamped and Falloff is not applied to composite layers. (Rotation and ExtraStamps still apply.)",
                    this);
        }

        private void WarnLowPrimaryWeight()
        {
            if (Stamp == null || Stamp.Kind == ShapeKind.Painted)
                return;

            var effective = Mathf.Abs(Mathf.RoundToInt(Stamp.BaseWeight * WeightMultiplier));
            if (effective is >= 1 and <= 7)
                Debug.LogWarning(
                    $"GridInfluenceClip '{name}' primary stamp effective weight is {effective}; small integer weights quantize badly under clip blending (footprints pop in/out). Prefer BaseWeight >= 8-10.",
                    this);
        }

        private bool HasSchemas()
        {
            if (Field == null)
            {
                Debug.LogError($"GridInfluenceClip '{name}' has no Field schema assigned. Clip will be skipped.", this);
                return false;
            }

            var hasComposite = Composite != null && Composite.Base != null;
            if (Stamp == null && !hasComposite)
            {
                Debug.LogError($"GridInfluenceClip '{name}' has no Stamp or Composite schema assigned. Clip will be skipped.", this);
                return false;
            }

            return true;
        }

        private void DependOnStamps(BakingContext context)
        {
            context.Baker.DependsOn(Stamp);
            if (ExtraStamps == null)
                return;

            foreach (var extra in ExtraStamps)
                if (extra != null)
                    context.Baker.DependsOn(extra);
        }

        private void BindOriginTransform(BakingContext context)
        {
            if (context.Binding != null && context.Binding.Target != Entity.Null)
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);
        }
    }
}