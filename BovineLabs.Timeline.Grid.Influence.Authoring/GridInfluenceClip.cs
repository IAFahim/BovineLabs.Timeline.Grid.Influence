using System;
using BovineLabs.Reaction.Data.Core;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.EntityLinks.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
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

        [Header("Semantics")] public GridFieldCategory Category = GridFieldCategory.Generic;

        public Polarity Polarity = Polarity.Additive;

        [Header("Transform")] public float WeightMultiplier = 1.0f;

        public Vector3 LocalOffset;

        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (!HasSchemas())
                return;

            context.Baker.DependsOn(Field);
            context.Baker.DependsOn(Stamp);
            BindOriginTransform(context);

            context.Baker.AddComponent(clipEntity, new InfluenceClipData
            {
                FieldKey = Field.Id,
                Shape = Stamp.BuildShape(WeightMultiplier * Polarity.Sign()),
                LocalOffset = LocalOffset,
                OriginTarget = originTarget,
                OriginLinkKey = ResolveLinkKey()
            });

            base.Bake(clipEntity, context);
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

        private ushort ResolveLinkKey()
        {
            return originLink != null && EntityLinkAuthoringUtility.TryGetKey(originLink, out var key) ? key : (ushort)0;
        }

        private void BindOriginTransform(BakingContext context)
        {
            if (context.Binding != null && context.Binding.Target != Entity.Null)
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);
        }
    }
}