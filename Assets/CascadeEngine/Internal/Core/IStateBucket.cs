#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal untyped contract for committed state storage.
    /// </summary>
    internal interface IStateBucket
    {
        Type StateType { get; }
        int StateCapacityHint { get; }
        int MutationCapacity { get; }

        bool Has(EntityRef entity);

        void Delete(EntityRef entity);

        void EnsureCapacity(int stateCapacity, int mutationCapacity);

        void ClearMutations();

        void DisposeBucket();

        int MutationCount { get; }
    }
}
