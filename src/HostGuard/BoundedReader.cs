using System;
using System.Buffers.Binary;
using System.Text;

namespace HostGuard
{
    /// <summary>
    /// Reads one untrusted message from a byte span and checks every rule as it goes: no read past the end, sizes and
    /// counts within <see cref="MessageLimits"/>, finite floats, values within their ranges, strict UTF-8 without control
    /// or direction-override characters, bounded nesting, and no trailing bytes (<see cref="EnsureEnd"/>). The first
    /// broken rule throws <see cref="MessageRejectedException"/>. Numbers are little-endian; counts and string lengths
    /// are unsigned LEB128 (at most 5 bytes). Nothing is allocated except the strings returned.
    /// </summary>
    public ref struct BoundedReader
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        readonly ReadOnlySpan<byte> _data;
        readonly MessageLimits _limits;
        int _position;
        int _depth;

        /// <summary>Starts reading <paramref name="data"/> under <paramref name="limits"/> (the defaults when null).</summary>
        public BoundedReader(ReadOnlySpan<byte> data, MessageLimits limits = null)
        {
            _limits = limits ?? MessageLimits.Default;
            if (data.Length > _limits.MaxMessageBytes)
                throw new MessageRejectedException(RejectReason.TooLarge, $"{data.Length} bytes, limit {_limits.MaxMessageBytes}");
            _data = data;
            _position = 0;
            _depth = 0;
        }

        /// <summary>Bytes not read yet.</summary>
        public int Remaining => _data.Length - _position;

        /// <summary>Bytes read so far.</summary>
        public int Position => _position;

        /// <summary>The limits in force.</summary>
        public MessageLimits Limits => _limits;

        ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining) throw new MessageRejectedException(RejectReason.Truncated);
            var span = _data.Slice(_position, count);
            _position += count;
            return span;
        }

        /// <summary>One byte.</summary>
        public byte ReadByte() => Take(1)[0];

        /// <summary>One byte, which must lie in <paramref name="min"/>..<paramref name="max"/>.</summary>
        public byte ReadByte(byte min, byte max)
        {
            byte b = ReadByte();
            if (b < min || b > max) throw new MessageRejectedException(RejectReason.OutOfRange, $"{b} not in {min}..{max}");
            return b;
        }

        /// <summary>A boolean: exactly 0 or 1 (any other byte is malformed, so a value has one encoding).</summary>
        public bool ReadBool()
        {
            byte b = ReadByte();
            if (b > 1) throw new MessageRejectedException(RejectReason.Malformed, "boolean must be 0 or 1");
            return b == 1;
        }

        /// <summary>A 16-bit unsigned number.</summary>
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

        /// <summary>A 32-bit signed number.</summary>
        public int ReadInt32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

        /// <summary>A 32-bit signed number that must lie in <paramref name="min"/>..<paramref name="max"/>.</summary>
        public int ReadInt32(int min, int max)
        {
            int v = ReadInt32();
            if (v < min || v > max) throw new MessageRejectedException(RejectReason.OutOfRange, $"{v} not in {min}..{max}");
            return v;
        }

        /// <summary>A 64-bit signed number.</summary>
        public long ReadInt64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

        /// <summary>A 64-bit unsigned number.</summary>
        public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

        /// <summary>A 32-bit float; NaN and infinities are refused.</summary>
        public float ReadSingle()
        {
            float f = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(Take(4)));
            if (float.IsNaN(f) || float.IsInfinity(f)) throw new MessageRejectedException(RejectReason.NotFinite);
            return f;
        }

        /// <summary>A 64-bit float; NaN and infinities are refused.</summary>
        public double ReadDouble()
        {
            double d = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(Take(8)));
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new MessageRejectedException(RejectReason.NotFinite);
            return d;
        }

        /// <summary>
        /// An unsigned LEB128 number of at most 5 bytes that fits in 32 bits. Overlong encodings (a final zero byte after the
        /// first) are malformed, so every value has exactly one encoding.
        /// </summary>
        public uint ReadVarUInt32()
        {
            uint result = 0;
            for (int i = 0; i < 5; i++)
            {
                byte b = ReadByte();
                if (i == 4 && b > 0x0F) throw new MessageRejectedException(RejectReason.Malformed, "variable-length number too large");
                result |= (uint)(b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0)
                {
                    if (i > 0 && b == 0) throw new MessageRejectedException(RejectReason.Malformed, "overlong variable-length number");
                    return result;
                }
            }
            throw new MessageRejectedException(RejectReason.Malformed, "variable-length number too long");
        }

        /// <summary>
        /// A count of elements to read next. It must be at most <paramref name="max"/> (and the limits' MaxCount), and the
        /// data left must be able to hold that many elements of at least <paramref name="minElementBytes"/> each, so a
        /// hostile count can never make you allocate more than the message could fill.
        /// </summary>
        public int ReadCount(int max = int.MaxValue, int minElementBytes = 1)
        {
            if (minElementBytes < 1) throw new ArgumentOutOfRangeException(nameof(minElementBytes));
            uint count = ReadVarUInt32();
            long cap = Math.Min(max, _limits.MaxCount);
            if (count > cap) throw new MessageRejectedException(RejectReason.CountTooLarge, $"{count} > {cap}");
            if ((long)count * minElementBytes > Remaining)
                throw new MessageRejectedException(RejectReason.CountTooLarge, $"{count} elements cannot fit in {Remaining} bytes");
            return (int)count;
        }

        /// <summary>
        /// A UTF-8 string with a length prefix, at most <paramref name="maxBytes"/> bytes (and the limits' MaxStringBytes).
        /// Invalid UTF-8, control characters (newlines only when <paramref name="allowNewlines"/>) and Unicode direction
        /// overrides are refused.
        /// </summary>
        public string ReadString(int maxBytes = int.MaxValue, bool allowNewlines = false)
        {
            int cap = Math.Min(maxBytes, _limits.MaxStringBytes);
            uint length = ReadVarUInt32();
            if (length > cap) throw new MessageRejectedException(RejectReason.StringTooLong, $"{length} > {cap} bytes");
            var bytes = Take((int)length);
            string s;
            try { s = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException) { throw new MessageRejectedException(RejectReason.InvalidUtf8); }
            foreach (char c in s)
                if (TextRules.IsForbidden(c, allowNewlines)) throw new MessageRejectedException(RejectReason.ForbiddenCharacter, $"U+{(int)c:X4}");
            return s;
        }

        /// <summary>A block of bytes with a length prefix, at most <paramref name="maxBytes"/>. The span points into the message.</summary>
        public ReadOnlySpan<byte> ReadBlock(int maxBytes)
        {
            uint length = ReadVarUInt32();
            if (length > maxBytes) throw new MessageRejectedException(RejectReason.TooLarge, $"block of {length} > {maxBytes} bytes");
            return Take((int)length);
        }

        /// <summary>Exactly <paramref name="destination"/>.Length bytes, copied.</summary>
        public void ReadBytes(Span<byte> destination) => Take(destination.Length).CopyTo(destination);

        /// <summary>Call before reading a nested value; refuses nesting deeper than the limits' MaxDepth. Pair with <see cref="ExitNested"/>.</summary>
        public void EnterNested()
        {
            if (++_depth > _limits.MaxDepth) throw new MessageRejectedException(RejectReason.TooDeep, $"deeper than {_limits.MaxDepth}");
        }

        /// <summary>Call after reading a nested value.</summary>
        public void ExitNested()
        {
            if (_depth == 0) throw new InvalidOperationException("ExitNested without EnterNested.");
            _depth--;
        }

        /// <summary>Refuses the message if any bytes are left: a well-formed message is read exactly to its end.</summary>
        public void EnsureEnd()
        {
            if (Remaining != 0) throw new MessageRejectedException(RejectReason.TrailingBytes, $"{Remaining} bytes left");
        }
    }
}
