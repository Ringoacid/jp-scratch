using JpScratch.Infrastructure;

namespace JpScratch.PromptValidation;

/// <summary>
/// <see cref="SingleInstance.ResolveNames"/>（環境変数によるMutex/イベント名の切り替え）の自己テスト。
/// 実際に Mutex/EventWaitHandle を作らず、名前導出の純粋関数だけを検査する。
/// 従来の固定名との完全一致は、実データ側インスタンスの二重起動防止を壊していないことの担保になるため
/// 特に重要視して検証する。
/// </summary>
internal static class SingleInstanceValidation
{
    private const string LegacyMutexName = @"Local\JpScratch.SingleInstance";
    private const string LegacyActivateEventName = @"Local\JpScratch.Activate";

    // Windows のカーネルオブジェクト名は約260文字までとされる。
    private const int MaxKernelObjectNameLength = 260;

    internal static bool RunSelfTests()
    {
        (string Mutex, string ActivateEvent) unset = SingleInstance.ResolveNames(null);
        (string Mutex, string ActivateEvent) empty = SingleInstance.ResolveNames("");
        (string Mutex, string ActivateEvent) whitespace = SingleInstance.ResolveNames("   ");

        bool unsetMatchesLegacyExactly =
            unset.Mutex == LegacyMutexName && unset.ActivateEvent == LegacyActivateEventName;
        bool emptyMatchesLegacyExactly =
            empty.Mutex == LegacyMutexName && empty.ActivateEvent == LegacyActivateEventName;
        bool whitespaceMatchesLegacyExactly =
            whitespace.Mutex == LegacyMutexName && whitespace.ActivateEvent == LegacyActivateEventName;

        string dirA = @"C:\Users\test\AppData\Roaming\JpScratchIsolatedA";
        string dirB = @"C:\Users\test\AppData\Roaming\JpScratchIsolatedB";

        (string Mutex, string ActivateEvent) namesA1 = SingleInstance.ResolveNames(dirA);
        (string Mutex, string ActivateEvent) namesA2 = SingleInstance.ResolveNames(dirA);
        (string Mutex, string ActivateEvent) namesB = SingleInstance.ResolveNames(dirB);

        bool sameDirectoryProducesSameNames = namesA1 == namesA2;
        bool differentDirectoryProducesDifferentNames =
            namesA1.Mutex != namesB.Mutex && namesA1.ActivateEvent != namesB.ActivateEvent;
        bool suffixedNamesDifferFromLegacy =
            namesA1.Mutex != LegacyMutexName && namesA1.ActivateEvent != LegacyActivateEventName;
        bool suffixedNamesStartWithLegacyPrefix =
            namesA1.Mutex.StartsWith(LegacyMutexName + ".", StringComparison.Ordinal) &&
            namesA1.ActivateEvent.StartsWith(LegacyActivateEventName + ".", StringComparison.Ordinal);

        // 末尾区切り文字の表記ゆれ（"...A" と "...A\"）は同じディレクトリとして同名になる。
        (string Mutex, string ActivateEvent) namesTrailingSlash = SingleInstance.ResolveNames(dirA + @"\");
        bool trailingSlashNormalizes = namesTrailingSlash == namesA1;

        // 大文字小文字の表記ゆれも同じディレクトリとして同名になる（Windows パスは大小無視）。
        (string Mutex, string ActivateEvent) namesLowercase = SingleInstance.ResolveNames(dirA.ToLowerInvariant());
        bool caseInsensitiveNormalizes = namesLowercase == namesA1;

        bool namesWithinKernelObjectLimit =
            namesA1.Mutex.Length < MaxKernelObjectNameLength &&
            namesA1.ActivateEvent.Length < MaxKernelObjectNameLength;

        // 非常に長いパスでも、サフィックスは固定長ハッシュなので名前長は膨らまない。
        string veryLongDir = @"C:\" + string.Concat(Enumerable.Repeat("very-long-segment\\", 20)) + "leaf";
        (string Mutex, string ActivateEvent) namesLongPath = SingleInstance.ResolveNames(veryLongDir);
        bool longPathStillWithinLimit =
            namesLongPath.Mutex.Length < MaxKernelObjectNameLength &&
            namesLongPath.ActivateEvent.Length < MaxKernelObjectNameLength;

        bool passed =
            unsetMatchesLegacyExactly && emptyMatchesLegacyExactly && whitespaceMatchesLegacyExactly &&
            sameDirectoryProducesSameNames && differentDirectoryProducesDifferentNames &&
            suffixedNamesDifferFromLegacy && suffixedNamesStartWithLegacyPrefix &&
            trailingSlashNormalizes && caseInsensitiveNormalizes &&
            namesWithinKernelObjectLimit && longPathStillWithinLimit;

        Console.WriteLine(
            "SingleInstance（環境変数による名前導出、未設定時は従来固定名と完全一致、決定的サフィックス）: " +
            (passed ? "PASS" : "FAIL"));
        return passed;
    }
}
