using Unity.Collections;
using Unity.Entities;

namespace BovineLabs.Timeline.Grid.Influence.Data
{
    /// <summary>
    /// The single access point for the whole influence-field feature. It holds nested native containers
    /// (the per-field <see cref="InfluenceField"/> buffers and the <see cref="PendingStamps"/> multimap)
    /// that the job-safety system CANNOT see through, so job ordering across systems is carried entirely by
    /// ECS chaining on this component's read/write access.
    /// <para>
    /// CONTRACT: every system that reads or writes field data must acquire <see cref="FieldRegistrySingleton"/>
    /// via <c>GetSingletonRW</c> (even pure readers) so its job is ordered against the tick pump, and must
    /// publish its job handle back through <c>state.Dependency</c>. Reading a field's data on the MAIN thread
    /// (e.g. tooling, <c>StatelessFeatures</c>) is only safe AFTER completing that field's dependency.
    /// Violations produce silent data races, not exceptions.
    /// </para>
    /// </summary>
    public struct FieldRegistrySingleton : IComponentData
    {
        public FieldRegistry Registry;
        public NativeParallelMultiHashMap<int, Stamp> PendingStamps;
    }
}