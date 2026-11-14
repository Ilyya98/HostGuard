namespace HostGuard
{
    /// <summary>Which characters untrusted text may contain.</summary>
    internal static class TextRules
    {
        /// <summary>
        /// True for characters that never belong in names or chat: C0 and C1 control characters and DEL (newline only when
        /// allowed), and the Unicode direction overrides and isolates (U+202A–U+202E, U+2066–U+2069), which can make text
        /// display differently from what it contains.
        /// </summary>
        public static bool IsForbidden(char c, bool allowNewlines)
        {
            if (c == '\n') return !allowNewlines;
            if (c < 0x20 || c == 0x7F) return true;
            if (c >= 0x80 && c <= 0x9F) return true;
            if (c >= '‪' && c <= '‮') return true;
            if (c >= '⁦' && c <= '⁩') return true;
            return false;
        }
    }
}
