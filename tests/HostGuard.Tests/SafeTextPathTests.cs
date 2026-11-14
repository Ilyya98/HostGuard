using System.IO;
using NUnit.Framework;

namespace HostGuard.Tests
{
    public class SafeTextPathTests
    {
        [Test]
        public void SanitizeRemovesControlsAndCuts()
        {
            Assert.AreEqual("abc", SafeText.Sanitize("a\u0000b\u202Ec", 10));
            Assert.AreEqual("ab", SafeText.Sanitize("a\nb", 10));
            Assert.AreEqual("a\nb", SafeText.Sanitize("a\nb", 10, allowNewlines: true));
            Assert.AreEqual("abc", SafeText.Sanitize("abcdef", 3));
            Assert.AreEqual("ab", SafeText.Sanitize("a\uD800b", 10));
            Assert.AreEqual("a", SafeText.Sanitize("a🙂", 2), "A surrogate pair is never split.");
            Assert.AreEqual("", SafeText.Sanitize(null, 5));
        }

        [Test]
        public void RichTextCannotOpenTags()
        {
            string escaped = SafeText.EscapeRichText("<color=red>hi</color>");
            Assert.IsFalse(escaped.Contains("<c"));
            Assert.IsFalse(escaped.Contains("</"));
            Assert.AreEqual("plain", SafeText.EscapeRichText("plain"));
        }

        [TestCase("Ada", true)]
        [TestCase("Ada Lovelace", true)]
        [TestCase("x_1.2-y", true)]
        [TestCase("", false)]
        [TestCase(" Ada", false)]
        [TestCase("Ada ", false)]
        [TestCase("A  da", false)]
        [TestCase("<b>Ada</b>", false)]
        [TestCase("Ada\u202E", false)]
        [TestCase("abcdefghijklmnopqrstuvwxy", false)]
        public void Names(string name, bool valid) => Assert.AreEqual(valid, SafeText.IsValidName(name));

        [TestCase("slot1.sav", true)]
        [TestCase("maps/world_3.json", true)]
        [TestCase("../evil", false)]
        [TestCase("a/../../evil", false)]
        [TestCase("/etc/passwd", false)]
        [TestCase("C:/Windows", false)]
        [TestCase("a\\b", false)]
        [TestCase("a//b", false)]
        [TestCase("CON", false)]
        [TestCase("nul.txt", false)]
        [TestCase("lpt1", false)]
        [TestCase("file.", false)]
        [TestCase("file name", false)]
        [TestCase("", false)]
        public void RelativeNames(string relative, bool safe) => Assert.AreEqual(safe, SafePath.IsSafeRelative(relative));

        [Test]
        public void ResolvedPathsStayInsideTheRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "hostguard-root");
            Assert.IsTrue(SafePath.TryResolve(root, "saves/slot1.sav", out string full));
            StringAssert.StartsWith(Path.GetFullPath(root), full);
            Assert.IsFalse(SafePath.TryResolve(root, "../outside", out full));
            Assert.IsNull(full);
        }

        [Test]
        public void ContentHashSeparatesEntries()
        {
            var a = new ContentHash();
            a.AddEntry(new byte[] { 1, 2 });
            a.AddEntry(new byte[] { 3 });
            var b = new ContentHash();
            b.AddEntry(new byte[] { 1 });
            b.AddEntry(new byte[] { 2, 3 });
            Assert.AreNotEqual(a.Value, b.Value);

            var c = new ContentHash();
            c.Add(System.Text.Encoding.ASCII.GetBytes("a"));
            Assert.AreEqual(0xaf63dc4c8601ec8cUL, c.Value, "FNV-1a 64 test vector for \"a\".");
        }

        [Test]
        public void HandshakeChecksEverything()
        {
            const ulong hash = 42;
            var ok = new JoinRequest(3, "1.0.0", hash, "Ada");
            var w = new BoundedWriter();
            ok.Write(w);
            var r = new BoundedReader(w.Written);
            var read = JoinRequest.Read(ref r);
            r.EnsureEnd();

            Assert.AreEqual(JoinVerdict.Ok, Handshake.Check(read, 3, "1.0.0", hash, 1, 4));
            Assert.AreEqual(JoinVerdict.WrongProtocol, Handshake.Check(read, 4, "1.0.0", hash, 1, 4));
            Assert.AreEqual(JoinVerdict.WrongBuild, Handshake.Check(read, 3, "1.0.1", hash, 1, 4));
            Assert.AreEqual(JoinVerdict.WrongContent, Handshake.Check(read, 3, "1.0.0", 43, 1, 4));
            Assert.AreEqual(JoinVerdict.Full, Handshake.Check(read, 3, "1.0.0", hash, 4, 4));
            Assert.AreEqual(JoinVerdict.BadName, Handshake.Check(new JoinRequest(3, "1.0.0", hash, " "), 3, "1.0.0", hash, 1, 4));
        }
    }
}
