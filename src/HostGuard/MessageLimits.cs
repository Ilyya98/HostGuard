using System;

namespace HostGuard
{
    /// <summary>
    /// Hard caps applied while reading and writing. Every limit protects against one kind of hostile input: huge
    /// messages, huge strings, counts that would make you allocate millions of elements, and deeply nested data that
    /// would exhaust the stack. Pick the smallest values your protocol allows.
    /// </summary>
    public sealed class MessageLimits
    {
        /// <summary>Largest whole message, in bytes.</summary>
        public int MaxMessageBytes { get; }

        /// <summary>Largest string, in UTF-8 bytes.</summary>
        public int MaxStringBytes { get; }

        /// <summary>Largest count (number of elements) a message may announce.</summary>
        public int MaxCount { get; }

        /// <summary>Deepest nesting allowed (see <see cref="BoundedReader.EnterNested"/>).</summary>
        public int MaxDepth { get; }

        /// <summary>Creates limits. Every value must be at least 1.</summary>
        public MessageLimits(int maxMessageBytes, int maxStringBytes, int maxCount, int maxDepth)
        {
            if (maxMessageBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
            if (maxStringBytes < 1) throw new ArgumentOutOfRangeException(nameof(maxStringBytes));
            if (maxCount < 1) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            MaxMessageBytes = maxMessageBytes;
            MaxStringBytes = maxStringBytes;
            MaxCount = maxCount;
            MaxDepth = maxDepth;
        }

        /// <summary>Reasonable defaults for game commands: 64 KiB messages, 256-byte strings, 1024 elements, depth 8.</summary>
        public static readonly MessageLimits Default = new MessageLimits(64 * 1024, 256, 1024, 8);
    }
}
