using System;

namespace HostGuard
{
    /// <summary>Why incoming data was refused.</summary>
    public enum RejectReason : byte
    {
        /// <summary>The data ended before the value was complete.</summary>
        Truncated = 1,
        /// <summary>The message (or a byte block in it) is larger than allowed.</summary>
        TooLarge,
        /// <summary>A string is longer than allowed.</summary>
        StringTooLong,
        /// <summary>A count (array length) is larger than allowed, or larger than the data left could hold.</summary>
        CountTooLarge,
        /// <summary>Nested values go deeper than allowed.</summary>
        TooDeep,
        /// <summary>A floating-point value is NaN or infinite.</summary>
        NotFinite,
        /// <summary>A number or enum value is outside its allowed range.</summary>
        OutOfRange,
        /// <summary>A string is not valid UTF-8.</summary>
        InvalidUtf8,
        /// <summary>A string contains control or direction-override characters.</summary>
        ForbiddenCharacter,
        /// <summary>Bytes are left over after the message was read.</summary>
        TrailingBytes,
        /// <summary>The message type is not on the whitelist.</summary>
        UnknownMessageType,
        /// <summary>The sender sent too much, too fast.</summary>
        RateLimited,
        /// <summary>The data is structurally wrong (bad length prefix, bad boolean, overlong variable-length number…).</summary>
        Malformed,
        /// <summary>Well-formed, but a game rule refuses it (not the sender's character, not their turn…).</summary>
        NotAllowed,
    }

    /// <summary>
    /// Thrown when incoming data breaks a rule. Readers fail closed: the first broken rule stops reading, and nothing
    /// read so far should be used. Catch it at the message boundary (see <see cref="HostGate{TContext}"/>), never inside
    /// game logic.
    /// </summary>
    public sealed class MessageRejectedException : Exception
    {
        /// <summary>The rule that was broken.</summary>
        public RejectReason Reason { get; }

        /// <summary>Creates the exception.</summary>
        public MessageRejectedException(RejectReason reason, string detail = null)
            : base(detail == null ? reason.ToString() : reason + ": " + detail)
        {
            Reason = reason;
        }
    }
}
