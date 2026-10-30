using ILInspector.Analysis;
using ILInspector.Analysis.UnsafeMemberFindingFixtures;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

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
        Assert.Equal(GuidanceRefinement.Iterator, iterator.Refinement);
        Assert.NotNull(unknown);
        Assert.Equal(GuidanceRefinement.None, unknown.Refinement);
        // The base entry must be true of both classifications, so it is neither
        // refinement's advice and links to both pages.
        Assert.NotEqual(iterator.Advice, unknown.Advice);
        Assert.NotEqual(async.Advice, unknown.Advice);
        Assert.Contains(unknown.Links, link => iterator.Links.Contains(link));
        Assert.Contains(unknown.Links, link => async.Links.Contains(link));
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
    /// The real producer-to-lookup path: a <c>stackalloc</c> inside a loop in a
    /// fixture Library reaches the loop-refined entry through the unsafety
    /// producer's own loop attribute, and the same operation on a straight-line
    /// path reaches the base entry.
    /// </summary>
    [Theory]
    [InlineData(StateMachineClaimKind.Iterator, GuidanceRefinement.Iterator)]
    [InlineData(StateMachineClaimKind.AsyncIterator, GuidanceRefinement.Iterator)]
    [InlineData(StateMachineClaimKind.ClassicAsync, GuidanceRefinement.Async)]
    [InlineData(null, GuidanceRefinement.None)]
    public void For_SelectsTheStateMachineRefinement_FromTheContext(
        StateMachineClaimKind? claim,
        GuidanceRefinement expected)
    {
        var stateMachine = NotableFindingPresetTests.Allocation("alloc.statemachine", AllocationKind.StateMachine);
        var context = new NotableFindingContext(StateMachine: claim);

        Assert.Equal(expected, GuidanceCatalog.RefinementOf(stateMachine, context));
        GuidanceEntry? entry = GuidanceCatalog.For(stateMachine, context);
        Assert.NotNull(entry);
        Assert.Equal(expected, entry.Refinement);
        Assert.Equal(GuidanceCatalog.Find("alloc.statemachine", expected), entry);
    }

    [Fact]
    public void Context_DoesNotRefineOtherDescriptors()
    {
        var box = NotableFindingPresetTests.Allocation("alloc.box", AllocationKind.Box);
        var context = new NotableFindingContext(StateMachine: StateMachineClaimKind.ClassicAsync);

        Assert.Equal(GuidanceRefinement.None, GuidanceCatalog.RefinementOf(box, context));
        Assert.Equal(GuidanceCatalog.For(box), GuidanceCatalog.For(box, context));
    }

    [Fact]
    public void StackallocInLoop_ReachesTheLoopRefinedEntry_ThroughTheProducer()
    {
        using var source = MetadataSource.Open(
            typeof(StackallocLoopFixtures).Assembly.Location);

        var inLoop = StackallocFact(source, nameof(StackallocLoopFixtures.SumInLoop));
        var once = StackallocFact(source, nameof(StackallocLoopFixtures.SumOnce));

        Assert.True(inLoop.Payload.InLoop);
        Assert.True(NotableFindingPreset.IsLoopMultiplied(inLoop));
        var refined = GuidanceCatalog.For(inLoop);
        Assert.NotNull(refined);
        Assert.Equal(GuidanceRefinement.InLoop, refined.Refinement);
        Assert.Equal("Move the allocation outside the loop.", refined.Advice);

        Assert.False(once.Payload.InLoop);
        Assert.False(NotableFindingPreset.IsLoopMultiplied(once));
        var baseEntry = GuidanceCatalog.For(once);
        Assert.NotNull(baseEntry);
        Assert.Equal(GuidanceRefinement.None, baseEntry.Refinement);
        Assert.Equal("Cap the length and assign to a span.", baseEntry.Advice);

        Assert.Equal(
            NotableClassification.Admit(NotableCriterion.Contract),
            NotableFindingPreset.Classify(inLoop));
    }

    static Annotation<UnsafetyOccurrence> StackallocFact(MetadataSource source, string method)
    {
        var facts = ResearchViews.CollectFacts(
            source,
            typeof(StackallocLoopFixtures).FullName!,
            method);
        var fact = Assert.Single(facts, fact => fact.Descriptor.Id == "unsafe.stackalloc");
        return Assert.IsType<Annotation<UnsafetyOccurrence>>(fact);
    }

    /// <summary>
    /// Resolves every catalog link against the live network. Excluded from
    /// every CI gate by <c>Network=Live</c> and skipped unless
    /// <c>DOTNET_INSPECT_TEST_LIVE_LINKS</c> opts in, so an unfiltered local
    /// run stays offline; run on demand with
    /// <c>DOTNET_INSPECT_TEST_LIVE_LINKS=1 dotnet run --project tests/ILInspector.Research.Tests -c Release -- --filter-trait "Network=Live"</c>.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    [Trait("Network", "Live")]
    public async Task EveryLink_Resolves()
    {
        Assert.SkipWhen(
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_INSPECT_TEST_LIVE_LINKS")),
            "Set DOTNET_INSPECT_TEST_LIVE_LINKS=1 to resolve the catalog links against the live network.");

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
