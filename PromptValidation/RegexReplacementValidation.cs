using JpScratch.Editor;

namespace JpScratch.PromptValidation;

internal static class RegexReplacementValidation
{
    internal static bool RunSelfTests()
    {
        bool pass = true;
        pass &= Expect(@"\r\n", "\r\n", "CRLF");
        pass &= Expect(@"\n", "\n", "LF");
        pass &= Expect(@"\t", "\t", "tab");
        pass &= Expect(@"\\n", @"\n", "escaped backslash");
        pass &= Expect(@"$1\n${name}", "$1\n${name}", "capture references");
        pass &= Expect(@"\q", @"\q", "unknown escape");
        pass &= Expect("末尾\\", "末尾\\", "trailing backslash");
        return pass;
    }

    private static bool Expect(string input, string expected, string label)
    {
        string actual = RegexReplacement.ExpandEscapes(input);
        bool pass = actual == expected;
        Console.WriteLine(
            $"[RegexReplacement:{label}] {(pass ? "PASS" : "FAIL")} " +
            $"expected={Escape(expected)} actual={Escape(actual)}");
        return pass;
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);
}
