#nullable enable

namespace CascadeEngineApi
{
    /// <summary>Single continuation phase for the open tick.</summary>
    internal enum SimulationPhase
    {
        Immediate,
        Transactional,
        Batch,
        State,
        Negative,
        Reconciliation
    }
}
