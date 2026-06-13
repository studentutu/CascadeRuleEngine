#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Converts movement input into a resolved movement fact. The position committer owns conflict policy.
    /// </summary>
    public sealed class HestiaMoveRequestReducer : IFactReducer<MoveRequestedFact>
    {
        public void Reduce(IReduceContext ctx, EntityRef entity, in MoveRequestedFact fact)
        {
            ctx.Emit(entity, new MoveResolvedFact(fact.Position, fact.Priority));
        }
    }
}
