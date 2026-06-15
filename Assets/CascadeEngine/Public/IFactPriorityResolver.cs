#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed fact priority resolver used by the work queue without boxing struct facts.
    /// </summary>
    public interface IFactPriorityResolver<TFact>
        where TFact : struct, IFact
    {
        FactPriority Resolve(in TFact fact);
    }
}
