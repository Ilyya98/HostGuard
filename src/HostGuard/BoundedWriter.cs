using System;
using System.Buffers.Binary;
using System.Text;

namespace HostGuard
{
    /// <summary>
    /// Writes a message in the format <see cref="BoundedReader"/> reads, under the same <see cref="MessageLimits"/>, so a
    /// well-behaved sender can never produce a message the receiver refuses. Reuse one writer per connection
    /// (<see cref="Reset"/>): the buffer grows to the largest message written, up to MaxMessageBytes, and is then reused
    /// without allocating.
    /// </summary>
    public sealed class BoundedWriter
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        readonly MessageLimits _limits;
        byte[] _buffer;
        int _length;
        int _depth;

        /// <summary>Creates a writer under <paramref name="limits"/> (the defaults when null).</summary>
        public BoundedWriter(MessageLimits limits = null, int initialCapacity = 256)
        {
            _limits = limits ?? MessageLimits.Default;
            _buffer = new byte[Math.Max(16, Math.Min(initialCapacity, _limits.MaxMessageBytes))];
        }

        /// <summary>The bytes written since the last <see cref="Reset"/>.</summary>
        public ReadOnlySpan<byte> Written => new ReadOnlySpan<byte>(_buffer, 0, _length);

        /// <summary>Number of bytes written.</summary>
        public int Length => _length;

        /// <summary>The limits in force.</summary>
        public MessageLimits Limits => _limits;

        /// <summary>Starts a new message, keeping the buffer.</summary>
        public void Reset()
        {
            _length = 0;
            _depth = 0;
        }

        Span<byte> Grow(int count)
        {
            long needed = (long)_length + count;
            if (needed > _limits.MaxMessageBytes)
                throw new MessageRejectedException(RejectReason.TooLarge, $"message would be {needed} bytes, limit {_limits.MaxMessageBytes}");
            if (needed > _buffer.Length)
            {
                int size = (int)Math.Min(_limits.MaxMessageBytes, Math.Max(needed, (long)_buffer.Length * 2));
                Array.Resize(ref _buffer, size);
            }
            var span = new Span<byte>(_buffer, _length, count);
            _length += count;
            return span;
        }

        /// <summary>One byte.</summary>
        public void WriteByte(byte value) => Grow(1)[0] = value;

        /// <summary>A boolean as 0 or 1.</summary>
        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        /// <summary>A 16-bit unsigned number.</summary>
        public void WriteUInt16(ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(Grow(2), value);

        /// <summary>A 32-bit signed number.</summary>
        public void WriteInt32(int value) => BinaryPrimitives.WriteInt32LittleEndian(Grow(4), value);

        /// <summary>A 64-bit signed number.</summary>
        public void WriteInt64(long value) => BinaryPrimitives.WriteInt64LittleEndian(Grow(8), value);

        /// <summary>A 64-bit unsigned number.</summary>
        public void WriteUInt64(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(Grow(8), value);

        /// <summary>A 32-bit float; NaN and infinities are refused.</summary>
        public void WriteSingle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new MessageRejectedException(RejectReason.NotFinite);
            BinaryPrimitives.WriteInt32LittleEndian(Grow(4), BitConverter.SingleToInt32Bits(value));
        }

        /// <summary>A 64-bit float; NaN and infinities are refused.</summary>
        public void WriteDouble(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new MessageRejectedException(RejectReason.NotFinite);
            BinaryPrimitives.WriteInt64LittleEndian(Grow(8), BitConverter.DoubleToInt64Bits(value));
        }

        /// <summary>An unsigned LEB128 number (1 to 5 bytes).</summary>
        public void WriteVarUInt32(uint value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0) b |= 0x80;
                WriteByte(b);
            }
            while (value != 0);
        }

        /// <summary>A count of elements that follow; refused above the limits' MaxCount.</summary>
        public void WriteCount(int count)
        {
            if (count < 0 || count > _limits.MaxCount) throw new MessageRejectedException(RejectReason.CountTooLarge, $"{count} > {_limits.MaxCount}");
            WriteVarUInt32((uint)count);
        }

        /// <summary>
        /// A UTF-8 string with a length prefix; refused when longer than the limits' MaxStringBytes, or when it contains
        /// control characters (newlines only when <paramref name="allowNewlines"/>), direction overrides or unpaired surrogates.
        /// </summary>
        public void WriteString(string value, bool allowNewlines = false)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            foreach (char c in value)
                if (TextRules.IsForbidden(c, allowNewlines)) throw new MessageRejectedException(RejectReason.ForbiddenCharacter, $"U+{(int)c:X4}");
            int byteCount;
            try { byteCount = StrictUtf8.GetByteCount(value); }
            catch (EncoderFallbackException) { throw new MessageRejectedException(RejectReason.InvalidUtf8, "unpaired surrogate"); }
            if (byteCount > _limits.MaxStringBytes)
                throw new MessageRejectedException(RejectReason.StringTooLong, $"{byteCount} > {_limits.MaxStringBytes} bytes");
            WriteVarUInt32((uint)byteCount);
            StrictUtf8.GetBytes(value, Grow(byteCount));
        }

        /// <summary>A block of bytes with a length prefix.</summary>
        public void WriteBlock(ReadOnlySpan<byte> bytes)
        {
            WriteVarUInt32((uint)bytes.Length);
            bytes.CopyTo(Grow(bytes.Length));
        }

        /// <summary>Raw bytes, no length prefix.</summary>
        public void WriteBytes(ReadOnlySpan<byte> bytes) => bytes.CopyTo(Grow(bytes.Length));

        /// <summary>Call before writing a nested value; refuses nesting deeper than the limits' MaxDepth.</summary>
        public void EnterNested()
        {
            if (++_depth > _limits.MaxDepth) throw new MessageRejectedException(RejectReason.TooDeep, $"deeper than {_limits.MaxDepth}");
        }

        /// <summary>Call after writing a nested value.</summary>
        public void ExitNested()
        {
            if (_depth == 0) throw new InvalidOperationException("ExitNested without EnterNested.");
            _depth--;
        }
    }
}
