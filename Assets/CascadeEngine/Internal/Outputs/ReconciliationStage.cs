#nullable enable

namespace CascadeEngineApi
{
    /// <summary>Cursor owner within unpublished output preparation.</summary>
    internal enum ReconciliationStage
    {
        Affected,
        Absent,
        Lifecycle,
        Validate,
        Ready
    }
}
