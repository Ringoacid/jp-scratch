using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace JpScratch.PromptValidation;

internal static class DocumentDiffValidation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static bool RunSelfTests()
    {
        (string Name, string Source, string Corrected, int Changes, bool Accepted)[] tests =
        [
            ("変更なし", "文章です。", "文章です。", 0, true),
            ("置換", "この文章ア誤りです。", "この文章が誤りです。", 1, true),
            ("離れた2変更", "文章ア、間違いが二ともある。", "文章が、間違いが二つともある。", 2, true),
            ("途中への挿入", "誤りがあ場合", "誤りがある場合", 1, true),
            ("先頭への挿入", "文章です", "この文章です", 1, true),
            ("削除", "これはは誤り", "これは誤り", 1, true),
            ("サロゲートペア", "😀文s尿です", "😀文章です", 1, true),
            ("結合文字", "か\u3099文章ア", "か\u3099文章が", 1, true),
            ("改行コード差", "一行目\r\n二行目", "一行目\n二行目", 0, true),
            ("末尾改行差", "文章です。", "文章です。\n", 0, true),
            ("Markdown保持", "# 見出し\n\n文s尿です。", "# 見出し\n\n文章です。", 1, true),
            ("空文書への挿入拒否", "", "文章", 0, false),
            ("大規模書換拒否", new string('あ', 100), new string('い', 100), 0, false)
        ];

        bool passed = true;
        foreach ((string name, string source, string corrected, int expected, bool accepted) in tests)
        {
            DocumentDiffResult result = DocumentDiff.Create(source, corrected);
            string applied = result.Accepted
                ? DocumentDiff.Apply(TrimTerminalLineBreaks(source), result.Changes)
                : string.Empty;
            bool roundTrip = !result.Accepted ||
                string.Equals(
                    NormalizeLineEndings(applied),
                    NormalizeLineEndings(TrimTerminalLineBreaks(corrected)),
                    StringComparison.Ordinal);
            bool testPassed =
                result.Accepted == accepted &&
                (!accepted || result.Changes.Count == expected) &&
                roundTrip &&
                result.Changes.All(IsUtf16BoundarySafe);

            Console.WriteLine(
                $"全文差分（{name}）: {(testPassed ? "PASS" : "FAIL")}" +
                $" / 提案 {result.Changes.Count}" +
                (result.RejectionReason is null ? string.Empty : $" / {result.RejectionReason}"));
            passed &= testPassed;
        }

        bool randomizedPass = RunRandomizedRoundTrips();
        Console.WriteLine(
            $"全文差分（ランダム往復200件）: {(randomizedPass ? "PASS" : "FAIL")}");
        passed &= randomizedPass;

        return passed;
    }

    internal static int AnalyzeSavedResults(
        string resultsPath,
        IReadOnlyList<ValidationCase> cases)
    {
        string fullPath = Path.GetFullPath(resultsPath);
        string[] files = Directory.Exists(fullPath)
            ? Directory.GetFiles(fullPath, "full-rewrite-safe-*.json")
            : [fullPath];

        int documents = 0;
        int accepted = 0;
        int changes = 0;
        int rejected = 0;
        Dictionary<string, int> changesByCase = [];
        Dictionary<string, int> distinctChanges = [];

        foreach (string file in files.Order())
        {
            List<CaseResult> results =
                JsonSerializer.Deserialize<List<CaseResult>>(
                    File.ReadAllText(file),
                    JsonOptions) ?? [];

            foreach (CaseResult saved in results.Where(result => result.CorrectedText is not null))
            {
                ValidationCase? testCase = cases.FirstOrDefault(
                    item => item.Id == saved.Id);
                if (testCase is null)
                    continue;

                string source = PromptFactory.BuildFullDocument(testCase);
                DocumentDiffResult diff = DocumentDiff.Create(source, saved.CorrectedText!);
                documents++;
                if (diff.Accepted)
                {
                    accepted++;
                    changes += diff.Changes.Count;
                    changesByCase[saved.Id] =
                        changesByCase.GetValueOrDefault(saved.Id) + diff.Changes.Count;
                    foreach (DocumentChange change in diff.Changes)
                    {
                        string description = $"「{change.Original}」→「{change.Suggestion}」";
                        distinctChanges[description] =
                            distinctChanges.GetValueOrDefault(description) + 1;
                    }
                }
                else
                {
                    rejected++;
                    Console.WriteLine(
                        $"  REJECT {Path.GetFileName(file)} / {saved.Id} / " +
                        $"{saved.Iteration}: {diff.RejectionReason}");
                }
            }
        }

        Console.WriteLine(
            $"保存済み全文応答: {accepted}/{documents} 受理 / " +
            $"提案 {changes} 件 / 破棄 {rejected} 件");
        foreach ((string id, int count) in changesByCase.OrderBy(item => item.Key))
            Console.WriteLine($"  {id}: {count} 件");
        foreach ((string description, int count) in
                 distinctChanges.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
        {
            Console.WriteLine($"  {count}x {description}");
        }
        return rejected == 0 && documents > 0 ? 0 : 1;
    }

    private static bool IsUtf16BoundarySafe(DocumentChange change)
    {
        if (change.Original.Length == 0)
            return false;

        return !char.IsLowSurrogate(change.Original[0]) &&
            !char.IsHighSurrogate(change.Original[^1]) &&
            CharUnicodeInfo.GetUnicodeCategory(change.Original, 0) !=
                UnicodeCategory.NonSpacingMark;
    }

    private static bool RunRandomizedRoundTrips()
    {
        string[] alphabet =
            ["あ", "い", "う", "文", "章", "。", "、", "A", "1", "😀", "か\u3099"];
        Random random = new(20260729);

        for (int iteration = 0; iteration < 200; iteration++)
        {
            List<string> sourceElements = Enumerable.Range(0, 100)
                .Select(_ => alphabet[random.Next(alphabet.Length)])
                .ToList();
            List<string> correctedElements = [.. sourceElements];

            int edits = random.Next(1, 6);
            for (int edit = 0; edit < edits; edit++)
            {
                int index = random.Next(correctedElements.Count);
                switch (random.Next(3))
                {
                    case 0:
                        correctedElements[index] = alphabet[random.Next(alphabet.Length)];
                        break;
                    case 1:
                        correctedElements.Insert(index, alphabet[random.Next(alphabet.Length)]);
                        break;
                    default:
                        if (correctedElements.Count > 1)
                            correctedElements.RemoveAt(index);
                        break;
                }
            }

            string source = string.Concat(sourceElements);
            string corrected = string.Concat(correctedElements);
            DocumentDiffResult result = DocumentDiff.Create(source, corrected);
            if (!result.Accepted ||
                !string.Equals(
                    DocumentDiff.Apply(source, result.Changes),
                    corrected,
                    StringComparison.Ordinal) ||
                !result.Changes.All(IsUtf16BoundarySafe))
            {
                return false;
            }
        }

        return true;
    }

    private static string TrimTerminalLineBreaks(string value) =>
        value.TrimEnd('\r', '\n');

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
}
