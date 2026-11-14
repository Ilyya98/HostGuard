using System;
using System.Collections.Generic;

namespace HostGuard
{
    /// <summary>What the gate decided about one incoming frame.</summary>
    public enum GateVerdict : byte
    {
        /// <summary>Decoded and applied.</summary>
        Accepted = 0,
        /// <summary>Refused; nothing was applied. The sender got a strike.</summary>
        Rejected = 1,
        /// <summary>Refused, and the sender reached the strike limit: disconnect them. Their frames stay refused until <see cref="HostGate{TContext}.Forget"/>.</summary>
        Disconnect = 2,
    }

    /// <summary>The gate's answer for one frame.</summary>
    public readonly struct GateResult
    {
        /// <summary>The decision.</summary>
        public readonly GateVerdict Verdict;
        /// <summary>Why it was refused (when not accepted).</summary>
        public readonly RejectReason Reason;
        /// <summary>The message type (when accepted).</summary>
        public readonly ushort MessageType;

        /// <summary>Creates a result.</summary>
        public GateResult(GateVerdict verdict, RejectReason reason, ushort messageType)
        {
            Verdict = verdict;
            Reason = reason;
            MessageType = messageType;
        }

        /// <summary>True when the frame was applied.</summary>
        public bool Accepted => Verdict == GateVerdict.Accepted;
    }

    /// <summary>
    /// The host's front door for client frames: rate limit per sender, then the <see cref="MessageRegistry{TContext}"/>
    /// (whitelist, bounded decoding, allow rule, apply). Every refused frame is a strike; at <see cref="StrikeLimit"/>
    /// strikes the verdict is <see cref="GateVerdict.Disconnect"/>. Only <see cref="MessageRejectedException"/> counts as a
    /// refusal; any other exception from your apply code is a bug and is not swallowed.
    /// </summary>
    public sealed class HostGate<TContext>
    {
        readonly MessageRegistry<TContext> _registry;
        readonly RateLimiter _limiter;
        readonly Dictionary<int, int> _strikes = new Dictionary<int, int>();

        /// <summary>Refused frames that disconnect a sender.</summary>
        public int StrikeLimit { get; }

        /// <summary>Creates a gate over a sealed registry.</summary>
        public HostGate(MessageRegistry<TContext> registry, RateLimiter limiter, int strikeLimit = 5)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
            if (!registry.IsSealed) throw new ArgumentException("Seal the registry first.", nameof(registry));
            if (strikeLimit < 1) throw new ArgumentOutOfRangeException(nameof(strikeLimit));
            StrikeLimit = strikeLimit;
        }

        /// <summary>Strikes a sender has now.</summary>
        public int StrikesOf(int senderId) => _strikes.TryGetValue(senderId, out int s) ? s : 0;

        /// <summary>Checks and applies one frame from <paramref name="senderId"/> at <paramref name="nowMs"/>.</summary>
        public GateResult Receive(int senderId, ReadOnlySpan<byte> frame, long nowMs, TContext context)
        {
            if (StrikesOf(senderId) >= StrikeLimit) return new GateResult(GateVerdict.Disconnect, RejectReason.RateLimited, 0);
            if (!_limiter.TryTake(senderId, nowMs)) return Strike(senderId, RejectReason.RateLimited);
            try
            {
                return new GateResult(GateVerdict.Accepted, 0, _registry.Dispatch(frame, context));
            }
            catch (MessageRejectedException e)
            {
                return Strike(senderId, e.Reason);
            }
        }

        GateResult Strike(int senderId, RejectReason reason)
        {
            int s = StrikesOf(senderId) + 1;
            _strikes[senderId] = s;
            return new GateResult(s >= StrikeLimit ? GateVerdict.Disconnect : GateVerdict.Rejected, reason, 0);
        }

        /// <summary>Forgets a sender's strikes and rate (after they leave or are kicked).</summary>
        public void Forget(int senderId)
        {
            _strikes.Remove(senderId);
            _limiter.Forget(senderId);
        }
    }
}
