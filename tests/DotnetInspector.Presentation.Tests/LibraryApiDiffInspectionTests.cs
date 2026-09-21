using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation.Tests;

public sealed class LibraryApiDiffInspectionTests
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    [Fact]
    public async Task Execute_PathAndMemoryBackedVersionPair_ProducesEquivalentDetachedEnvelopes()
    {
        string beforePath = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string afterPath = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();

        InspectionEnvelope<LibraryApiDiffOutcome> pathBacked =
            await ExecutePathBacked(
                beforePath,
                afterPath,
                ApiSurfaceScope.Public,
                GenerousLimits);
        InspectionEnvelope<LibraryApiDiffOutcome> memoryBacked =
            await ExecuteMemoryBacked(
                beforePath,
                afterPath,
                ApiSurfaceScope.Public,
                GenerousLimits);

        Assert.Equal(pathBacked.Content, memoryBacked.Content);
        InspectionPortableProjection.NonProjectable pathProjection =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(pathBacked.PortableProjection);
        InspectionPortableProjection.NonProjectable memoryProjection =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(memoryBacked.PortableProjection);
        Assert.Equal(pathBacked.ResourcePath, memoryBacked.ResourcePath);
        Assert.Equal(pathProjection.Location, memoryProjection.Location);
        Assert.Equal(pathProjection.Reason, memoryProjection.Reason);
        Assert.Equal(pathProjection.Explanation, memoryProjection.Explanation);
        Assert.Equal("library-api-diff", pathBacked.ResourcePath.Value);
        Assert.Equal("endpoints", pathProjection.Location);
        Assert.Equal(
            InspectionPortableProjectionFailureReason.NotSupported,
            pathProjection.Reason);
        string explanation = Assert.IsType<string>(pathProjection.Explanation);
        Assert.Contains(
            "ordered endpoints",
            explanation,
            StringComparison.Ordinal);
        Assert.Contains(
            "API scope",
            explanation,
            StringComparison.Ordinal);
        Assert.Equal(pathBacked.Diagnostics.ToArray(), memoryBacked.Diagnostics.ToArray());
        Assert.Equal(pathBacked, memoryBacked);

        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(pathBacked.Content);
        LibraryApiDiffDocument document = available.Document;
        LibraryApiDiffDocument memoryDocument =
            Assert.IsType<LibraryApiDiffOutcome.Available>(memoryBacked.Content).Document;
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
    }

    [Fact]
    public async Task Execute_SameImage_ReturnsSuccessfulEmptyCompleteDocument()
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();

        InspectionEnvelope<LibraryApiDiffOutcome> envelope =
            await ExecutePathBacked(
                path,
                path,
                ApiSurfaceScope.Public,
                GenerousLimits);

        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(envelope.Content);
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
    public async Task Execute_IndependentEndpointTypeLimit_ReturnsTypedUnavailableWithHealthyCounterpart()
    {
        var limits = new ApiSurfaceProjectionLimits(
            maxParticipants: 64,
            maxTypes: 6,
            maxMembers: 1_000_000,
            maxInspectionFailures: int.MaxValue,
            maxTypeForwarders: int.MaxValue,
            maxMetadataRows: int.MaxValue);

        InspectionEnvelope<LibraryApiDiffOutcome> envelope =
            await ExecutePathBacked(
                FixtureCatalog.DiffV1.AssemblyPath(),
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
                ApiSurfaceScope.Public,
                limits);

        LibraryApiDiffOutcome.Unavailable unavailable =
            Assert.IsType<LibraryApiDiffOutcome.Unavailable>(envelope.Content);
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
    public async Task Execute_DifferentLogicalLibraryIdentities_ReturnsTypedRejection()
    {
        InspectionEnvelope<LibraryApiDiffOutcome> envelope =
            await ExecutePathBacked(
                FixtureCatalog.DiffV1.AssemblyPath(),
                FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
                ApiSurfaceScope.Public,
                GenerousLimits);

        LibraryApiDiffOutcome.Rejected rejected =
            Assert.IsType<LibraryApiDiffOutcome.Rejected>(envelope.Content);
        Assert.Equal(
            LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            rejected.Kind);
        Assert.True(rejected.Before.IsComplete);
        Assert.True(rejected.After.IsComplete);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Execute_ForwardedConstraint_PreservesDependencyEvidenceAcrossHosts(bool includeBase)
    {
        var pathBacked = await ExecuteForwardedConstraint(
            memoryBacked: false, includeBase, GenerousLimits);
        var memoryBacked = await ExecuteForwardedConstraint(
            memoryBacked: true, includeBase, GenerousLimits);

        Assert.Equal(pathBacked, memoryBacked);
        if (includeBase)
        {
            var available = Assert.IsType<LibraryApiDiffOutcome.Available>(pathBacked.Content);
            Assert.Empty(available.Document.Comparison.Subjects);
        }
        else
        {
            var unavailable = Assert.IsType<LibraryApiDiffOutcome.Unavailable>(pathBacked.Content);
            Assert.False(unavailable.Before.IsComplete);
            Assert.False(unavailable.After.IsComplete);
            var issue = Assert.Single(
                unavailable.Before.Issues.OfType<LibraryApiDiffEndpointIssue.InspectionFailures>());
            Assert.Contains(issue.Details, failure =>
                failure.Operation.ToString() == ApiSurfaceInspectionFailure.GenericParameterConstraintResolutionOperation
                && failure.DependencyAssembly?.Name == "DotnetInspector.Services.RouteLearning.Base");
        }
    }

    [Fact]
    public async Task Execute_ConstraintFailureExceedsBudget_ReturnsTruncationInsteadOfEmptySuccess()
    {
        var limits = new ApiSurfaceProjectionLimits(
            1, 1_000_000, 1_000_000, 0, 1_000_000, 10_000_000);
        var envelope = await ExecuteForwardedConstraint(
            memoryBacked: true, includeBase: false, limits);

        var unavailable = Assert.IsType<LibraryApiDiffOutcome.Unavailable>(envelope.Content);
        var truncated = Assert.Single(
            unavailable.Before.Issues.OfType<LibraryApiDiffEndpointIssue.Truncated>());
        Assert.Equal(ApiSurfaceProjectionLimit.InspectionFailures, truncated.Truncation.Limit);
    }

    static async Task<InspectionEnvelope<LibraryApiDiffOutcome>> ExecuteForwardedConstraint(
        bool memoryBacked,
        bool includeBase,
        ApiSurfaceProjectionLimits limits)
    {
        string[] paths =
        [
            FixtureCatalog.ServicesRouteLearningConsumer.AssemblyPath(),
            FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("middle"),
            .. includeBase
                ? new[] { FixtureCatalog.ServicesRouteLearningConsumer.AssetPath("base") }
                : [],
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
        return LibraryApiDiffInspection.Execute(
            before, participant, after, participant, ApiSurfaceScope.Public, limits);
    }

    static async Task<InspectionEnvelope<LibraryApiDiffOutcome>>
        ExecutePathBacked(
            string beforePath,
            string afterPath,
            ApiSurfaceScope scope,
            ApiSurfaceProjectionLimits limits)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            SinglePathGroup(workspace, beforePath, "Before", policy);
        using AssemblyContextGroup afterGroup =
            SinglePathGroup(workspace, afterPath, "After", policy);

        return LibraryApiDiffInspection.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            scope,
            limits);
    }

    static async Task<InspectionEnvelope<LibraryApiDiffOutcome>>
        ExecuteMemoryBacked(
            string beforePath,
            string afterPath,
            ApiSurfaceScope scope,
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

        return LibraryApiDiffInspection.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            scope,
            limits);
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
