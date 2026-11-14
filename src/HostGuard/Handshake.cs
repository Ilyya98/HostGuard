using System;

namespace HostGuard
{
    /// <summary>
    /// A 64-bit FNV-1a hash for checking that host and client loaded the same content (definition files, mods).
    /// It detects accidental mismatches only; it is NOT cryptographic and proves nothing against a modified client.
    /// Feed the content in a fixed order (for example, files sorted by name).
    /// </summary>
    public struct ContentHash
    {
        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        ulong _value;
        bool _started;

        /// <summary>The hash so far.</summary>
        public ulong Value => _started ? _value : Offset;

        /// <summary>Adds bytes.</summary>
        public void Add(ReadOnlySpan<byte> bytes)
        {
            if (!_started) { _value = Offset; _started = true; }
            foreach (byte b in bytes)
            {
                _value ^= b;
                _value *= Prime;
            }
        }

        /// <summary>Adds a length, then the bytes, so ("ab","c") and ("a","bc") hash differently.</summary>
        public void AddEntry(ReadOnlySpan<byte> bytes)
        {
            Span<byte> len = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(len, bytes.Length);
            Add(len);
            Add(bytes);
        }
    }

    /// <summary>The first message a client sends.</summary>
    public readonly struct JoinRequest
    {
        /// <summary>Wire protocol version.</summary>
        public readonly ushort ProtocolVersion;
        /// <summary>Game build id; host and client must match.</summary>
        public readonly string BuildId;
        /// <summary><see cref="ContentHash"/> of the client's loaded content.</summary>
        public readonly ulong ContentHash;
        /// <summary>The player's name.</summary>
        public readonly string PlayerName;

        /// <summary>Creates a request.</summary>
        public JoinRequest(ushort protocolVersion, string buildId, ulong contentHash, string playerName)
        {
            ProtocolVersion = protocolVersion;
            BuildId = buildId ?? string.Empty;
            ContentHash = contentHash;
            PlayerName = playerName ?? string.Empty;
        }

        /// <summary>Longest build id accepted, in bytes.</summary>
        public const int MaxBuildIdBytes = 64;
        /// <summary>Longest player name accepted, in characters.</summary>
        public const int MaxNameChars = 24;

        /// <summary>Writes the request.</summary>
        public void Write(BoundedWriter w)
        {
            w.WriteUInt16(ProtocolVersion);
            w.WriteString(BuildId);
            w.WriteUInt64(ContentHash);
            w.WriteString(PlayerName);
        }

        /// <summary>Reads a request (bounded; the caller checks the end).</summary>
        public static JoinRequest Read(ref BoundedReader r)
        {
            ushort version = r.ReadUInt16();
            string build = r.ReadString(MaxBuildIdBytes);
            ulong hash = r.ReadUInt64();
            string name = r.ReadString(MaxNameChars * 4);
            return new JoinRequest(version, build, hash, name);
        }
    }

    /// <summary>Why a join was refused, or <see cref="Ok"/>.</summary>
    public enum JoinVerdict : byte
    {
        /// <summary>The client may join.</summary>
        Ok = 0,
        /// <summary>Different wire protocol.</summary>
        WrongProtocol,
        /// <summary>Different game build.</summary>
        WrongBuild,
        /// <summary>Different content (definitions, mods).</summary>
        WrongContent,
        /// <summary>The name breaks <see cref="SafeText.IsValidName"/>.</summary>
        BadName,
        /// <summary>The session is full.</summary>
        Full,
    }

    /// <summary>The host's join check: same protocol, same build, same content, a valid name, a free slot.</summary>
    public static class Handshake
    {
        /// <summary>Checks a decoded <see cref="JoinRequest"/> against the host's own values.</summary>
        public static JoinVerdict Check(in JoinRequest request, ushort protocolVersion, string buildId, ulong contentHash,
            int playerCount, int maxPlayers)
        {
            if (request.ProtocolVersion != protocolVersion) return JoinVerdict.WrongProtocol;
            if (!string.Equals(request.BuildId, buildId, StringComparison.Ordinal)) return JoinVerdict.WrongBuild;
            if (request.ContentHash != contentHash) return JoinVerdict.WrongContent;
            if (!SafeText.IsValidName(request.PlayerName, JoinRequest.MaxNameChars)) return JoinVerdict.BadName;
            if (playerCount >= maxPlayers) return JoinVerdict.Full;
            return JoinVerdict.Ok;
        }
    }
}
