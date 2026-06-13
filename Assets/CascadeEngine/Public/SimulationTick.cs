#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Monotonic simulation tick id assigned by FactSimulation.
    /// </summary>
    public readonly struct SimulationTick : IEquatable<SimulationTick>
    {
        public SimulationTick(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public bool Equals(SimulationTick other)
            => Value == other.Value;

        public override bool Equals(object? obj)
            => obj is SimulationTick other && Equals(other);

        public override int GetHashCode()
            => Value;

        public override string ToString()
            => Value.ToString();
    }
}
