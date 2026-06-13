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
        public int FalloffSteps = 3;
        public int FalloffSpacing = 2;

        [Header("Transform")] public float WeightMultiplier = 1.0f;

        public Vector3 LocalOffset;

        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

        [Header("Display")]
        [Tooltip("Editor-only: tints the clip and gizmo for visual grouping. Has no effect on runtime field routing, which is determined solely by the assigned Field schema.")]
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

            var compositeBlob = default(BlobAssetReference<CompositeShapeBlob>);
            if (Composite != null && Composite.Base != null)
            {
                context.Baker.DependsOn(Composite);
                if (TryBuildComposite(out compositeBlob))
                    context.Baker.AddBlobAsset(ref compositeBlob, out _);
                else
                    compositeBlob = default;
            }

            var shapes = new List<InfluenceShape>(1 + (ExtraStamps?.Length ?? 0));
            GridInfluenceExpansion.Collect(this, shapes);
            if (shapes.Count == 0 && !compositeBlob.IsCreated)
                return;

            var builder = new GridInfluenceBuilder
            {
                FieldKey = Field.Id,
                Shape = shapes.Count > 0 ? shapes[0] : default,
                Composite = compositeBlob,
                LocalOffset = LocalOffset,
                OriginTarget = originTarget,
                OriginLinkKey = ResolveLinkKey()
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            var buffer = commands.AddBuffer<InfluenceStampElement>();
            for (var i = 1; i < shapes.Count; i++)
                buffer.Add(new InfluenceStampElement { Shape = shapes[i] });

            base.Bake(clipEntity, context);
        }

        private bool TryBuildComposite(out BlobAssetReference<CompositeShapeBlob> blob)
        {
            var baseShape = Composite.Base.BuildShape(1f).WithWeight(1);
            var weights = Composite.Profile.SampleDepthWeights(baseShape, Allocator.Temp);

            var sign = Polarity.Sign();
            if (sign != 1)
                for (var i = 0; i < weights.Length; i++)
                    weights[i] *= sign;

            blob = CompositeBaker.Build(baseShape, weights, Allocator.Persistent);
            weights.Dispose();

            return blob.IsCreated && blob.Value.Layers.Length > 0;
        }

        private bool HasSchemas()
        {
            if (Field == null)
            {
                Debug.LogError($"GridInfluenceClip '{name}' has no Field schema assigned. Clip will be skipped.", this);
                return false;
            }

            if (Stamp == null)
            {
                Debug.LogError($"GridInfluenceClip '{name}' has no Stamp schema assigned. Clip will be skipped.", this);
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