using System;
using System.IO;

namespace HostGuard
{
    /// <summary>
    /// Turns a name that came from data (a save slot, a map name, a mod file) into a path inside a fixed root folder, or
    /// refuses it. Only plain relative names are accepted: no rooted paths, no drive letters, no <c>..</c>, no empty
    /// segments, no reserved Windows device names, no trailing dots or spaces, and only letters, digits, '-', '_', '.'
    /// and '/' as a separator. The result is checked once more after normalising, so it is always inside the root.
    /// </summary>
    public static class SafePath
    {
        static readonly string[] Reserved =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        /// <summary>Longest relative name accepted.</summary>
        public const int MaxRelativeLength = 200;

        /// <summary>True when <paramref name="relative"/> is a plain relative name by the rules above.</summary>
        public static bool IsSafeRelative(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > MaxRelativeLength) return false;
            foreach (char c in relative)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                          || c == '-' || c == '_' || c == '.' || c == '/';
                if (!ok) return false;
            }
            foreach (string segment in relative.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..") return false;
                if (segment[segment.Length - 1] == '.') return false;
                int dot = segment.IndexOf('.');
                string stem = dot < 0 ? segment : segment.Substring(0, dot);
                foreach (string r in Reserved)
                    if (string.Equals(stem, r, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>
        /// Combines <paramref name="root"/> (a folder your code chose) and <paramref name="relative"/> (from data). Returns
        /// false, with <paramref name="fullPath"/> null, when the name is not safe or would leave the root.
        /// </summary>
        public static bool TryResolve(string root, string relative, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrEmpty(root)) throw new ArgumentException("A root folder is required.", nameof(root));
            if (!IsSafeRelative(relative)) return false;
            string rootFull = Path.GetFullPath(root);
            if (!rootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                rootFull += Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!candidate.StartsWith(rootFull, StringComparison.Ordinal) || candidate.Length == rootFull.Length) return false;
            fullPath = candidate;
            return true;
        }
    }
}
