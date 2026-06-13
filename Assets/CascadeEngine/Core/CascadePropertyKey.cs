#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Untyped handle for one schema-declared property. Typed storage and policy live in <see cref="CascadeProperty{T}"/>.
    /// </summary>
    public abstract class CascadePropertyKey
    {
        private protected CascadePropertyKey(CascadeSchema owner, int index, string name)
        {
            Owner = owner;
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }

        internal CascadeSchema Owner { get; }

        /// <summary>
        /// Range: mutation output from the last tick. Condition: routing checks. Output: number of entities this property mutated.
        /// </summary>
        internal abstract int MutatedCount { get; }

        public override string ToString()
            => Name;

        /// <summary>
        /// Range: staged entities of this property. Condition: engine commit phase. Output: committed values and recorded mutations.
        /// </summary>
        internal abstract void CommitStaged(CascadeEntityStore entities, CascadeMutationLog mutations);

        /// <summary>
        /// Range: staged entities of this property. Condition: tick failed. Output: staged work discarded without committing.
        /// </summary>
        internal abstract void AbortStaged();

        /// <summary>
        /// Range: mutation output of this property. Condition: new tick starts or caller consumed output. Output: mutation output cleared.
        /// </summary>
        internal abstract void ClearMutationOutput();

        /// <summary>
        /// Range: one entity slot. Condition: entity destroyed. Output: committed and staged values reset to default.
        /// </summary>
        internal abstract void ClearEntity(CascadeEntityId entityId);

        /// <summary>
        /// Range: mutation output from the last tick. Condition: exact entity check, no side effects. Output: true if this property mutated for the entity.
        /// </summary>
        internal abstract bool WasMutated(CascadeEntityId entityId);
    }
}