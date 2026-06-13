#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Thrown when a committer detects contradictory facts that cannot be resolved deterministically.
    /// </summary>
    public sealed class CommitConflictException : InvalidOperationException
    {
        public CommitConflictException(EntityRef entity, Type outputStateType, string message)
            : base($"Commit conflict for entity '{entity}', output '{outputStateType.Name}': {message}")
        {
            Entity = entity;
            OutputStateType = outputStateType;
        }

        public EntityRef Entity { get; }
        public Type OutputStateType { get; }
    }
}
