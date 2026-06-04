using BovineLabs.Core.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [AutoRef(nameof(InfluenceGridSettingsAuthoring), nameof(InfluenceGridSettingsAuthoring.Fields), "GridField", "Schemas/GridFields")]
    [CreateAssetMenu(menuName = "BovineLabs/Grid/Field Schema")]
    public class GridFieldSchemaObject : ScriptableObject, IUID
    {
        [SerializeField]
        [InspectorReadOnly]
        private int id;

        public ushort Id => (ushort)this.id;

        int IUID.ID
        {
            get => this.id;
            set => this.id = value;
        }

        public string FieldName = "New Field";
        [Range(1, 8)] public int ChunkPower = 4;
        public uint RetentionFrames = 300;
        public bool DoubleBuffered = false;

        [Header("Diffusion")]
        [Range(0, 1000)] public int DecayPerMille = 0;
        [Min(1)] public int SpreadDenominator = 5;
    }
}
