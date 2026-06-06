using System;
using BovineLabs.Core.Authoring.EntityCommands;
using BovineLabs.Timeline.Authoring;
using BovineLabs.Timeline.Grid.Influence.Data;
using BovineLabs.Timeline.Grid.Influence.Data.Builders;
using BovineLabs.Timeline.Grid.Influence.Data.Flows;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Timeline;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [Serializable]
    public sealed class GridFlowSteeringClip : DOTSClip, ITimelineClipAsset
    {
        [Header("Schemas")] public GridFieldSchemaObject Field;

        [Header("Steering")] public FlowBias Bias = FlowBias.Descend;

        public float MaxSpeed = 1.0f;

        [Header("Transform")] public Vector3 LocalOffset;

        public override double duration => 1.0;
        public ClipCaps clipCaps => ClipCaps.Blending | ClipCaps.Looping;

        public override void Bake(Entity clipEntity, BakingContext context)
        {
            if (Field == null)
            {
                Debug.LogError($"GridFlowSteeringClip '{name}' has no Field schema assigned. Clip will be skipped.",
                    this);
                return;
            }

            context.Baker.DependsOn(Field);

            if (context.Binding != null && context.Binding.Target != Entity.Null)
                context.Baker.AddTransformUsageFlags(context.Binding.Target, TransformUsageFlags.Dynamic);

            var builder = new GridFlowSteeringBuilder
            {
                FieldKey = Field.Id,
                LocalOffset = LocalOffset,
                Bias = Bias,
                MaxSpeed = MaxSpeed
            };
            var commands = new BakerCommands(context.Baker, clipEntity);
            builder.ApplyTo(ref commands);

            base.Bake(clipEntity, context);
        }
    }
}