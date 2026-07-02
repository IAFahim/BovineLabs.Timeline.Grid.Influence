using System;
using System.Collections.Generic;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Builders;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public sealed class GridInfluenceClip : DOTSClip, ITimelineClipAsset
    {
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
            "Scales stamp weights. On composite clips it scales the per-depth CUMULATIVE weights (rounded per layer), so inter-layer deltas can differ slightly from the single-stamp path.")]
        public float WeightMultiplier = 1.0f;

        public Vector3 LocalOffset;

        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

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
            context.Baker.DependsOn(originLink);
            DependOnStamps(context);
            BindOriginTransform(context);

            var compositeBlob = default(BlobAssetReference<CompositeShapeBlob>);
            if (Composite != null && Composite.Base != null)
            {
                context.Baker.DependsOn(Composite);
                if (TryBuildComposite(out compositeBlob))
                    context.Baker.AddBlobAsset(ref compositeBlob, out _);
                else
                    compositeBlob = default;
            }

            var hasComposite = compositeBlob.IsCreated;

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
                OriginTarget = originTarget,
                OriginLinkKey = ResolveLinkKey()
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

        private bool TryBuildComposite(out BlobAssetReference<CompositeShapeBlob> blob)
        {
            blob = default;

            if (Composite.Base.Kind == ShapeKind.Painted)
            {
                Debug.LogWarning($"GridInfluenceClip '{name}' uses a Painted stamp as its Composite base. " +
                                 "Painted stamps have no composite form; the composite is skipped. Use a parametric base shape.",
                    this);
                return false;
            }

            var baseShape = Composite.Base.BuildShape(1f).WithWeight(1);
            var weights = Composite.Profile.SampleDepthWeights(baseShape, Allocator.Temp);

            var sign = Polarity.Sign();
            var anyNonZero = false;
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = Mathf.RoundToInt(weights[i] * WeightMultiplier) * sign;
                anyNonZero |= weights[i] != 0;
            }

            if (!anyNonZero && weights.Length > 0)
                Debug.LogWarning(
                    $"GridInfluenceClip '{name}' WeightMultiplier ({WeightMultiplier}) rounded every composite layer weight to 0; the composite contributes nothing. Raise WeightMultiplier or the base weights.",
                    this);

            blob = CompositeBaker.Build(baseShape, weights, Allocator.Persistent);
            weights.Dispose();

            if (!(blob.IsCreated && blob.Value.Layers.Length > 0))
            {
                if (blob.IsCreated)
                    blob.Dispose();

                blob = default;
                return false;
            }

            return true;
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

        private ushort ResolveLinkKey()
        {
            return originLink != null && EntityLinkAuthoringUtility.TryGetKey(originLink, out var key)
                ? key
                : (ushort)0;
        }

        private void BindOriginTransform(BakingContext context)
        {
            if (context.Binding != null && context.Binding.Target != Entity.Null)
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);
        }
    }
}