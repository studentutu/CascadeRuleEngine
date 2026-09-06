#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Derived fact: movement request resolved to a candidate durable position.
    /// </summary>
    public readonly struct MoveResolvedFact : IFact<MoveResolvedFact>
    {
        public MoveResolvedFact(float position)
        {
            Position = position;
        }

        public float Position { get; }

        public bool Equals(MoveResolvedFact other)
            => Position.Equals(other.Position);
        public void Dispose() { }
    }
}
