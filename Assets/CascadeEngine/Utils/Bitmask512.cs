#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace CascadeEngineApi
{
    /// <summary>
    /// Bitmask with capacity for keys 0-511.
    /// </summary>
    public struct Bitmask512
    {
        public const int BitCount = 512;

        private uint _w0;
        private uint _w1;
        private uint _w2;
        private uint _w3;
        private uint _w4;
        private uint _w5;
        private uint _w6;
        private uint _w7;
        private uint _w8;
        private uint _w9;
        private uint _w10;
        private uint _w11;
        private uint _w12;
        private uint _w13;
        private uint _w14;
        private uint _w15;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Set(int index)
        {
            if (IsSet(index))
            {
                return false;
            }

            SetDirty(index);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetDirty(int index)
        {
            ValidateIndex(index);

            var wordIndex = index >> 5;
            var bitIndex = index & 31;

            SetWordValue(wordIndex, GetWordValue(wordIndex) | (1u << bitIndex));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsSet(int index)
        {
            ValidateIndex(index);

            var wordIndex = index >> 5;
            var bitIndex = index & 31;

            return (GetWordValue(wordIndex) & (1u << bitIndex)) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsDirty(int index)
            => IsSet(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear(int index)
        {
            ValidateIndex(index);

            var wordIndex = index >> 5;
            var bitIndex = index & 31;

            SetWordValue(wordIndex, GetWordValue(wordIndex) & ~(1u << bitIndex));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearBit(int index)
            => Clear(index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll()
        {
            _w0 = _w1 = _w2 = _w3 = 0;
            _w4 = _w5 = _w6 = _w7 = 0;
            _w8 = _w9 = _w10 = _w11 = 0;
            _w12 = _w13 = _w14 = _w15 = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool AnySet()
            => (_w0 | _w1 | _w2 | _w3 |
                _w4 | _w5 | _w6 | _w7 |
                _w8 | _w9 | _w10 | _w11 |
                _w12 | _w13 | _w14 | _w15) != 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool AnyDirty()
            => AnySet();

        public readonly int CountSetBits()
        {
            return CountBits(_w0) + CountBits(_w1) + CountBits(_w2) + CountBits(_w3) +
                   CountBits(_w4) + CountBits(_w5) + CountBits(_w6) + CountBits(_w7) +
                   CountBits(_w8) + CountBits(_w9) + CountBits(_w10) + CountBits(_w11) +
                   CountBits(_w12) + CountBits(_w13) + CountBits(_w14) + CountBits(_w15);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountBits(uint value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateIndex(int index)
        {
            if ((uint)index >= BitCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 0 and 511.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetWordValue(int wordIndex, uint value)
        {
            switch (wordIndex)
            {
                case 0:
                    _w0 = value;
                    return;
                case 1:
                    _w1 = value;
                    return;
                case 2:
                    _w2 = value;
                    return;
                case 3:
                    _w3 = value;
                    return;
                case 4:
                    _w4 = value;
                    return;
                case 5:
                    _w5 = value;
                    return;
                case 6:
                    _w6 = value;
                    return;
                case 7:
                    _w7 = value;
                    return;
                case 8:
                    _w8 = value;
                    return;
                case 9:
                    _w9 = value;
                    return;
                case 10:
                    _w10 = value;
                    return;
                case 11:
                    _w11 = value;
                    return;
                case 12:
                    _w12 = value;
                    return;
                case 13:
                    _w13 = value;
                    return;
                case 14:
                    _w14 = value;
                    return;
                case 15:
                    _w15 = value;
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(wordIndex));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private readonly uint GetWordValue(int wordIndex)
        {
            switch (wordIndex)
            {
                case 0: return _w0;
                case 1: return _w1;
                case 2: return _w2;
                case 3: return _w3;
                case 4: return _w4;
                case 5: return _w5;
                case 6: return _w6;
                case 7: return _w7;
                case 8: return _w8;
                case 9: return _w9;
                case 10: return _w10;
                case 11: return _w11;
                case 12: return _w12;
                case 13: return _w13;
                case 14: return _w14;
                case 15: return _w15;
                default:
                    throw new ArgumentOutOfRangeException(nameof(wordIndex));
            }
        }
    }
}
