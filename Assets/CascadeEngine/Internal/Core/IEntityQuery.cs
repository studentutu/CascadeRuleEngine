#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Reducer query API over committed state and current tick facts.
    /// </summary>
    public interface IEntityQuery
    {
        EntityQueryResult With<TState>()
            where TState : struct, IOutputState;

        EntityQueryResult With<TStateA, TStateB>()
            where TStateA : struct, IOutputState
            where TStateB : struct, IOutputState;

        EntityQueryResult WithFact<TFact>()
            where TFact : struct, IFact;
    }
}
