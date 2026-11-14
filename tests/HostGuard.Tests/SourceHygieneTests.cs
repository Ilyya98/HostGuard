using System.IO;
using NUnit.Framework;

namespace HostGuard.Tests
{
    /// <summary>
    /// The repository itself must not contain invisible or direction-changing characters ("Trojan Source"): source files
    /// spell such characters as \u escapes, so what a reviewer sees is what compiles.
    /// </summary>
    public class SourceHygieneTests
    {
        static bool IsHidden(char c) =>
            (c >= '\u200B' && c <= '\u200F') || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069') || c == '\uFEFF';

        [Test]
        public void NoHiddenUnicodeInTheRepository()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HostGuard.slnx"))) dir = dir.Parent;
            Assert.IsNotNull(dir, "Repository root not found.");

            int checkedFiles = 0;
            foreach (string pattern in new[] { "*.cs", "*.md", "*.yml", "*.csproj" })
            foreach (string file in Directory.GetFiles(dir.FullName, pattern, SearchOption.AllDirectories))
            {
                string sep = Path.DirectorySeparatorChar.ToString();
                if (file.Contains(sep + "bin" + sep) || file.Contains(sep + "obj" + sep) || file.Contains(sep + ".git" + sep)) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    foreach (char c in lines[i])
                        if (IsHidden(c)) Assert.Fail($"{file}:{i + 1} contains U+{(int)c:X4}; write it as an escape.");
                checkedFiles++;
            }
            Assert.Greater(checkedFiles, 10);
        }
    }
}
