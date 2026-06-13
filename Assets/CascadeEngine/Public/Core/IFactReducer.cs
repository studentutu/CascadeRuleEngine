#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Reducer for one triggering fact type. Reducers derive facts and never write output state.
    /// </summary>
    public interface IFactReducer<TFact>
        where TFact : struct, IFact
    {
        void Reduce(IReduceContext ctx, EntityRef entity, in TFact fact);
    }
}
