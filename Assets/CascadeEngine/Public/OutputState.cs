#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed output descriptor used to route mutation streams without scanning unrelated state.
    /// </summary>
    public sealed class OutputState<TState>
        where TState : struct, IOutputState
    {
        internal OutputState(int index, string name, CommitConflictPolicy conflictPolicy)
        {
            Index = index;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ConflictPolicy = conflictPolicy;
        }

        internal int Index { get; set; }
        public string Name { get; }
        public CommitConflictPolicy ConflictPolicy { get; }

        public override string ToString()
            => Name;
    }
}
