using System;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Builders;
using BovineLabs.Timeline.Physics;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public sealed class GridFlowSteeringClip : DOTSClip, ITimelineClipAsset
    {
        public GridFieldSchemaObject Field;
        public GridStampSchemaObject Sampler;
        
        public Polarity Polarity = Polarity.Additive;
        public PhysicsForceMode Mode = PhysicsForceMode.Continuous;
        
        [Min(0f)]
        public float Strength = 10f;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (Field == null)
            {
                Debug.LogError($"GridFlowSteeringClip '{name}' has no Field schema assigned. Clip will be skipped.", this);
                return;
            }

            if (Sampler == null)
            {
                Debug.LogError($"GridFlowSteeringClip '{name}' has no Sampler schema assigned. Clip will be skipped.", this);
                return;
            }

            context.Baker.DependsOn(Field);
            context.Baker.DependsOn(Sampler);

            if (context.Binding != null && context.Binding.Target != Entity.Null)
            {
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);
                context.Baker.AddBuffer<PendingForce>(context.Binding.Target);
            }

            var shape = Sampler.BuildShape(1f);

            var builder = new GridFlowSteeringBuilder
            {
                FieldKey = Field.Id,
                SamplerShape = shape,
                Polarity = Polarity.Sign(),
                Mode = Mode,
                Strength = Strength
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}
