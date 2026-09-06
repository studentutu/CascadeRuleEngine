#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Routed pending pairs stored as bits, with exact removal and no per-candidate objects. Keys are stage-ordered row/column pairs.
    /// </summary>
    internal sealed class ReducerCandidates
    {
        private ulong[] _bits = Array.Empty<ulong>();
        private int _firstWord;
        private bool _fixedCapacity;
        internal int Count { get; private set; }
        internal long ReservedBytes => (long)_bits.Length * sizeof(ulong);

        internal void EnsureCapacity(int rows, int columns)
        {
            var pairs = (long)rows * columns;
            var words = (pairs + 63) / 64;
            if (pairs > int.MaxValue) throw new InvalidOperationException("Candidate capacity exceeds supported storage.");
            if (words <= _bits.Length) return;
            if (_fixedCapacity) throw new InvalidOperationException("Fixed reducer candidate capacity exceeded.");
            Array.Resize(ref _bits, (int)words);
        }

        internal void FreezeCapacity() => _fixedCapacity = true;

        internal void RepackRows(int oldStride, int newStride, int rows)
        {
            if (_fixedCapacity) throw new InvalidOperationException("Fixed reducer candidate capacity exceeded.");
            var next = new ReducerCandidates();
            next.EnsureCapacity(rows, newStride);
            while (TryPeek(out var key))
            {
                next.Add(checked(key / oldStride * newStride + key % oldStride));
                Remove(key);
            }
            _bits = next._bits;
            _firstWord = next._firstWord;
            Count = next.Count;
        }

        internal void Add(int key)
        {
            var word = key >> 6;
            var bit = 1UL << (key & 63);
            if ((_bits[word] & bit) != 0) return;
            _bits[word] |= bit;
            _firstWord = Count == 0 ? word : Math.Min(_firstWord, word);
            Count++;
        }

        internal bool TryPeek(out int key)
        {
            if (Count == 0) { key = -1; return false; }
            while (_bits[_firstWord] == 0) _firstWord++;
            var bits = _bits[_firstWord];
            key = (_firstWord << 6) + FirstBit(bits);
            return true;
        }

        internal bool TryPeekRange(int minimum, int maximum, out int key)
        {
            if (Count == 0 || minimum >= maximum) { key = -1; return false; }
            var word = Math.Max(_firstWord, minimum >> 6);
            var end = Math.Min(_bits.Length, (int)(((long)maximum + 63) >> 6));
            for (; word < end; word++)
            {
                var bits = _bits[word];
                if (word == minimum >> 6) bits &= ulong.MaxValue << (minimum & 63);
                if (bits == 0) continue;
                key = (word << 6) + FirstBit(bits);
                return key < maximum;
            }
            key = -1;
            return false;
        }

        private static int FirstBit(ulong bits)
        {
            // Portable trailing-zero lookup for Unity's C# 8 runtime.
            var offset = 0;
            if ((bits & 0xffffffffUL) == 0) { bits >>= 32; offset += 32; }
            if ((bits & 0xffffUL) == 0) { bits >>= 16; offset += 16; }
            if ((bits & 0xffUL) == 0) { bits >>= 8; offset += 8; }
            if ((bits & 0xfUL) == 0) { bits >>= 4; offset += 4; }
            if ((bits & 3UL) == 0) { bits >>= 2; offset += 2; }
            if ((bits & 1UL) == 0) offset++;
            return offset;
        }

        internal void Remove(int key)
        {
            _bits[key >> 6] &= ~(1UL << (key & 63));
            Count--;
            if (Count == 0) _firstWord = 0;
            else while (_bits[_firstWord] == 0) _firstWord++;
        }

        internal void Clear()
        {
            while (TryPeek(out var key)) Remove(key);
            _firstWord = 0;
        }

        internal void DisposeStorage()
        {
            _bits = Array.Empty<ulong>();
            Count = 0;
            _firstWord = 0;
        }
    }
}
