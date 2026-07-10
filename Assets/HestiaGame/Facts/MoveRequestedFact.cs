#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: gameplay requested a position change.
    /// </summary>
    public readonly struct MoveRequestedFact : IFact<MoveRequestedFact>
    {
        public MoveRequestedFact(float position)
        {
            Position = position;
        }

        public float Position { get; }

        public bool Equals(MoveRequestedFact other)
            => Position.Equals(other.Position);
    }
}
