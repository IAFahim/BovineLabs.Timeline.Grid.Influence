using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    public static class CompositeBaking
    {
        public static bool TryBuild(GridCompositeSchemaObject composite, int sign, float weightMultiplier,
            Quarter rotation, Object warnContext, bool warnAllZero,
            out BlobAssetReference<CompositeShapeBlob> blob)
        {
            blob = default;

            if (composite == null || composite.Base == null)
                return false;

            if (composite.Base.Kind == ShapeKind.Painted)
            {
                Debug.LogWarning(
                    $"Grid composite '{composite.name}' uses a Painted base stamp; Painted stamps have no composite form, so the composite is skipped. Use a parametric base shape.",
                    warnContext);
                return false;
            }

            var baseShape = composite.Base.BuildShape(1f).WithWeight(1).Rotated(rotation);
            var weights = composite.Profile.SampleDepthWeights(baseShape, Allocator.Temp);

            var anyNonZero = false;
            for (var i = 0; i < weights.Length; i++)
            {
                weights[i] = Mathf.RoundToInt(weights[i] * weightMultiplier) * sign;
                anyNonZero |= weights[i] != 0;
            }

            if (warnAllZero && !anyNonZero && weights.Length > 0)
                Debug.LogWarning(
                    $"Grid composite on '{(warnContext != null ? warnContext.name : composite.name)}' rounded every layer weight to 0 (WeightMultiplier {weightMultiplier}); the composite contributes nothing. Raise WeightMultiplier or the base weights.",
                    warnContext);

            blob = CompositeBaker.Build(baseShape, weights, Allocator.Persistent);
            weights.Dispose();

            if (blob.IsCreated && blob.Value.Layers.Length > 0)
                return true;

            if (blob.IsCreated)
                blob.Dispose();

            blob = default;
            return false;
        }
    }
}
