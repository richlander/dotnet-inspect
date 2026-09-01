using System.Collections.Concurrent;
using DotnetInspector.Core;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Proves <see cref="CacheMaintenanceProgress"/> never reports a torn byte/directory
/// pair: every deletion contributes exactly <c>BytesPerDeletion</c> bytes and one
/// directory as a single unit, so any snapshot's byte count must be an exact multiple
/// of its directory count. See docs/design/corecache-maintenance-lifecycle.md's
/// "Maintenance progress accounting" section and
/// docs/models/corecache-maintenance-progress/ for the modeled defect this guards
/// against.
/// </summary>
public class CacheMaintenanceProgressTests
{
    private const long BytesPerDeletion = 4096;
    private const int IterationsPerReader = 50_000;
    private const int WriterCount = 8;

    [Fact]
    public void Snapshot_ConcurrentWithRecordDeletion_NeverObservesTornAccounting()
    {
        AssertNoTornAccounting(progress => progress.Snapshot());
    }

    [Fact]
    public void TakeSnapshot_ConcurrentWithRecordDeletion_NeverObservesTornAccounting()
    {
        AssertNoTornAccounting(progress => progress.TakeSnapshot());
    }

    private static void AssertNoTornAccounting(
        Func<CacheMaintenanceProgress, CacheMaintenanceResult> read)
    {
        var progress = new CacheMaintenanceProgress();
        using var cts = new CancellationTokenSource();
        using var writersStarted = new CountdownEvent(WriterCount);
        var violations = new ConcurrentBag<CacheMaintenanceResult>();

        Task[] writers = Enumerable.Range(0, WriterCount)
            .Select(_ => Task.Run(() =>
            {
                bool signaled = false;
                while (!cts.IsCancellationRequested)
                {
                    progress.RecordDeletion(BytesPerDeletion);
                    if (!signaled)
                    {
                        signaled = true;
                        writersStarted.Signal();
                    }
                }
            }))
            .ToArray();

        // Wait for every writer to have recorded at least once so the reader
        // loop below overlaps genuine concurrent writes from the start,
        // rather than spending early iterations reading the trivially
        // consistent (0, 0) initial state.
        writersStarted.Wait();

        for (int i = 0; i < IterationsPerReader; i++)
        {
            CacheMaintenanceResult snapshot = read(progress);
            if (snapshot.BytesFreed != BytesPerDeletion * snapshot.DirectoriesDeleted)
                violations.Add(snapshot);
        }

        cts.Cancel();
        Task.WaitAll(writers);

        Assert.Empty(violations);
    }
}
