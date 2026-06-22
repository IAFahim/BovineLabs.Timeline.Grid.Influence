using BovineLabs.Core.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [CreateAssetMenu(menuName = "BovineLabs/Grid/Composite Schema")]
    public class GridCompositeSchemaObject : ScriptableObject, IUID
    {
        [SerializeField] [InspectorReadOnly] private int id;

        public GridStampSchemaObject Base;
        public CompositeProfile Profile = CompositeProfile.Default;

        public ushort Id => (ushort)id;

        private void OnValidate()
        {
            Profile.Peak = math.max(0, Profile.Peak);
            Profile.Levels = math.max(1, Profile.Levels);
        }

        int IUID.ID
        {
            get => id;
            set => id = value;
        }

        public bool TryBuild(out BlobAssetReference<CompositeShapeBlob> blob)
        {
            blob = default;
            if (Base == null)
                return false;

            var baseShape = Base.BuildShape(1f).WithWeight(1);
            var weights = Profile.SampleDepthWeights(baseShape, Allocator.Temp);
            blob = CompositeBaker.Build(baseShape, weights, Allocator.Persistent);
            weights.Dispose();

            if (!blob.IsCreated || blob.Value.Layers.Length == 0)
            {
                if (blob.IsCreated)
                    blob.Dispose();
                blob = default;
                return false;
            }

            return true;
        }
    }
}