using System;
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
    public sealed class GridCompositeClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

        [Header("Schemas")] public GridFieldSchemaObject Field;

        public GridCompositeSchemaObject Composite;

        [Header("Semantics")] public Polarity Polarity = Polarity.Additive;

        [Header("Transform")] public Vector3 LocalOffset;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (!HasSchemas())
                return;

            context.Baker.DependsOn(Field);
            context.Baker.DependsOn(Composite);
            context.Baker.DependsOn(Composite.Base);
            BindOriginTransform(context);

            if (!CompositeBaking.TryBuild(Composite, Polarity.Sign(), 1f, Quarter.R0, this, false, out var blob))
                return;

            context.Baker.AddBlobAsset(ref blob, out _);

            var builder = new GridCompositeBuilder
            {
                FieldKey = Field.Id,
                Composite = blob,
                LocalOffset = LocalOffset,
                Origin = EntityLinkAuthoringUtility.BakeRef(context.Baker, originLink, originTarget)
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);
            commands.AddBuffer<InfluenceStampElement>();

            base.Bake(clipEntity, context);
        }

        private bool HasSchemas()
        {
            if (Field == null)
            {
                Debug.LogError($"GridCompositeClip '{name}' has no Field schema assigned. Clip will be skipped.", this);
                return false;
            }

            if (Composite == null || Composite.Base == null)
            {
                Debug.LogError($"GridCompositeClip '{name}' has no Composite schema assigned. Clip will be skipped.",
                    this);
                return false;
            }

            return true;
        }

        private void BindOriginTransform(BakingContext context)
        {
            if (context.Binding != null && context.Binding.Target != Entity.Null)
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);
        }
    }
}