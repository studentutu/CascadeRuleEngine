#nullable enable

using System;
using CascadeEngineApi;
using UnityEngine;

namespace Hestia
{
    /// <summary>
    /// Durable ammo output state consumed by gameplay/UI after commit.
    /// </summary>
    public readonly struct HestiaAmmoState : IOutputState, IEquatable<HestiaAmmoState>
    {
        public static readonly CascadeTypeId CascadeId = CascadeTypeId.FromName(nameof(HestiaAmmoState));

        public HestiaAmmoState(int current)
        {
            Current = Mathf.Max(0, current);
            IsEmpty = Current <= 0;
        }

        public int Current { get; }
        public bool IsEmpty { get; }

        public bool Equals(HestiaAmmoState other)
            => Current == other.Current && IsEmpty == other.IsEmpty;

        public override bool Equals(object? obj)
            => obj is HestiaAmmoState other && Equals(other);

        public override int GetHashCode()
            => Current.CombineHestiaHash(IsEmpty.GetHashCode());
    }
}
