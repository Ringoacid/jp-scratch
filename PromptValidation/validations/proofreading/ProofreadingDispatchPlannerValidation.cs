using JpScratch.Proofreading;

namespace JpScratch.PromptValidation;

internal static class ProofreadingDispatchPlannerValidation
{
    internal static bool RunSelfTests()
    {
        ProofreadingRequest[] requests =
        [
            CreateRequest(0, 0),
            CreateRequest(0, 1),
            CreateRequest(1, 0),
            CreateRequest(2, 0),
        ];

        HashSet<(int ParagraphIndex, int PartIndex)> completed =
        [
            (0, 0),
            (1, 0),
            (2, 0),
        ];
        HashSet<(int ParagraphIndex, int PartIndex)> middleFailure =
            ProofreadingDispatchPlanner.GetUnsentParts(requests, completed);
        bool successfulRequestsStaySent =
            middleFailure.SetEquals([(0, 1)]);

        ProofreadingRequest[] mixedRequests =
        [
            CreateRequest(0, 0),
            CreateRequest(1, 0),
            CreateRequest(2, 0),
            CreateRequest(3, 0),
        ];
        HashSet<(int ParagraphIndex, int PartIndex)> mixedCompleted =
        [
            (0, 0),
            (1, 0),
            (3, 0),
        ];
        HashSet<(int ParagraphIndex, int PartIndex)> mixedResult =
            ProofreadingDispatchPlanner.GetUnsentParts(mixedRequests, mixedCompleted);
        bool mixedSuccessAndFailureAreSeparated =
            mixedResult.SetEquals([(2, 0)]) &&
            !mixedResult.Contains((0, 0)) &&
            !mixedResult.Contains((1, 0)) &&
            !mixedResult.Contains((3, 0));

        HashSet<(int ParagraphIndex, int PartIndex)> partialCompletion = [(0, 0)];
        HashSet<(int ParagraphIndex, int PartIndex)> failedAndUnsent =
            ProofreadingDispatchPlanner.GetUnsentParts(requests, partialCompletion);
        bool failedAndUnsentAreComplete =
            failedAndUnsent.SetEquals([(0, 1), (1, 0), (2, 0)]);

        IReadOnlyList<IReadOnlyList<ProofreadingRequest>> batches =
            ProofreadingDispatchPlanner.CreateBatches(requests, 2);
        bool splitPartsAreSequential =
            batches.All(batch => batch
                .Select(request => request.ParagraphIndex)
                .Distinct()
                .Count() == batch.Count) &&
            batches.Count == 2;

        IReadOnlyList<IReadOnlyList<ProofreadingRequest>> sequentialBatches =
            ProofreadingDispatchPlanner.CreateBatches(requests, 1);
        bool oneIsFullySequential =
            sequentialBatches.Count == requests.Length &&
            sequentialBatches.SelectMany(batch => batch).SequenceEqual(requests);

        bool clampPasses =
            ProofreadingDispatchPlanner.ClampParallelism(-1) == 1 &&
            ProofreadingDispatchPlanner.ClampParallelism(0) == 1 &&
            ProofreadingDispatchPlanner.ClampParallelism(3) == 3 &&
            ProofreadingDispatchPlanner.ClampParallelism(11) == 10 &&
            ProofreadingDispatchPlanner.ClampParallelism(100) == 10;

        bool passed = successfulRequestsStaySent &&
                      mixedSuccessAndFailureAreSeparated &&
                      failedAndUnsentAreComplete &&
                      splitPartsAreSequential &&
                      oneIsFullySequential &&
                      clampPasses;
        Console.WriteLine($"校正送信計画（未送信集合・段落並列・クランプ）: {(passed ? "PASS" : "FAIL")}");
        return passed;
    }

    private static ProofreadingRequest CreateRequest(int paragraphIndex, int partIndex)
    {
        string text = $"paragraph-{paragraphIndex}-part-{partIndex}";
        return new ProofreadingRequest(
            0,
            text.Length,
            text,
            null,
            null,
            text,
            paragraphIndex,
            partIndex,
            2);
    }
}
