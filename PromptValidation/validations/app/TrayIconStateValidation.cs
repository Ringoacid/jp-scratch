using System.Runtime.CompilerServices;
using JpScratch.Services;

namespace JpScratch.PromptValidation;

/// <summary>
/// トレイアイコンの4状態表示（要件 3.1.1）の自己テスト。
/// 状態の優先順位、リソースの対応付け、<c>MainWindow</c> が辿る遷移、
/// そして .ico ファイル自体の格納形式（小サイズは DIB）を確認する。APIは呼ばない。
/// </summary>
internal static class TrayIconStateValidation
{
    internal static bool RunSelfTests()
    {
        bool priorityPassed = RunPriorityTests();
        bool mappingPassed = RunMappingTests();
        bool transitionPassed = RunTransitionTests();
        bool iconFilePassed = RunIconFileTests();

        bool passed = priorityPassed && mappingPassed && transitionPassed && iconFilePassed;
        Console.WriteLine(
            "トレイアイコンの状態表示（優先順位・リソース対応・遷移・icoの格納形式）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 8通りすべての組み合わせを期待値表と突き合わせる。
    /// 優先順位は 校正中 &gt; APIエラー &gt; 上限到達 &gt; 通常。
    /// </summary>
    private static bool RunPriorityTests()
    {
        (bool Proofreading, bool ApiError, bool LimitReached, TrayIconState Expected)[] cases =
        [
            (false, false, false, TrayIconState.Normal),
            (false, false, true, TrayIconState.LimitReached),
            (false, true, false, TrayIconState.ApiError),
            (false, true, true, TrayIconState.ApiError),
            (true, false, false, TrayIconState.Proofreading),
            (true, false, true, TrayIconState.Proofreading),
            (true, true, false, TrayIconState.Proofreading),
            (true, true, true, TrayIconState.Proofreading),
        ];

        bool passed = true;
        foreach (var (proofreading, apiError, limitReached, expected) in cases)
        {
            TrayIconState actual = TrayIconStateResolver.Resolve(proofreading, apiError, limitReached);
            if (actual == expected) continue;

            passed = false;
            Console.WriteLine(
                $"    NG 校正中={proofreading} エラー={apiError} 上限={limitReached}: " +
                $"期待 {expected} / 実際 {actual}");
        }

        Console.WriteLine("  優先順位（8通りの組み合わせ）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static bool RunMappingTests()
    {
        TrayIconState[] states = Enum.GetValues<TrayIconState>();

        // 4状態が別々のリソースを指すこと。取り違えると「状態が変わっても絵が変わらない」になる。
        string[] paths = [.. states.Select(TrayIconStateResolver.ResourcePath)];
        bool distinctPaths = paths.Distinct(StringComparer.Ordinal).Count() == states.Length;
        bool wellFormedPaths = paths.All(p =>
            p.StartsWith("Assets/", StringComparison.Ordinal) &&
            p.EndsWith(".ico", StringComparison.Ordinal));

        // 通常だけツールチップの接尾辞を持たない。他は互いに異なる非空の文言。
        bool normalHasNoSuffix = TrayIconStateResolver.TooltipSuffix(TrayIconState.Normal) is null;
        string[] suffixes =
        [
            .. states
                .Where(s => s != TrayIconState.Normal)
                .Select(TrayIconStateResolver.TooltipSuffix)
                .OfType<string>(),
        ];
        bool suffixesPresent =
            suffixes.Length == states.Length - 1 &&
            suffixes.All(s => !string.IsNullOrWhiteSpace(s)) &&
            suffixes.Distinct(StringComparer.Ordinal).Count() == suffixes.Length;

        bool passed = distinctPaths && wellFormedPaths && normalHasNoSuffix && suffixesPresent;
        Console.WriteLine(
            "  リソース対応（4状態が別ファイル・通常のみ接尾辞なし）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// <c>MainWindow.UpdateTrayIconState</c> が実際に辿る遷移を、同じ入力の組み立て方で再現する。
    /// とくに「上限到達中でも手動校正は実行でき、その間は校正中を出し、終わったら上限到達へ戻る」
    /// と「API失敗で残り続けたエラー表示が、次の成功で消える」を確認する。
    /// </summary>
    private static bool RunTransitionTests()
    {
        bool proofreadingRunInProgress = false;
        bool alternativeInProgress = false;
        bool apiErrorSticky = false;
        bool limitReached = false;

        TrayIconState Current() => TrayIconStateResolver.Resolve(
            proofreading: proofreadingRunInProgress || alternativeInProgress,
            apiError: apiErrorSticky,
            limitReached: limitReached);

        bool startsNormal = Current() == TrayIconState.Normal;

        // 自動校正が走る → 校正中
        proofreadingRunInProgress = true;
        bool showsProofreading = Current() == TrayIconState.Proofreading;

        // 失敗して終わる → APIエラーが残る
        apiErrorSticky = true;
        proofreadingRunInProgress = false;
        bool showsErrorAfterFailure = Current() == TrayIconState.ApiError;

        // 次の呼び出しが成功する → エラー解除で通常へ戻る
        proofreadingRunInProgress = true;
        bool proofreadingWinsOverError = Current() == TrayIconState.Proofreading;
        apiErrorSticky = false;
        proofreadingRunInProgress = false;
        bool clearedAfterSuccess = Current() == TrayIconState.Normal;

        // 当月累計が上限へ達する（RefreshUsageDisplay 経由）
        limitReached = true;
        bool showsLimit = Current() == TrayIconState.LimitReached;

        // 上限到達中でも手動校正・別案生成は実行できる。その間は校正中を出す。
        alternativeInProgress = true;
        bool proofreadingWinsOverLimit = Current() == TrayIconState.Proofreading;

        // 終われば上限到達へ戻る（一時的な状態が消えても、残る条件を取りこぼさない）
        alternativeInProgress = false;
        bool returnsToLimit = Current() == TrayIconState.LimitReached;

        // 月が変わって上限が解除される
        limitReached = false;
        bool backToNormal = Current() == TrayIconState.Normal;

        bool passed = startsNormal && showsProofreading && showsErrorAfterFailure &&
            proofreadingWinsOverError && clearedAfterSuccess && showsLimit &&
            proofreadingWinsOverLimit && returnsToLimit && backToNormal;

        Console.WriteLine(
            "  遷移（校正中→エラー継続→成功で解除→上限到達→上限中の手動実行→復帰）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }

    /// <summary>
    /// 状態アイコンの .ico が <c>app.ico</c> と同じ構成で、**256px 以外が DIB** であることを確認する。
    /// <c>System.Drawing.Icon</c>（= NotifyIcon）は PNG 圧縮エントリを展開できないため、
    /// ここが崩れるとトレイのアイコンが黙って既定のアイコンへフォールバックする。
    /// アイコンはビルド成果物ではなくソースツリーの一部なので、ソースの場所を基準に探す。
    /// </summary>
    private static bool RunIconFileTests([CallerFilePath] string sourcePath = "")
    {
        string assets = Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(sourcePath)) ?? ".", "Assets");
        if (!Directory.Exists(assets))
        {
            // ソースツリーの外から実行された場合（配布物など）。判定材料が無いので落とさない。
            Console.WriteLine("  icoの格納形式: SKIP（Assets が見つかりません: " + assets + "）");
            return true;
        }

        int[] expectedSizes = ReadSizes(Path.Combine(assets, "app.ico"));
        bool passed = true;

        foreach (TrayIconState state in Enum.GetValues<TrayIconState>())
        {
            string file = Path.Combine(
                assets,
                Path.GetFileName(TrayIconStateResolver.ResourcePath(state)));
            if (!File.Exists(file))
            {
                passed = false;
                Console.WriteLine($"    NG {state}: {file} がありません");
                continue;
            }

            byte[] data = File.ReadAllBytes(file);
            foreach ((int size, bool isPng) in ReadEntries(data))
            {
                if (size != 256 && isPng)
                {
                    passed = false;
                    Console.WriteLine(
                        $"    NG {state}: {size}px が PNG で格納されています" +
                        "（System.Drawing.Icon が展開できません）");
                }

                // 256px は PNG であること。app.ico と構成を揃える目的に加えて、
                // ここが常に false だとこの検査全体が素通りになるため、判別が効いていることも確かめる。
                if (size == 256 && !isPng)
                {
                    passed = false;
                    Console.WriteLine($"    NG {state}: 256px が PNG で格納されていません");
                }
            }

            int[] sizes = [.. ReadEntries(data).Select(e => e.Size)];
            if (!sizes.SequenceEqual(expectedSizes))
            {
                passed = false;
                Console.WriteLine(
                    $"    NG {state}: サイズ構成が app.ico と違います " +
                    $"[{string.Join(",", sizes)}]");
            }
        }

        Console.WriteLine(
            "  icoの格納形式（4ファイルが同一構成・256px以外はDIB）: " + (passed ? "PASS" : "FAIL"));
        return passed;
    }

    private static int[] ReadSizes(string path)
        => [.. ReadEntries(File.ReadAllBytes(path)).Select(e => e.Size)];

    /// <summary>ICO のディレクトリを読み、各エントリの (サイズ, PNGか) を返す。</summary>
    private static IEnumerable<(int Size, bool IsPng)> ReadEntries(byte[] data)
    {
        int count = BitConverter.ToUInt16(data, 4);
        for (int i = 0; i < count; i++)
        {
            int entry = 6 + i * 16;
            int width = data[entry];
            int offset = BitConverter.ToInt32(data, entry + 12);
            bool isPng =
                offset + 8 <= data.Length &&
                data[offset] == 0x89 && data[offset + 1] == (byte)'P' &&
                data[offset + 2] == (byte)'N' && data[offset + 3] == (byte)'G';
            yield return (width == 0 ? 256 : width, isPng);
        }
    }
}
