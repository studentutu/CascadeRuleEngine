#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal output registration contract used by the commit loop.
    /// </summary>
    internal interface IOutputRegistration
    {
        Type StateType { get; }
        string Name { get; }

        void Reindex(int index);

        bool IsAffectedBy(IEntityFactView facts);

        ICommitAction? CreateCommitAction(FactSimulation simulation, EntityRef entity);

        void DeleteState(FactSimulation simulation, EntityRef entity);

        void ClearMutations(FactSimulation simulation);

        int MutationCount(FactSimulation simulation);
    }
}
