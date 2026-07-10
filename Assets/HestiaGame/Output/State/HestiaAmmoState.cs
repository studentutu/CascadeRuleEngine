#nullable enable

using CascadeEngineApi;
using UnityEngine;

namespace Hestia
{
    /// <summary>
    /// Durable ammo output state consumed by gameplay/UI after commit.
    /// </summary>
    public readonly struct HestiaAmmoState : IOutputState<HestiaAmmoState>
    {
        public HestiaAmmoState(int current)
        {
            Current = Mathf.Max(0, current);
            IsEmpty = Current <= 0;
        }

        public int Current { get; }
        public bool IsEmpty { get; }

        public bool Equals(HestiaAmmoState other)
            => Current == other.Current && IsEmpty == other.IsEmpty;
    }
}
