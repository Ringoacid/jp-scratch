namespace JpScratch.PromptValidation;

internal static class Evaluator
{
    internal static CaseResult Evaluate(
        ValidationCase testCase,
        ProbeResult probe,
        string variant,
        int iteration)
    {
        List<string> failures = [];

        if (probe.CorrectedText is not null)
            return EvaluateFullRewrite(testCase, probe, variant, iteration, failures);

        List<ResolvedCorrection> resolved = [];
        int discarded = 0;

        foreach (Correction correction in probe.Response.Corrections)
        {
            if (string.IsNullOrEmpty(correction.Original) ||
                correction.Original == correction.Suggestion)
            {
                failures.Add($"不正な提案: original={Quote(correction.Original)}");
                discarded++;
                continue;
            }

            int? start = ResolvePosition(testCase.Text, correction);
            if (start is null)
            {
                failures.Add($"原文位置を解決できない提案: {Quote(correction.Original)}");
                discarded++;
                continue;
            }

            resolved.Add(new ResolvedCorrection(correction, start.Value));
        }

        if (testCase.Kind == "style")
        {
            foreach (ResolvedCorrection item in resolved)
            {
                failures.Add(
                    $"文体保護違反: {Quote(item.Correction.Original)} → " +
                    $"{Quote(item.Correction.Suggestion)} ({item.Correction.Category})");
            }
        }
        else
        {
            string corrected = ApplyCorrections(testCase.Text, resolved);
            foreach (ExpectedChange expected in testCase.ExpectedChanges ?? [])
            {
                if (corrected.Contains(expected.From, StringComparison.Ordinal) ||
                    !expected.To.Any(
                        replacement => corrected.Contains(replacement, StringComparison.Ordinal)))
                {
                    failures.Add(
                        $"期待修正を確認できない: {Quote(expected.From)} → " +
                        string.Join(" または ", expected.To.Select(Quote)));
                }
            }
        }

        return new CaseResult(
            testCase.Id,
            testCase.Kind,
            variant,
            iteration,
            failures.Count == 0,
            failures,
            probe.Response.Corrections,
            null,
            discarded,
            probe.Usage.PromptTokens,
            probe.Usage.CandidateTokens,
            probe.Elapsed.TotalMilliseconds,
            probe.CostUsd);
    }

    private static CaseResult EvaluateFullRewrite(
        ValidationCase testCase,
        ProbeResult probe,
        string variant,
        int iteration,
        List<string> failures)
    {
        string source = Normalize(PromptFactory.BuildFullDocument(testCase));
        string corrected = Normalize(probe.CorrectedText!);

        if (testCase.BeforeContext is not null &&
            !corrected.Contains(Normalize(testCase.BeforeContext), StringComparison.Ordinal))
            failures.Add("修正対象ではない前文脈が変更または欠落した");
        if (testCase.AfterContext is not null &&
            !corrected.Contains(Normalize(testCase.AfterContext), StringComparison.Ordinal))
            failures.Add("修正対象ではない後文脈が変更または欠落した");

        int lengthDelta = Math.Abs(source.Length - corrected.Length);
        if (lengthDelta > Math.Max(20, source.Length / 5))
            failures.Add($"全文の長さが大きく変化した（差 {lengthDelta} 文字）");

        if (testCase.Kind == "style")
        {
            if (!string.Equals(source, corrected, StringComparison.Ordinal))
                failures.Add($"文体保護違反: {DescribeDifference(source, corrected)}");
        }
        else
        {
            foreach (ExpectedChange expected in testCase.ExpectedChanges ?? [])
            {
                if (corrected.Contains(expected.From, StringComparison.Ordinal) ||
                    !expected.To.Any(
                        replacement => corrected.Contains(replacement, StringComparison.Ordinal)))
                {
                    failures.Add(
                        $"期待修正を確認できない: {Quote(expected.From)} → " +
                        string.Join(" または ", expected.To.Select(Quote)));
                }
            }
        }

        return new CaseResult(
            testCase.Id,
            testCase.Kind,
            variant,
            iteration,
            failures.Count == 0,
            failures,
            [],
            corrected,
            0,
            probe.Usage.PromptTokens,
            probe.Usage.CandidateTokens,
            probe.Elapsed.TotalMilliseconds,
            probe.CostUsd);
    }

    internal static int? ResolvePosition(string text, Correction correction)
    {
        string combined = correction.LeftContext + correction.Original + correction.RightContext;
        int combinedStart = text.IndexOf(combined, StringComparison.Ordinal);
        if (combinedStart >= 0)
            return combinedStart + correction.LeftContext.Length;

        List<int> candidates = FindAll(text, correction.Original);
        if (candidates.Count == 1)
            return candidates[0];
        if (candidates.Count == 0)
            return null;

        return candidates
            .Select(index => new
            {
                Index = index,
                Score = MatchingSuffixLength(
                    text[..index],
                    correction.LeftContext)
            })
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .First()
            .Index;
    }

    private static string ApplyCorrections(
        string text,
        IReadOnlyList<ResolvedCorrection> corrections)
    {
        string result = text;
        foreach (ResolvedCorrection item in corrections.OrderByDescending(item => item.Start))
        {
            result = result.Remove(item.Start, item.Correction.Original.Length)
                .Insert(item.Start, item.Correction.Suggestion);
        }

        return result;
    }

    private static List<int> FindAll(string text, string value)
    {
        List<int> results = [];
        int start = 0;
        while (start <= text.Length - value.Length)
        {
            int index = text.IndexOf(value, start, StringComparison.Ordinal);
            if (index < 0)
                break;

            results.Add(index);
            start = index + Math.Max(1, value.Length);
        }

        return results;
    }

    private static int MatchingSuffixLength(string textBefore, string leftContext)
    {
        int max = Math.Min(textBefore.Length, leftContext.Length);
        int count = 0;
        while (count < max &&
               textBefore[^(count + 1)] == leftContext[^(count + 1)])
        {
            count++;
        }

        return count;
    }

    private static string Quote(string value) => $"「{value}」";

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string DescribeDifference(string source, string corrected)
    {
        int prefix = 0;
        int maxPrefix = Math.Min(source.Length, corrected.Length);
        while (prefix < maxPrefix && source[prefix] == corrected[prefix])
            prefix++;

        int sourceEnd = source.Length;
        int correctedEnd = corrected.Length;
        while (sourceEnd > prefix && correctedEnd > prefix &&
               source[sourceEnd - 1] == corrected[correctedEnd - 1])
        {
            sourceEnd--;
            correctedEnd--;
        }

        string before = source[prefix..sourceEnd];
        string after = corrected[prefix..correctedEnd];
        return $"{Quote(Truncate(before))} → {Quote(Truncate(after))}";
    }

    private static string Truncate(string value) =>
        value.Length <= 40 ? value : value[..40] + "…";
}
