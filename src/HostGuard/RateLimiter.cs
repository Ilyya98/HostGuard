using System;
using System.Collections.Generic;

namespace HostGuard
{
    /// <summary>
    /// A token bucket per sender: each sender may send a burst of <see cref="Burst"/> messages, then
    /// <see cref="PerSecond"/> messages a second. Integer arithmetic only (thousandths of a token), and the caller supplies
    /// the time in milliseconds, so results are exact and repeatable in tests and replays. A clock that goes backwards
    /// refills nothing.
    /// </summary>
    public sealed class RateLimiter
    {
        sealed class Bucket
        {
            public long Milli;
            public long LastMs;
        }

        readonly Dictionary<int, Bucket> _buckets = new Dictionary<int, Bucket>();

        /// <summary>Messages a sender may send at once.</summary>
        public int Burst { get; }

        /// <summary>Messages a second, after the burst.</summary>
        public int PerSecond { get; }

        /// <summary>Creates a limiter.</summary>
        public RateLimiter(int burst, int perSecond)
        {
            if (burst < 1) throw new ArgumentOutOfRangeException(nameof(burst));
            if (perSecond < 1) throw new ArgumentOutOfRangeException(nameof(perSecond));
            Burst = burst;
            PerSecond = perSecond;
        }

        /// <summary>Takes <paramref name="cost"/> tokens from the sender's bucket; false when it does not have them (refuse the message).</summary>
        public bool TryTake(int senderId, long nowMs, int cost = 1)
        {
            if (cost < 1) throw new ArgumentOutOfRangeException(nameof(cost));
            if (!_buckets.TryGetValue(senderId, out var b))
            {
                b = new Bucket { Milli = Burst * 1000L, LastMs = nowMs };
                _buckets.Add(senderId, b);
            }
            long elapsed = nowMs - b.LastMs;
            if (elapsed > 0)
            {
                // PerSecond tokens per 1000 ms = PerSecond thousandths of a token per ms.
                // Cap the elapsed time at what fills an empty bucket, so the product cannot overflow.
                long fillMs = Burst * 1000L / PerSecond + 1;
                b.Milli = Math.Min(Burst * 1000L, b.Milli + Math.Min(elapsed, fillMs) * PerSecond);
                b.LastMs = nowMs;
            }
            long need = cost * 1000L;
            if (b.Milli < need) return false;
            b.Milli -= need;
            return true;
        }

        /// <summary>Forgets a sender (after they leave).</summary>
        public void Forget(int senderId) => _buckets.Remove(senderId);
    }
}
