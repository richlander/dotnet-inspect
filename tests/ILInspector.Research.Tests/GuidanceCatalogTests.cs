using ILInspector.Analysis;
using ILInspector.Decompiler.Annotations;

namespace ILInspector.Research.Tests;

/// <summary>
/// Catalog gate for docs/design/notable-findings.md#guidance-catalog: every
/// descriptor the preset admits has an entry, the ordinary gates read the
/// catalog offline, and one slow network-classified test resolves each link.
/// </summary>
public class GuidanceCatalogTests
{
    static readonly string[] AllowedHosts =
    [
        "learn.microsoft.com",
        "devblogs.microsoft.com",
        "github.com",
    ];

    [Fact]
    public void EveryAdmittedDescriptor_HasAnEntry()
    {
        foreach (string id in NotableFindingPreset.HiddenDescriptorIds)
            Assert.NotNull(GuidanceCatalog.Find(id));
        foreach (string id in NotableFindingPreset.ContractDescriptorIds)
            Assert.NotNull(GuidanceCatalog.Find(id));
        foreach (string id in NotableFindingPreset.AmplifiedDescriptorIds)
            Assert.NotNull(GuidanceCatalog.Find(id, GuidanceRefinement.InLoop));
        foreach (string id in NotableFindingPreset.ExpectationDescriptorIds)
            Assert.NotNull(GuidanceCatalog.Find(id));
    }

    [Fact]
    public void Entries_AreWellFormedAndUnique()
    {
        Assert.NotEmpty(GuidanceCatalog.Entries);
        Assert.Equal(
            GuidanceCatalog.Entries.Length,
            GuidanceCatalog.Entries
                .Select(entry => (entry.Key, entry.Refinement))
                .Distinct()
                .Count());

        foreach (var entry in GuidanceCatalog.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Key));
            Assert.False(string.IsNullOrWhiteSpace(entry.Advice));
            Assert.EndsWith(".", entry.Advice, StringComparison.Ordinal);
            Assert.NotEmpty(entry.Links);
            foreach (var link in entry.Links)
            {
                Assert.False(string.IsNullOrWhiteSpace(link.Title));
                var url = new Uri(link.Url, UriKind.Absolute);
                Assert.Equal(Uri.UriSchemeHttps, url.Scheme);
                Assert.Contains(url.Host, AllowedHosts);
            }
        }
    }

    [Fact]
    public void DescriptorKeys_AreDeclaredByTheRegistry()
    {
        var declared = ResearchFactRegistry.MemberCensusWithCallRelationships.DescriptorIds
            .ToHashSet(StringComparer.Ordinal);
        string[] ownKeys =
        [
            GuidanceCatalog.ExpectationStateMachineAsyncKey,
            GuidanceCatalog.LibraryDebugProfileKey,
            GuidanceCatalog.ThrowCountColumnKey,
        ];

        foreach (var entry in GuidanceCatalog.Entries)
        {
            if (ownKeys.Contains(entry.Key, StringComparer.Ordinal))
                Assert.DoesNotContain(entry.Key, declared);
            else
                Assert.Contains(entry.Key, declared);
        }
    }

    [Fact]
    public void Find_PrefersTheRefinement_AndFallsBackToTheBase()
    {
        var async = GuidanceCatalog.Find("alloc.statemachine", GuidanceRefinement.Async);
        var iterator = GuidanceCatalog.Find("alloc.statemachine", GuidanceRefinement.Iterator);
        var unknown = GuidanceCatalog.Find("alloc.statemachine");
        var boxInLoop = GuidanceCatalog.Find("alloc.box", GuidanceRefinement.InLoop);

        Assert.NotNull(async);
        Assert.Equal(GuidanceRefinement.Async, async.Refinement);
        Assert.NotNull(iterator);
        Assert.NotNull(unknown);
        Assert.Equal(iterator.Advice, unknown.Advice);
        Assert.NotNull(boxInLoop);
        Assert.Equal(GuidanceRefinement.None, boxInLoop.Refinement);
    }

    [Fact]
    public void VisibleAllocation_HasGuidanceOnlyInALoop()
    {
        Assert.Null(GuidanceCatalog.Find("alloc.new"));
        Assert.Null(GuidanceCatalog.Find("alloc.array"));
        Assert.NotNull(GuidanceCatalog.Find("alloc.new", GuidanceRefinement.InLoop));
        Assert.NotNull(GuidanceCatalog.Find("alloc.array", GuidanceRefinement.InLoop));
    }

    [Fact]
    public void UnknownKey_YieldsNoEntry()
    {
        Assert.Null(GuidanceCatalog.Find("cost.callee"));
        Assert.Null(GuidanceCatalog.Find("semantics.callee", GuidanceRefinement.InLoop));
    }

    [Fact]
    public void For_ReadsLoopContextFromTheFinding()
    {
        var straight = NotableFindingPresetTests.Allocation("alloc.array", AllocationKind.Array);
        var inLoop = NotableFindingPresetTests.Allocation(
            "alloc.array",
            AllocationKind.Array,
            pathContext: AllocationPathContext.LoopBody);
        var perIteration = new Annotation(
            new AnnotationDescriptor("unsafe.stackalloc", AnnotationCategory.Unsafety, "allocates on the stack"),
            SourceOffset: 0,
            Conditionality: AnnotationConditionality.PerIteration);

        Assert.Null(GuidanceCatalog.For(straight));
        Assert.Equal(GuidanceRefinement.InLoop, GuidanceCatalog.For(inLoop)?.Refinement);
        Assert.Equal(GuidanceRefinement.InLoop, GuidanceCatalog.For(perIteration)?.Refinement);
        Assert.Equal(GuidanceRefinement.None, GuidanceCatalog.RefinementOf(straight));
    }

    /// <summary>
    /// Resolves every catalog link against the live network. Excluded from
    /// every CI gate by <c>Network=Live</c>; run on demand with
    /// <c>dotnet run --project tests/ILInspector.Research.Tests -c Release -- --filter-trait "Network=Live"</c>.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Network", "Live")]
    public async Task EveryLink_Resolves()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("dotnet-inspect-guidance-link-check/1.0");
        var urls = GuidanceCatalog.Entries
            .SelectMany(entry => entry.Links)
            .Select(link => new UriBuilder(link.Url) { Fragment = string.Empty }.Uri)
            .Distinct()
            .ToArray();
        var failures = new List<string>();

        foreach (var url in urls)
        {
            using var response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                TestContext.Current.CancellationToken);
            if (!response.IsSuccessStatusCode)
                failures.Add($"{url}: {(int)response.StatusCode}");
        }

        Assert.Empty(failures);
    }
}
