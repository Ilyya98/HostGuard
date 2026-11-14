using System;
using System.Text;

namespace HostGuard
{
    /// <summary>
    /// Helpers for showing text that came from other players (names, chat, sign text). Received strings are data: show
    /// them with <see cref="EscapeRichText"/> in any UI that parses markup, and pass names through
    /// <see cref="IsValidName"/> before accepting them.
    /// </summary>
    public static class SafeText
    {
        /// <summary>
        /// Removes control and direction-override characters (keeping newlines when <paramref name="allowNewlines"/>) and
        /// unpaired surrogates, and cuts the result to at most <paramref name="maxChars"/> UTF-16 units without splitting a
        /// surrogate pair.
        /// </summary>
        public static string Sanitize(string text, int maxChars, bool allowNewlines = false)
        {
            if (text == null) return string.Empty;
            if (maxChars < 0) throw new ArgumentOutOfRangeException(nameof(maxChars));
            var sb = new StringBuilder(Math.Min(text.Length, maxChars));
            for (int i = 0; i < text.Length && sb.Length < maxChars; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    {
                        if (sb.Length + 2 > maxChars) break;
                        sb.Append(c).Append(text[i + 1]);
                        i++;
                    }
                    continue;
                }
                if (char.IsLowSurrogate(c)) continue;
                if (TextRules.IsForbidden(c, allowNewlines)) continue;
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Makes text safe for UI systems that parse <c>&lt;tag&gt;</c> markup (Unity rich text, TextMeshPro, many others):
        /// every <c>&lt;</c> is followed by a zero-width space so no tag can open. The visible text does not change.
        /// Use an HTML encoder instead for HTML.
        /// </summary>
        public static string EscapeRichText(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0) return text ?? string.Empty;
            return text.Replace("<", "<​");
        }

        /// <summary>
        /// True for a player name: 1 to <paramref name="maxChars"/> characters of letters, digits, spaces, '-', '_' and
        /// '.', not starting or ending with a space and without two spaces in a row. Strict on purpose: names appear
        /// everywhere and must never be confusing or carry markup.
        /// </summary>
        public static bool IsValidName(string name, int maxChars = 24)
        {
            if (string.IsNullOrEmpty(name) || name.Length > maxChars) return false;
            if (name[0] == ' ' || name[name.Length - 1] == ' ') return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == ' ')
                {
                    if (name[i - 1] == ' ') return false;
                    continue;
                }
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') continue;
                return false;
            }
            return true;
        }
    }
}
