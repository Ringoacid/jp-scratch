namespace JpScratch.Proofreading;

/// <summary>
/// 校正リクエストを、段落ごとの逐次制約を保ったまま並列送信する計画ロジック。
/// </summary>
internal static class ProofreadingDispatchPlanner
{
    internal const int MinParallelism = 1;
    internal const int MaxParallelism = 10;
    internal const int DefaultParallelism = 3;

    internal static int ClampParallelism(int value)
        => Math.Clamp(value, MinParallelism, MaxParallelism);

    internal static IReadOnlyList<IReadOnlyList<ProofreadingRequest>> CreateBatches(
        IReadOnlyList<ProofreadingRequest> requests,
        int parallelism)
    {
        ArgumentNullException.ThrowIfNull(requests);

        int limit = ClampParallelism(parallelism);
        Dictionary<int, Queue<ProofreadingRequest>> queues = [];
        foreach (ProofreadingRequest request in requests)
        {
            if (!queues.TryGetValue(request.ParagraphIndex, out Queue<ProofreadingRequest>? queue))
            {
                queue = new Queue<ProofreadingRequest>();
                queues.Add(request.ParagraphIndex, queue);
            }

            queue.Enqueue(request);
        }

        List<IReadOnlyList<ProofreadingRequest>> batches = [];
        while (queues.Values.Any(queue => queue.Count > 0))
        {
            List<ProofreadingRequest> batch = [];
            foreach (Queue<ProofreadingRequest> queue in queues.Values)
            {
                if (batch.Count >= limit)
                    break;
                if (queue.Count > 0)
                    batch.Add(queue.Dequeue());
            }

            batches.Add(batch);
        }

        return batches;
    }

    internal static HashSet<(int ParagraphIndex, int PartIndex)> GetUnsentParts(
        IReadOnlyList<ProofreadingRequest> requests,
        IReadOnlySet<(int ParagraphIndex, int PartIndex)> completedParts)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(completedParts);

        return requests
            .Where(request => !completedParts.Contains(
                (request.ParagraphIndex, request.PartIndex)))
            .Select(request => (request.ParagraphIndex, request.PartIndex))
            .ToHashSet();
    }
}
