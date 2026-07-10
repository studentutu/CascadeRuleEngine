#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Durable sample position output state.
    /// </summary>
    public readonly struct HestiaPositionState : IOutputState<HestiaPositionState>
    {
        public HestiaPositionState(float position)
        {
            Position = position;
        }

        public float Position { get; }

        public bool Equals(HestiaPositionState other)
            => Position.Equals(other.Position);
    }
}
