using System.Diagnostics;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

// PR-fast: one pinned System.Text.Json archive plus small boundary fixtures.
public sealed class InMemoryPackageAdmissionTests(ITestOutputHelper output)
{
    static byte[] RealArchive() => File.ReadAllBytes(Path.Combine(
        AppContext.BaseDirectory, "RealAssets", "PackageAdmission",
        "System.Text.Json.10.0.0.nupkg"));

    [Fact]
    public async Task RealPackage_CachedAcquisitionReusesTheOwnedStructuralIndex()
    {
        PackageSource source = PackageSource.NuGetOrg;
        string producer = NuGetCache.GetSourceKey(source.Url);
        var store = new InMemoryPackageStore();
        using var archive = new MemoryStream(RealArchive());
        await store.CommitAsync("System.Text.Json", "10.0.0", producer, archive,
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new NoNetwork());
        var coordinate = new ResolvedPackageCoordinate(
            "System.Text.Json", "10.0.0", "net10.0", null, [source], false);

        long start = Stopwatch.GetTimestamp();
        var first = Assert.IsType<PackagePayloadResult.Acquired>(
            await PackagePayloadAcquisition.AcquireAsync(client, coordinate, store,
                cancellationToken: TestContext.Current.CancellationToken));
        TimeSpan cold = Stopwatch.GetElapsedTime(start);
        var content = Assert.IsType<InMemoryPackageContent>(first.Payload.Content);
        PackageArchiveValidation admitted = content.ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken);
        Assert.IsType<PackageArchiveValidation.Valid>(admitted);

        start = Stopwatch.GetTimestamp();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var second = Assert.IsType<PackagePayloadResult.Acquired>(
            await PackagePayloadAcquisition.AcquireAsync(client, coordinate, store,
                cancellationToken: TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        TimeSpan warm = Stopwatch.GetElapsedTime(start);
        Assert.Equal(PackagePayloadOrigin.Cache, second.Payload.Origin);
        var cached = Assert.IsType<InMemoryPackageContent>(second.Payload.Content);
        Assert.Same(content.GenerationIdentity, cached.GenerationIdentity);
        Assert.Same(admitted, cached.ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
        // Cache acquisition retains the owned archive and structural index. It
        // must not allocate a decompression buffer or rebuild ZipArchive state.
        Assert.True(allocated < 32_768, $"Warm admission allocated {allocated} bytes.");
        Assert.Contains("lib/net10.0/System.Text.Json.dll", cached.EnumerateEntries());
        output.WriteLine($"First admission: {cold.TotalMilliseconds:F2} ms; "
            + $"cached admission: {warm.TotalMilliseconds:F2} ms; {allocated} bytes.");
    }

    [Fact]
    public void CacheViews_ReuseTheStructuralReceiptButIndependentArchivesDoNot()
    {
        byte[] archive = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        var content = new InMemoryPackageContent(archive, false, "source");
        PackageArchiveValidation first = content.ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken);
        Assert.IsType<PackageArchiveValidation.Valid>(first);
        Assert.Same(first, content.AsCacheHit().ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
        Assert.Same(first, content.AsCacheHitForProducer("alias").ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
        var independent = new InMemoryPackageContent(archive, false, "source");
        Assert.NotSame(first, independent.ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("archive")]
    [InlineData("expanded")]
    [InlineData("entries")]
    [InlineData("directories")]
    public async Task StricterPolicy_UsesTheRecordedStructuralFacts(string dimension)
    {
        var content = new InMemoryPackageContent(RealArchive(), false, "source");
        PackagePayloadLimits limits = PackagePayloadLimits.Default;
        Assert.Equal(PackageContentAdmission.Outcome.Admissible,
            await PackageContentAdmission.EvaluateAsync(content, limits,
                TestContext.Current.CancellationToken));
        PackagePayloadLimits strict = dimension switch
        {
            "archive" => limits with { MaxArchiveBytes = 1 },
            "expanded" => limits with { MaxExpandedBytes = 1 },
            "entries" => limits with { MaxEntryCount = 1 },
            "directories" => limits with { MaxUniqueDirectories = 1 },
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(PackageContentAdmission.Outcome.LimitsExceeded,
            await PackageContentAdmission.EvaluateAsync(content.AsCacheHit(), strict,
                TestContext.Current.CancellationToken));
        Assert.Equal(PackageContentAdmission.Outcome.Admissible,
            await PackageContentAdmission.EvaluateAsync(content.AsCacheHit(), limits,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Rejection_DoesNotPreventLaterValidPolicy()
    {
        var content = new InMemoryPackageContent(RealArchive(), false, "source");
        Assert.Equal(PackageContentAdmission.Outcome.LimitsExceeded,
            await PackageContentAdmission.EvaluateAsync(content,
                PackagePayloadLimits.Default with { MaxExpandedBytes = 1 },
                TestContext.Current.CancellationToken));
        Assert.Equal(PackageContentAdmission.Outcome.Admissible,
            await PackageContentAdmission.EvaluateAsync(content,
                PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LooserPolicy_ReusesTheStructuralReceipt()
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create("lib/net10.0/Sample.dll"), false, "source");
        PackageArchiveValidation first = content.ValidateArchive(
            PackagePayloadLimits.Default with { MaxEntryCount = 100 },
            TestContext.Current.CancellationToken);
        Assert.IsType<PackageArchiveValidation.Valid>(first);
        Assert.Same(first, content.ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_IsObservedWithOrWithoutReusableEvidence(bool warm)
    {
        var content = new InMemoryPackageContent(
            TestPackageArchive.Create("lib/net10.0/Sample.dll"), false, "source");
        if (warm) content.ValidateArchive(
            PackagePayloadLimits.Default, TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PackageContentAdmission.EvaluateAsync(content.AsCacheHit(),
                PackagePayloadLimits.Default, cancellation.Token));
        Assert.Equal(PackageContentAdmission.Outcome.Admissible,
            await PackageContentAdmission.EvaluateAsync(content,
                PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CorruptArchive_RemainsRejectedAcrossCacheViews()
    {
        var content = new InMemoryPackageContent([1, 2, 3], false, "source");
        for (int index = 0; index < 2; index++)
            Assert.Equal(PackageContentAdmission.Outcome.LimitsExceeded,
                await PackageContentAdmission.EvaluateAsync(content.AsCacheHit(),
                    PackagePayloadLimits.Default, TestContext.Current.CancellationToken));
    }

    sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Cached acquisition attempted network work.");
    }
}
