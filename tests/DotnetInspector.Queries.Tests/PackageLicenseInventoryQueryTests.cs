using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class PackageLicenseInventoryQueryTests
{
    [Theory]
    [InlineData(null, null, PackageLicenseIdentityKind.None, "none")]
    [InlineData(
        "expression",
        "Apache-2.0",
        PackageLicenseIdentityKind.Expression,
        "Apache-2.0")]
    [InlineData(
        "file",
        "legal/OSMFEULA.TXT",
        PackageLicenseIdentityKind.RecognizedFile,
        "OSMF")]
    [InlineData(
        "file",
        "LICENSE.txt",
        PackageLicenseIdentityKind.Unknown,
        "unknown")]
    [InlineData(
        "url",
        "https://example.test/license",
        PackageLicenseIdentityKind.Unknown,
        "unknown")]
    public void IdentityUsesOnlyTheNuspecDeclaration(
        string? kind,
        string? value,
        PackageLicenseIdentityKind expectedKind,
        string expectedValue)
    {
        PackageLicenseDeclaration? declaration = kind switch
        {
            "expression" => new(
                PackageLicenseDeclarationKind.Expression,
                value!),
            "file" => new(
                PackageLicenseDeclarationKind.File,
                value!),
            "url" => new(
                PackageLicenseDeclarationKind.Url,
                value!),
            null => null,
            _ => throw new InvalidOperationException(),
        };

        PackageLicenseIdentity identity =
            PackageLicenseIdentityQuery.Execute(declaration);

        Assert.Equal(expectedKind, identity.Kind);
        Assert.Equal(expectedValue, identity.Value);
    }

    [Fact]
    public async Task InventoryProducesOneSortedRowPerCoordinate()
    {
        PackageSourceCoordinate apache =
            PackageSourceCoordinate.Create("Example.Apache", "2.0.0");
        PackageSourceCoordinate none =
            PackageSourceCoordinate.Create("Example.None", "1.0.0");
        PackageSourceCoordinate unavailable =
            PackageSourceCoordinate.Create("Example.Unavailable", "3.0.0");
        var source = new FakeManifestSource(
            new Dictionary<PackageSourceCoordinate, string>
            {
                [apache] = Nuspec(
                    apache,
                    "<license type=\"expression\">Apache-2.0</license>"),
                [none] = Nuspec(none, license: null),
            },
            unavailable);
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "The inventory query test does not consult a source client."));

        PackageLicenseInventoryResult result =
            await PackageLicenseInventoryQuery.ExecuteAsync(
                [
                    unavailable,
                    none,
                    apache,
                    apache,
                ],
                Execution(source, lease),
                TestContext.Current.CancellationToken);

        Assert.Equal(PackageLicenseInventoryCompletion.Partial, result.Completion);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(apache, item.Coordinate);
                Assert.Equal("Apache-2.0", item.License?.Value);
                Assert.Null(item.Failure);
            },
            item =>
            {
                Assert.Equal(none, item.Coordinate);
                Assert.Equal("none", item.License?.Value);
                Assert.Null(item.Failure);
            },
            item =>
            {
                Assert.Equal(unavailable, item.Coordinate);
                Assert.Null(item.License);
                Assert.Equal(
                    PackageLicenseInventoryFailureReason
                        .ManifestAcquisitionFailed,
                    item.Failure?.Reason);
            });
    }

    [Fact]
    public async Task InventoryUsesEightIndependentConcurrentOperations()
    {
        PackageSourceCoordinate[] coordinates =
        [
            .. Enumerable.Range(0, 17).Select(index =>
                PackageSourceCoordinate.Create(
                    $"Example.Package{index:D2}",
                    "1.0.0")),
        ];
        var source = new ConcurrentManifestSource();
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "The inventory query test does not consult a source client."));

        PackageLicenseInventoryResult result =
            await PackageLicenseInventoryQuery.ExecuteAsync(
                coordinates,
                Execution(source, lease),
                TestContext.Current.CancellationToken);

        Assert.Equal(PackageLicenseInventoryCompletion.Complete, result.Completion);
        Assert.Equal(8, source.MaximumConcurrency);
        Assert.Equal(coordinates.Length, source.Operations.Count);
        Assert.Equal(
            coordinates.Length,
            source.Operations.Distinct().Count());
        foreach (PackageSourceOperationLease operation in source.Operations)
        {
            Assert.Throws<ObjectDisposedException>(
                operation.ThrowIfExpired);
        }
    }

    [Fact]
    public async Task InventoryLaterBatchesUseTheSharedDeadline()
    {
        PackageSourceCoordinate[] coordinates =
        [
            .. Enumerable.Range(0, 9).Select(index =>
                PackageSourceCoordinate.Create(
                    $"Example.Package{index:D2}",
                    "1.0.0")),
        ];
        var source = new DelayedManifestSource(
            TimeSpan.FromMilliseconds(250));
        var issuedDurations = new List<TimeSpan>();
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "The inventory query test does not consult a source client."));
        var execution = new PackageLicenseInventoryExecution(
            source,
            TimeSpan.FromSeconds(2),
            (maximumDuration, cancellationToken) =>
            {
                issuedDurations.Add(maximumDuration);
                return lease.IssueOperationLease(
                    cancellationToken,
                    maximumDuration,
                    maximumDuration);
            });

        PackageLicenseInventoryResult result =
            await PackageLicenseInventoryQuery.ExecuteAsync(
                coordinates,
                execution,
                TestContext.Current.CancellationToken);

        Assert.Equal(PackageLicenseInventoryCompletion.Complete, result.Completion);
        Assert.Equal(coordinates.Length, issuedDurations.Count);
        Assert.True(
            issuedDurations[8] < TimeSpan.FromMilliseconds(1900),
            $"Expected the second batch to inherit elapsed inventory time; actual timeout was {issuedDurations[8]}.");
    }

    [Fact]
    public async Task InventoryDoesNotStartAnotherBatchAfterDeadline()
    {
        PackageSourceCoordinate[] coordinates =
        [
            .. Enumerable.Range(0, 9).Select(index =>
                PackageSourceCoordinate.Create(
                    $"Example.Package{index:D2}",
                    "1.0.0")),
        ];
        var source = new ExpiringManifestSource(
            TimeSpan.FromMilliseconds(100));
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "The inventory query test does not consult a source client."));

        PackageLicenseInventoryResult result =
            await PackageLicenseInventoryQuery.ExecuteAsync(
                coordinates,
                Execution(
                    source,
                    lease,
                    TimeSpan.FromMilliseconds(25)),
                TestContext.Current.CancellationToken);

        Assert.Equal(8, source.CallCount);
        Assert.Equal(PackageLicenseInventoryCompletion.Partial, result.Completion);
        Assert.All(
            result.Items,
            item => Assert.Equal(
                PackageLicenseInventoryFailureReason
                    .ManifestAcquisitionIncomplete,
                item.Failure?.Reason));
    }

    [Fact]
    public async Task InventoryCallerCancellationDoesNotPublishOutcome()
    {
        PackageSourceCoordinate[] coordinates =
        [
            .. Enumerable.Range(0, 8).Select(index =>
                PackageSourceCoordinate.Create(
                    $"Example.Package{index:D2}",
                    "1.0.0")),
        ];
        var source = new CancelableManifestSource();
        await using PackageSourceSettlementLease lease =
            PackageSourceSettlementService.IssueLease(
                _ => throw new InvalidOperationException(
                    "The inventory query test does not consult a source client."));
        using var cancellation = CancellationTokenSource
            .CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        Task<PackageLicenseInventoryResult> pending =
            PackageLicenseInventoryQuery.ExecuteAsync(
                coordinates,
                Execution(source, lease),
                cancellation.Token);
        await source.Started.Task.WaitAsync(
            TestContext.Current.CancellationToken);
        cancellation.Cancel();

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(coordinates.Length, source.Operations.Count);
        foreach (PackageSourceOperationLease operation in source.Operations)
        {
            Assert.Throws<ObjectDisposedException>(
                operation.ThrowIfExpired);
        }
    }

    private static PackageLicenseInventoryExecution Execution(
        IPackageLicenseManifestSource source,
        PackageSourceSettlementLease lease,
        TimeSpan? maximumDuration = null)
    {
        TimeSpan duration = maximumDuration ?? TimeSpan.FromSeconds(5);
        return new(
            source,
            duration,
            (remaining, cancellationToken) =>
                lease.IssueOperationLease(
                    cancellationToken,
                    remaining,
                    remaining));
    }

    private static string Nuspec(
        PackageSourceCoordinate coordinate,
        string? license) =>
        $"""
        <?xml version="1.0"?>
        <package>
          <metadata>
            <id>{coordinate.PackageId}</id>
            <version>{coordinate.Version}</version>
            <authors>Tests</authors>
            <description>License inventory fixture.</description>
            {license}
          </metadata>
        </package>
        """;

    private sealed class FakeManifestSource(
        IReadOnlyDictionary<PackageSourceCoordinate, string> manifests,
        PackageSourceCoordinate unavailable) : IPackageLicenseManifestSource
    {
        public Task<PackageLicenseManifestResult> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageSourceOperationLease operation)
        {
            operation.ThrowIfExpired();
            return Task.FromResult(
                coordinate == unavailable
                    ? (PackageLicenseManifestResult)
                        new PackageLicenseManifestResult.Unavailable(
                            PackageLicenseManifestFailureReason
                                .AcquisitionFailed)
                    : new PackageLicenseManifestResult.Acquired(
                        Encoding.UTF8.GetBytes(manifests[coordinate])));
        }
    }

    private sealed class ConcurrentManifestSource :
        IPackageLicenseManifestSource
    {
        private int _active;
        private int _maximumConcurrency;

        public List<PackageSourceOperationLease> Operations { get; } = [];

        public int MaximumConcurrency => Volatile.Read(
            ref _maximumConcurrency);

        public async Task<PackageLicenseManifestResult> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageSourceOperationLease operation)
        {
            lock (Operations)
                Operations.Add(operation);
            int active = Interlocked.Increment(ref _active);
            int observed;
            while (active > (observed = Volatile.Read(
                       ref _maximumConcurrency)))
            {
                if (Interlocked.CompareExchange(
                        ref _maximumConcurrency,
                        active,
                        observed) == observed)
                {
                    break;
                }
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    operation.OperationCancellationToken);
                return new PackageLicenseManifestResult.Acquired(
                    Encoding.UTF8.GetBytes(
                        Nuspec(coordinate, license: null)));
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class DelayedManifestSource(TimeSpan delay) :
        IPackageLicenseManifestSource
    {
        public async Task<PackageLicenseManifestResult> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageSourceOperationLease operation)
        {
            await Task.Delay(
                delay,
                operation.OperationCancellationToken);
            return new PackageLicenseManifestResult.Acquired(
                Encoding.UTF8.GetBytes(
                    Nuspec(coordinate, license: null)));
        }
    }

    private sealed class ExpiringManifestSource(TimeSpan delay) :
        IPackageLicenseManifestSource
    {
        public int CallCount;

        public async Task<PackageLicenseManifestResult> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageSourceOperationLease operation)
        {
            Interlocked.Increment(ref CallCount);
            await Task.Delay(
                delay,
                TestContext.Current.CancellationToken);
            try
            {
                operation.ThrowIfExpired();
                throw new InvalidOperationException(
                    "The operation was expected to expire.");
            }
            catch (NuGetOperationTimeoutException)
            {
                return new PackageLicenseManifestResult.Unavailable(
                    PackageLicenseManifestFailureReason
                        .AcquisitionIncomplete);
            }
        }
    }

    private sealed class CancelableManifestSource :
        IPackageLicenseManifestSource
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<PackageSourceOperationLease> Operations { get; } = [];

        public async Task<PackageLicenseManifestResult> AcquireAsync(
            PackageSourceCoordinate coordinate,
            PackageSourceOperationLease operation)
        {
            lock (Operations)
                Operations.Add(operation);
            Started.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                operation.CancellationToken);
            throw new InvalidOperationException(
                "Caller cancellation must stop manifest acquisition.");
        }
    }
}
