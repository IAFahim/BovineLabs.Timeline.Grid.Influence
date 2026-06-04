using BovineLabs.Core.ObjectManagement;
using BovineLabs.Core.PropertyDrawers;
using UnityEngine;

namespace BovineLabs.Timeline.Grid.Influence.Authoring
{
    [AutoRef("InfluenceGridSettings", "Fields", "GridFieldSchemaObject", "Schemas/GridFields/")]
    [CreateAssetMenu(menuName = "BovineLabs/Grid/Field Schema")]
    public class GridFieldSchemaObject : ScriptableObject, IUID
    {
        [SerializeField] [InspectorReadOnly] private ushort id;
        public ushort Id => id;
        int IUID.ID { get => id; set => id = (ushort)value; }

        public string FieldName = "New Field";
        [Range(1, 8)] public int ChunkPower = 4;
        public uint RetentionFrames = 300;
        public bool DoubleBuffered = false;

        [Header("Diffusion")]
        [Range(0, 1000)] public int DecayPerMille = 0;
        [Min(1)] public int SpreadDenominator = 5;
    }
}
