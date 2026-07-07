using BovineLabs.Core.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using BovineLabs.Timeline.Grid.Influence.Data;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    // Implements IUID for parity with the other Grid schema assets, but its Id is currently NOT consumed at
    // runtime (each clip bakes the composite blob inline via CompositeBaking) and the asset carries no [AutoRef],
    // so it is never auto-registered into InfluenceGridSettingsAuthoring. The Id is reserved for a future
    // registry/dedup path; do not remove IUID.
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
            return CompositeBaking.TryBuild(this, 1, 1f, Quarter.R0, this, false, out blob);
        }
    }
}