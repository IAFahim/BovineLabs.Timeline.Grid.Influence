using System;
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
using Hash128 = UnityEngine.Hash128;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public sealed class GridCompositeClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Schemas")] public GridFieldSchemaObject Field;

        public GridCompositeSchemaObject Composite;

        [Header("Semantics")] public Polarity Polarity = Polarity.Additive;

        [Header("Transform")] public Vector3 LocalOffset;

        [Header("Routing")] public Target originTarget = Target.Owner;

        public EntityLinkSchema originLink;

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

            if (!TryBakeBlob(context, out var blob))
                return;

            var builder = new GridCompositeBuilder
            {
                FieldKey = Field.Id,
                Composite = blob,
                LocalOffset = LocalOffset,
                OriginTarget = originTarget,
                OriginLinkKey = ResolveLinkKey()
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);
            commands.AddBuffer<InfluenceStampElement>();

            base.Bake(clipEntity, context);
        }

        private bool TryBakeBlob(BakingContext context, out BlobAssetReference<CompositeShapeBlob> blob)
        {
            var baseShape = Composite.Base.BuildShape(1f).WithWeight(1);
            var weights = Composite.Profile.SampleDepthWeights(baseShape, Allocator.Temp);

            var sign = Polarity.Sign();
            if (sign != 1)
                for (var i = 0; i < weights.Length; i++)
                    weights[i] *= sign;

            var hash = ContentHash(baseShape, weights);

            if (!context.Baker.TryGetBlobAssetReference(hash, out blob))
            {
                blob = CompositeBaker.Build(baseShape, weights, Allocator.Persistent);
                context.Baker.AddBlobAssetWithCustomHash(ref blob, hash);
            }

            weights.Dispose();
            return blob.IsCreated && blob.Value.Layers.Length > 0;
        }

        private static Hash128 ContentHash(InfluenceShape baseShape, NativeArray<int> weights)
        {
            uint lo = 0x811c9dc5u;
            uint hi = 0x6a09e667u;
            for (var i = 0; i < weights.Length; i++)
            {
                var w = (uint)weights[i];
                lo ^= w;
                lo *= 0x01000193u;
                hi ^= w;
                hi *= 0x9E3779B9u;
            }

            Fold(ref lo, ref hi, (uint)baseShape.RectMin.x);
            Fold(ref lo, ref hi, (uint)baseShape.RectMin.y);
            Fold(ref lo, ref hi, (uint)baseShape.RectSize.x);
            Fold(ref lo, ref hi, (uint)baseShape.RectSize.y);
            Fold(ref lo, ref hi, (uint)baseShape.ShellThickness);
            Fold(ref lo, ref hi, (uint)baseShape.AnnulusInnerRadius);
            Fold(ref lo, ref hi, (uint)baseShape.SectorDir1.x);
            Fold(ref lo, ref hi, (uint)baseShape.SectorDir1.y);

            return new Hash128(
                (uint)baseShape.Kind ^ lo,
                (uint)baseShape.Weight ^ hi,
                lo,
                hi);
        }

        private static void Fold(ref uint lo, ref uint hi, uint value)
        {
            lo ^= value;
            lo *= 0x01000193u;
            hi ^= value;
            hi *= 0x9E3779B9u;
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
