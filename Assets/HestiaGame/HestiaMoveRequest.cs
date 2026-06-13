#nullable enable

namespace Hestia
{
    /// <summary>
    /// Payload for DesiredPosition facts: target position plus conflict priority (higher wins, equal overwrites).
    /// </summary>
    public readonly struct HestiaMoveRequest
    {
        public HestiaMoveRequest(float position, int priority)
        {
            Position = position;
            Priority = priority;
        }

        public float Position { get; }
        public int Priority { get; }
    }
}