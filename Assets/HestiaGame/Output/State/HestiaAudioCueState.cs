#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Durable marker-like audio output. Version increments so repeated same cues still publish mutations.
    /// </summary>
    public readonly struct HestiaAudioCueState : IOutputState, IEquatable<HestiaAudioCueState>
    {
        public HestiaAudioCueState(HestiaAudioCueKind cue, int version)
        {
            Cue = cue;
            Version = version;
        }

        public HestiaAudioCueKind Cue { get; }
        public int Version { get; }

        public bool Equals(HestiaAudioCueState other)
            => Cue == other.Cue && Version == other.Version;

        public override bool Equals(object? obj)
            => obj is HestiaAudioCueState other && Equals(other);

        public override int GetHashCode()
            => ((int)Cue).CombineHestiaHash(Version);
    }
}
