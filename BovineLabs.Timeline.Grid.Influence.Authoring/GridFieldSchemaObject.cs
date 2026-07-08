using BovineLabs.Core.Asset;
using BovineLabs.Nerve.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [AutoRef(nameof(InfluenceGridSettingsAuthoring), nameof(InfluenceGridSettingsAuthoring.Fields), "GridField",
        "Schemas/GridFields")]
    [CreateAssetMenu(menuName = "BovineLabs/Grid/Field Schema")]
    public class GridFieldSchemaObject : ScriptableObject, IUID
    {
        [SerializeField] [InspectorReadOnly] private int id;

        public string FieldName = "New Field";
        [Range(1, 8)] public int ChunkPower = 4;
        public uint RetentionFrames = 300;
        public bool DoubleBuffered;

        [Header("Diffusion")] [Range(0, 1000)] public int DecayPerMille;

        [Min(1)] public int SpreadDenominator = 5;

        public ushort Id => (ushort)id;

        int IUID.ID
        {
            get => id;
            set => id = value;
        }
    }
}