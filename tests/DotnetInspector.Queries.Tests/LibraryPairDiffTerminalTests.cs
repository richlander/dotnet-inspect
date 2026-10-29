using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Diff;
using DotnetInspector.Fixtures;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using Inspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// The single-Library API Diff terminal's Library API presentation over
/// host-opened pairs (diff-system.md#single-library-api-diff-terminal), and
/// the request's content selection.
/// </summary>
public sealed class LibraryPairDiffTerminalTests
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    [Fact]
    public async Task ExecuteLibraryPair_PathAndMemoryBackedVersionPair_ProducesEquivalentDetachedEnvelopes()
    {
        string beforePath = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string afterPath = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();

        InspectionEnvelope<DiffAnalysisDocument> pathBacked =
            await ExecutePathBacked(beforePath, afterPath, GenerousLimits);
        InspectionEnvelope<DiffAnalysisDocument> memoryBacked =
            await ExecuteMemoryBacked(beforePath, afterPath, GenerousLimits);

        Assert.Equal(pathBacked.Content.LibraryApi, memoryBacked.Content.LibraryApi);
        InspectionShare.NonProjectable pathShare =
            Assert.IsType<InspectionShare.NonProjectable>(pathBacked.Share);
        InspectionShare.NonProjectable memoryShare =
            Assert.IsType<InspectionShare.NonProjectable>(memoryBacked.Share);
        Assert.Equal(pathShare.Path, memoryShare.Path);
        Assert.Equal(pathShare.Reason.ToString(), memoryShare.Reason.ToString());
        Assert.Equal("comparison/endpoints", pathShare.Path);
        Assert.Contains("ordered", pathShare.Reason.ToString(), StringComparison.Ordinal);
        Assert.Contains("endpoint pair", pathShare.Reason.ToString(), StringComparison.Ordinal);
        Assert.Empty(pathBacked.Diagnostics);
        Assert.Empty(memoryBacked.Diagnostics);

        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(pathBacked.Content.LibraryApi);
        LibraryApiDiffDocument document = available.Document;
        LibraryApiDiffDocument memoryDocument =
            Assert.IsType<LibraryApiDiffOutcome.Available>(memoryBacked.Content.LibraryApi).Document;
        Assert.Equal(document, memoryDocument);
        Assert.Equal(document.GetHashCode(), memoryDocument.GetHashCode());
        Assert.True(document.Before.IsComplete);
        Assert.True(document.After.IsComplete);
        Assert.NotEqual(document.Before.Identity.Version, document.After.Identity.Version);
        Assert.NotEmpty(document.Comparison.Subjects);
        Assert.Equal(document.Comparison.Subjects.Length, document.Summary.ChangedTypeCount);
        Assert.True(document.Summary.ChangedMemberCount > 0);
        Assert.True(document.Summary.BreakingCount > 0);
        Assert.True(document.Summary.AdditiveCount > 0);

        // The binding policy resolves no dependency, so the method constraint
        // replacements stay visible with their dependency absent.
        ComparisonSubject<LibraryApiTypeDiff> constraintChange =
            Assert.Single(
                document.Comparison.Subjects,
                subject => subject.Display
                    == "LibraryApiDiffFixture.MethodConstraintChange");
        Assert.Collection(
            constraintChange.Comparison.CompatibilityChanges,
            tightened =>
            {
                Assert.Equal(
                    ChangeKind.TypeParameterConstraintTightened,
                    tightened.Kind);
                Assert.Equal(
                    "LibraryApiDiffFixture.Dependency.AfterConstraint",
                    tightened.NewValue?.ToString());
                Assert.Equal(
                    ApiChangeSubjectKind.Member,
                    tightened.Subject.Kind);
            },
            loosened =>
            {
                Assert.Equal(
                    ChangeKind.TypeParameterConstraintLoosened,
                    loosened.Kind);
                Assert.Equal(
                    "LibraryApiDiffFixture.Dependency.BeforeConstraint",
                    loosened.OldValue?.ToString());
                Assert.Equal(
                    ApiChangeSubjectKind.Member,
                    loosened.Subject.Kind);
            });
    }

    [Fact]
    public async Task ExecuteLibraryPair_SameImage_ReturnsSuccessfulEmptyCompleteDocument()
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();

        InspectionEnvelope<DiffAnalysisDocument> envelope =
            await ExecutePathBacked(path, path, GenerousLimits);

        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(envelope.Content.LibraryApi);
        Assert.True(available.Document.Before.IsComplete);
        Assert.True(available.Document.After.IsComplete);
        Assert.Empty(available.Document.Comparison.Subjects);
        Assert.Equal(0, available.Document.Summary.ChangedTypeCount);
        Assert.Equal(0, available.Document.Summary.ChangedMemberCount);
        Assert.Equal(0, available.Document.Summary.BreakingCount);
        Assert.Equal(0, available.Document.Summary.AdditiveCount);
        Assert.Equal(0, available.Document.Summary.PotentiallyBreakingCount);
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task ExecuteLibraryPair_IndependentEndpointTypeLimit_ReturnsTypedUnavailableWithHealthyCounterpart()
    {
        var limits = new ApiSurfaceProjectionLimits(
            maxParticipants: 64,
            maxTypes: 7,
            maxMembers: 1_000_000,
            maxInspectionFailures: int.MaxValue,
            maxTypeForwarders: int.MaxValue,
            maxMetadataRows: int.MaxValue);

        InspectionEnvelope<DiffAnalysisDocument> envelope =
            await ExecutePathBacked(
                FixtureCatalog.DiffV1.AssemblyPath(),
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                limits);

        LibraryApiDiffOutcome.Unavailable unavailable =
            Assert.IsType<LibraryApiDiffOutcome.Unavailable>(envelope.Content.LibraryApi);
        Assert.Equal(
            LibraryApiDiffUnavailableKind.BeforeIncomplete,
            unavailable.Kind);
        Assert.False(unavailable.Before.IsComplete);
        Assert.Contains(
            unavailable.Before.Issues,
            issue => issue is LibraryApiDiffEndpointIssue.Truncated);
        Assert.True(unavailable.After.IsComplete);
        Assert.Empty(unavailable.After.Issues);
    }

    [Fact]
    public async Task ExecuteLibraryPair_DifferentLogicalLibraryIdentities_ReturnsTypedRejection()
    {
        InspectionEnvelope<DiffAnalysisDocument> envelope =
            await ExecutePathBacked(
                FixtureCatalog.DiffV1.AssemblyPath(),
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
                GenerousLimits);

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(envelope.Content.LibraryApi);
        Assert.Equal(
            LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            rejected.Kind);
        Assert.True(rejected.Before.IsComplete);
        Assert.True(rejected.After.IsComplete);
    }

    [Fact]
    public async Task ExecuteLibraryPair_ForwardedConstraint_DoesNotResolveDependencyAcrossHosts()
    {
        // A zero inspection-failure budget: resolving the forwarded
        // constraint's dependency would record a failure and truncate.
        var limits = new ApiSurfaceProjectionLimits(
            1, 1_000_000, 1_000_000, 0, 1_000_000, 10_000_000);
        InspectionEnvelope<DiffAnalysisDocument> pathBacked =
            await ExecuteForwardedConstraint(memoryBacked: false, limits);
        InspectionEnvelope<DiffAnalysisDocument> memoryBacked =
            await ExecuteForwardedConstraint(memoryBacked: true, limits);

        Assert.Equal(pathBacked.Content.LibraryApi, memoryBacked.Content.LibraryApi);
        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(pathBacked.Content.LibraryApi);
        Assert.Empty(available.Document.Before.Issues);
        Assert.Empty(available.Document.After.Issues);
        Assert.Empty(available.Document.Comparison.Subjects);
    }

    [Fact]
    public async Task ExecuteLibraryPair_LibraryApiOnly_RunsNoAnalysis()
    {
        InspectionEnvelope<DiffAnalysisDocument> envelope =
            await ExecutePathBacked(
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
                GenerousLimits);

        DiffAnalysisDocument document = envelope.Content;
        Assert.IsType<LibraryApiDiffOutcome.Available>(document.LibraryApi);
        Assert.Equal(AnalysisReportSurfaceKind.Library, document.Comparison.Surface);
        Assert.Equal(DiffAnalysisDocumentViews.None, document.Comparison.Views);
        Assert.Empty(document.Comparison.Analyses);
        Assert.Empty(document.Comparison.Predicates);
        Assert.Empty(document.Outcomes);
        Assert.Null(document.Changes);
        Assert.Null(document.Summary);
        Assert.Null(document.Transitions);
        Assert.Null(document.GetApiChanges());
    }

    [Fact]
    public async Task ExecuteLibraryPair_TypeSelection_NarrowsTheComparison()
    {
        const string selected = "LibraryApiDiffFixture.MethodConstraintChange";
        string beforePath = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string afterPath = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();
        InspectionEnvelope<DiffAnalysisDocument> whole =
            await ExecutePathBacked(beforePath, afterPath, GenerousLimits);
        InspectionEnvelope<DiffAnalysisDocument> narrowed =
            await ExecutePathBacked(
                beforePath,
                afterPath,
                GenerousLimits,
                new ApiTypeSelection(name => name == selected));

        string[] wholeTypes = ChangedTypes(whole);
        string[] narrowedTypes = ChangedTypes(narrowed);
        Assert.Contains(selected, narrowedTypes);
        Assert.Subset(wholeTypes.ToHashSet(), narrowedTypes.ToHashSet());
        Assert.True(narrowedTypes.Length < wholeTypes.Length);

        static string[] ChangedTypes(InspectionEnvelope<DiffAnalysisDocument> envelope)
            => [
                .. Assert.IsType<LibraryApiDiffOutcome.Available>(
                        envelope.Content.LibraryApi)
                    .Document.Comparison.Subjects
                    .Select(subject => subject.Display),
            ];
    }

    [Fact]
    public async Task ExecuteLibraryPair_TypeSelectionWithReuse_IsRejected()
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        using AssemblyContextGroup group =
            SinglePathGroup(workspace, path, "Before", policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            DiffOperation.ExecuteLibraryPair(
                LibraryApiOnlyRequest(
                    GenerousLimits,
                    new ApiTypeSelection(static _ => true)),
                new DiffLibraryPair(group, participant, group, participant),
                new DiffPairContent([]),
                static compute => compute()));
        Assert.Equal("reuse", exception.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteLibraryPair_AnalysisSet_CarriesLibraryApiOnlyWhenAsked(bool libraryApi)
    {
        InspectionCapabilityCatalog catalog =
            InspectionCapabilityCatalog.Create([DiffAnalysisCatalog.ProductModule]);
        var selection = Assert.IsType<AnalysisSetValidationResult.Accepted>(
            catalog.AnalysisCapabilities.ValidateSet(
                DiffAnalysisCatalog.Operation,
                AnalysisReportSurfaceKind.Library,
                targetCount: 1,
                ["api"]));
        var request = new LibraryPairDiffRequest(
            "LibraryApiDiffFixture",
            "1.0.0",
            "2.0.0",
            catalog,
            selection,
            DiffAnalysisDocumentViews.Changes,
            libraryApi,
            new DiffTargets.Library(),
            ApiSurfaceScope.Public,
            GenerousLimits);

        InspectionEnvelope<DiffAnalysisDocument> envelope =
            await ExecutePathBacked(
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
                request);

        Assert.Equal(["api"], envelope.Content.Comparison.Analyses);
        Assert.Single(envelope.Content.Outcomes);
        Assert.Equal(libraryApi, envelope.Content.LibraryApi is not null);
    }

    static LibraryPairDiffRequest LibraryApiOnlyRequest(
        ApiSurfaceProjectionLimits limits,
        ApiTypeSelection? typeSelection = null)
        => new(
            "LibraryApiDiffFixture",
            "1.0.0",
            "2.0.0",
            ApiSurfaceScope.Public,
            limits,
            typeSelection);

    static InspectionEnvelope<DiffAnalysisDocument> Execute(
        LibraryPairDiffRequest request,
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after)
        => DiffOperation.ExecuteLibraryPair(
            request,
            new DiffLibraryPair(beforeGroup, before, afterGroup, after),
            new DiffPairContent([]));

    static async Task<InspectionEnvelope<DiffAnalysisDocument>> ExecuteForwardedConstraint(
        bool memoryBacked,
        ApiSurfaceProjectionLimits limits)
    {
        string[] paths =
        [
            FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath(),
            FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("middle"),
        ];
        ResolvedAssemblyReference[] assemblies =
        [
            .. paths.Select(path => memoryBacked
                ? MemoryAssembly(File.ReadAllBytes(path), "constraint fixture")
                : ResolvedAssemblyReference.CreateFromPath(
                    path, AssemblyResolutionProvenance.Local("constraint fixture"))),
        ];
        var policy = new TestBindingPolicy(assemblies);
        var participant = new AssemblyContextParticipant(assemblies[0], policy);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup before = workspace.CreateAssemblyContextGroup([participant]);
        using AssemblyContextGroup after = workspace.CreateAssemblyContextGroup([participant]);
        return Execute(
            LibraryApiOnlyRequest(limits), before, participant, after, participant);
    }

    static Task<InspectionEnvelope<DiffAnalysisDocument>> ExecutePathBacked(
        string beforePath,
        string afterPath,
        ApiSurfaceProjectionLimits limits,
        ApiTypeSelection? typeSelection = null)
        => ExecutePathBacked(
            beforePath,
            afterPath,
            LibraryApiOnlyRequest(limits, typeSelection));

    static async Task<InspectionEnvelope<DiffAnalysisDocument>> ExecutePathBacked(
        string beforePath,
        string afterPath,
        LibraryPairDiffRequest request)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            SinglePathGroup(workspace, beforePath, "Before", policy);
        using AssemblyContextGroup afterGroup =
            SinglePathGroup(workspace, afterPath, "After", policy);

        return Execute(
            request,
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants));
    }

    static async Task<InspectionEnvelope<DiffAnalysisDocument>> ExecuteMemoryBacked(
        string beforePath,
        string afterPath,
        ApiSurfaceProjectionLimits limits)
    {
        byte[] beforeBytes = File.ReadAllBytes(beforePath);
        byte[] afterBytes = File.ReadAllBytes(afterPath);
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant beforeParticipant =
            MemoryParticipant(beforeBytes, "Before", policy);
        AssemblyContextParticipant afterParticipant =
            MemoryParticipant(afterBytes, "After", policy);
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);

        return Execute(
            LibraryApiOnlyRequest(limits),
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant);
    }

    static AssemblyContextGroup SinglePathGroup(
        InspectionWorkspace workspace,
        string path,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local(provenanceLabel)),
                    policy),
            ]);

    static AssemblyContextParticipant MemoryParticipant(
        byte[] bytes,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => new(MemoryAssembly(bytes, provenanceLabel), policy);

    static ResolvedAssemblyReference MemoryAssembly(byte[] bytes, string provenanceLabel)
    {
        using var reader = new PEReader(new MemoryStream(bytes, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
        return ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(bytes, writable: false),
            AssemblyResolutionProvenance.Local(provenanceLabel));
    }

    sealed class TestBindingPolicy(IReadOnlyList<ResolvedAssemblyReference>? assemblies = null)
        : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            ResolvedAssemblyReference? match = request.Target is AssemblyBindingTarget.AssemblyReference target
                ? assemblies?.FirstOrDefault(assembly =>
                    AssemblyReferenceIdentity.EquivalentComparer.Equals(assembly.Identity, target.Identity))
                : null;
            return new(
                Version,
                match is not null
                    ? AssemblyBindingSelection.Found(match)
                    : AssemblyBindingSelection.CannotSelect(
                        new AssemblyBindingFailure(AssemblyBindingFailureKind.CandidateUnavailable)));
        }
    }
}
