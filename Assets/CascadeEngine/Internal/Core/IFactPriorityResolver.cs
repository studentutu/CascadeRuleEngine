#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal typed fact priority resolver used by the work queue without boxing struct facts.
    /// </summary>
    internal interface IFactPriorityResolver<TFact>
        where TFact : struct, IFact
    {
        FactPriority Resolve(in TFact fact);
    }
}
