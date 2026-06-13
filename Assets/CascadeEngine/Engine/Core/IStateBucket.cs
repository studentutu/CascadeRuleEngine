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

        bool Has(EntityRef entity);

        void Delete(EntityRef entity);

        void ClearMutations();

        int MutationCount { get; }
    }
}
