using System;
using System.Collections.Generic;

namespace HostGuard
{
    /// <summary>Decodes one message's payload. Read everything the message holds; the registry checks nothing is left over.</summary>
    public delegate TMessage MessageDecoder<TMessage>(ref BoundedReader reader);

    /// <summary>
    /// The fixed whitelist of message types (a numeric id → a decoder and an apply step). Nothing in the data ever names a
    /// type or a method: an id is looked up in this table, and unknown ids are refused. Register everything at startup,
    /// then <see cref="Seal"/>; a sealed registry cannot change, and only a sealed registry dispatches.
    /// <para>
    /// Frame format: <c>[u16 type id][var-uint payload length][payload]</c>. Dispatch is two-phase: the payload is decoded
    /// completely and checked for trailing bytes first, then the optional <c>allow</c> rule runs (ownership, turn order…),
    /// and only then <c>apply</c>, so a rejected message never half-applies.
    /// </para>
    /// </summary>
    public sealed class MessageRegistry<TContext>
    {
        abstract class Entry
        {
            public string Name;
            public int MaxPayloadBytes;
            public abstract void Run(ReadOnlySpan<byte> payload, MessageLimits limits, TContext context);
        }

        sealed class Entry<TMessage> : Entry
        {
            public MessageDecoder<TMessage> Decode;
            public Func<TMessage, TContext, bool> Allow;
            public Action<TMessage, TContext> Apply;

            public override void Run(ReadOnlySpan<byte> payload, MessageLimits limits, TContext context)
            {
                var reader = new BoundedReader(payload, limits);
                var message = Decode(ref reader);
                reader.EnsureEnd();
                if (Allow != null && !Allow(message, context)) throw new MessageRejectedException(RejectReason.NotAllowed, Name);
                Apply(message, context);
            }
        }

        readonly Dictionary<ushort, Entry> _entries = new Dictionary<ushort, Entry>();
        readonly MessageLimits _limits;

        /// <summary>Creates an empty registry using <paramref name="limits"/> (the defaults when null).</summary>
        public MessageRegistry(MessageLimits limits = null) => _limits = limits ?? MessageLimits.Default;

        /// <summary>True once <see cref="Seal"/> was called.</summary>
        public bool IsSealed { get; private set; }

        /// <summary>The limits used for frames and payloads.</summary>
        public MessageLimits Limits => _limits;

        /// <summary>
        /// Adds a message type. <paramref name="maxPayloadBytes"/> caps this type's payload (keep it as small as the message
        /// allows); <paramref name="allow"/> (optional) is a game rule checked after decoding, before applying; returning
        /// false rejects the message as <see cref="RejectReason.NotAllowed"/>.
        /// </summary>
        public void Register<TMessage>(ushort typeId, string name, int maxPayloadBytes, MessageDecoder<TMessage> decode,
            Action<TMessage, TContext> apply, Func<TMessage, TContext, bool> allow = null)
        {
            if (IsSealed) throw new InvalidOperationException("The registry is sealed; register every message type at startup.");
            if (_entries.ContainsKey(typeId)) throw new ArgumentException($"Message type {typeId} is already registered.", nameof(typeId));
            if (maxPayloadBytes < 0 || maxPayloadBytes > _limits.MaxMessageBytes) throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));
            _entries.Add(typeId, new Entry<TMessage>
            {
                Name = name ?? typeId.ToString(),
                MaxPayloadBytes = maxPayloadBytes,
                Decode = decode ?? throw new ArgumentNullException(nameof(decode)),
                Apply = apply ?? throw new ArgumentNullException(nameof(apply)),
                Allow = allow,
            });
        }

        /// <summary>Freezes the whitelist. Call once all types are registered, before the first <see cref="Dispatch"/>.</summary>
        public void Seal() => IsSealed = true;

        /// <summary>True when <paramref name="typeId"/> is on the whitelist.</summary>
        public bool Contains(ushort typeId) => _entries.ContainsKey(typeId);

        /// <summary>
        /// Checks, decodes and applies one frame. Returns the message type id. Throws <see cref="MessageRejectedException"/>
        /// for any broken rule (unknown type, wrong length, oversized payload, bad payload, refused by the allow rule).
        /// </summary>
        public ushort Dispatch(ReadOnlySpan<byte> frame, TContext context)
        {
            if (!IsSealed) throw new InvalidOperationException("Seal the registry before dispatching.");
            var header = new BoundedReader(frame, _limits);
            ushort typeId = header.ReadUInt16();
            if (!_entries.TryGetValue(typeId, out var entry)) throw new MessageRejectedException(RejectReason.UnknownMessageType, typeId.ToString());
            uint length = header.ReadVarUInt32();
            if (length > entry.MaxPayloadBytes) throw new MessageRejectedException(RejectReason.TooLarge, $"{entry.Name}: {length} > {entry.MaxPayloadBytes} bytes");
            if (length != header.Remaining) throw new MessageRejectedException(RejectReason.Malformed, $"{entry.Name}: length {length}, {header.Remaining} bytes follow");
            entry.Run(frame.Slice(header.Position), _limits, context);
            return typeId;
        }

        /// <summary>Writes a frame: the type id, the payload's length and the payload (written with its own writer).</summary>
        public static void WriteFrame(BoundedWriter frame, ushort typeId, BoundedWriter payload)
        {
            frame.WriteUInt16(typeId);
            frame.WriteBlock(payload.Written);
        }
    }
}
