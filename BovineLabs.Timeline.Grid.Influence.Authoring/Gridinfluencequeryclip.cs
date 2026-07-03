using System;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data.Builders;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public sealed class GridInfluenceQueryClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

        [Header("Schemas")] public GridFieldSchemaObject Field;

        [Header("Transform")] public Vector3 LocalOffset;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (Field == null)
            {
                Debug.LogError($"GridInfluenceQueryClip '{name}' has no Field schema assigned. Clip will be skipped.",
                    this);
                return;
            }

            context.Baker.DependsOn(Field);

            if (context.Binding != null && context.Binding.Target != Entity.Null)
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);

            var builder = new GridInfluenceQueryBuilder
            {
                FieldKey = Field.Id,
                LocalOffset = LocalOffset,
                Origin = EntityLinkAuthoringUtility.BakeRef(context.Baker, originLink, originTarget)
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}