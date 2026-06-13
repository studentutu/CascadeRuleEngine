#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Untyped handle for one schema-declared fact kind. Typed payload queue and reducer live in <see cref="CascadeFact{TPayload}"/>.
    /// </summary>
    public abstract class CascadeFactKey
    {
        private protected CascadeFactKey(CascadeSchema owner, int index, string name)
        {
            Owner = owner;
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }

        internal CascadeSchema Owner { get; }

        public override string ToString()
            => Name;

        /// <summary>
        /// Range: queued slot. Condition: engine fact loop, no side effects. Output: entity targeted by the queued fact.
        /// </summary>
        internal abstract CascadeEntityId GetEntity(int slot);

        /// <summary>
        /// Range: queued slot. Condition: context is bound to the slot entity. Output: registered reducer runs with the typed payload.
        /// </summary>
        internal abstract void Dispatch(CascadeReducerContext context, int slot);

        /// <summary>
        /// Range: all queued slots. Condition: tick finished or failed. Output: queue emptied and payload slots reset.
        /// </summary>
        internal abstract void ClearQueue();
    }
}