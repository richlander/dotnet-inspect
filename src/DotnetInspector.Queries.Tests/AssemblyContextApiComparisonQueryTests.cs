using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Gates the two-endpoint library comparison query: ordered before/after acquisition, each
/// endpoint's independent bounded projection, and the Metadata-owned comparison the query
/// retains only when both endpoints are exactly and completely projected.
/// </summary>
public sealed class AssemblyContextApiComparisonQueryTests(ITestOutputHelper output)
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    // The same physical image registered in two distinct groups is the trivial A-vs-A case: an
    // exact comparison, with an unselected decoy participant on each side that must never be
    // opened or projected.
    [Fact]
    public void Execute_SameImageAcrossDistinctGroups_IsExactAndIgnoresDecoyParticipants()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);
        AssemblyReferenceIdentity identity = IdentityOf(bytes);
        var policy = new TestBindingPolicy();
        int selectedBeforeOpens = 0;
        int decoyBeforeOpens = 0;
        int selectedAfterOpens = 0;
        int decoyAfterOpens = 0;

        var selectedBefore = Participant(
            identity,
            bytes,
            () => Interlocked.Increment(ref selectedBeforeOpens),
            "selected-before",
            policy);
        var decoyBefore = Participant(
            identity with { Name = "DecoyBefore" },
            bytes,
            () => Interlocked.Increment(ref decoyBeforeOpens),
            "decoy-before",
            policy);
        var selectedAfter = Participant(
            identity,
            bytes,
            () => Interlocked.Increment(ref selectedAfterOpens),
            "selected-after",
            policy);
        var decoyAfter = Participant(
            identity with { Name = "DecoyAfter" },
            bytes,
            () => Interlocked.Increment(ref decoyAfterOpens),
            "decoy-after",
            policy);

        using var workspace = new InspectionWorkspace();
        // The decoy comes first in one group and second in the other, so selection cannot be
        // relying on participant position within the group.
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([decoyBefore, selectedBefore]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([selectedAfter, decoyAfter]);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            selectedBefore,
            afterGroup,
            selectedAfter,
            ApiSurfaceScope.Public,
            GenerousLimits);

        Assert.True(result.IsComplete);
        Assert.True(result.IsExact);
        Assert.NotNull(result.Comparison);
        Assert.True(result.Comparison.IsExact);
        Assert.Same(
            selectedBefore.Assembly.Registration,
            result.Before.Subject.Registration);
        Assert.Same(
            selectedAfter.Assembly.Registration,
            result.After.Subject.Registration);
        Assert.Equal(1, Volatile.Read(ref selectedBeforeOpens));
        Assert.Equal(1, Volatile.Read(ref selectedAfterOpens));
        Assert.Equal(0, Volatile.Read(ref decoyBeforeOpens));
        Assert.Equal(0, Volatile.Read(ref decoyAfterOpens));
        WriteOutcome("Same image, distinct registrations", result);
    }

    // A real version pair retains Metadata's own type/member correspondence and its breaking and
    // additive classification, exercising the actual product comparison rather than a
    // success-shaped stand-in.
    [Fact]
    public void Execute_RealVersionPair_RetainsMetadataOwnedBreakingAndAdditiveClassification()
    {
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup = SinglePathGroup(
            workspace,
            FixtureCatalog.DiffV1.AssemblyPath(),
            "diff v1",
            policy);
        using AssemblyContextGroup afterGroup = SinglePathGroup(
            workspace,
            FixtureCatalog.DiffV2.AssemblyPath(),
            "diff v2",
            policy);
        AssemblyContextParticipant beforeParticipant = Assert.Single(beforeGroup.Participants);
        AssemblyContextParticipant afterParticipant = Assert.Single(afterGroup.Participants);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.Public,
            GenerousLimits);

        Assert.True(result.IsComplete);
        Assert.False(result.IsExact);
        ApiFindingComparison comparison = Assert.IsType<ApiFindingComparison>(result.Comparison);
        Assert.True(comparison.ApiDiff.TotalBreaking > 0);
        Assert.True(comparison.ApiDiff.TotalAdditive > 0);

        TypeDiff methodRemovalDiff = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "DiffFixtureSample.MethodRemovalSample");
        Assert.Contains(
            methodRemovalDiff.Changes,
            change => change.Kind == ChangeKind.MemberRemoved
                && change.Classification == ChangeClassification.Breaking
                && change.Subject!.MemberName == "Removed");

        TypeDiff bodyStateDiff = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "DiffFixtureSample.BodyStateSample");
        Assert.Contains(
            bodyStateDiff.Changes,
            change => change.Kind == ChangeKind.AbstractRemoved
                && change.Classification == ChangeClassification.Additive);

        // MethodRemovalSample retains its type identity across the pair.
        var types = Assert.IsType<FindingComparison<ApiTypeHandle>.Complete>(comparison.Types.Value);
        Assert.Contains(
            types.Pairs,
            pair => pair is PairFinding<ApiTypeHandle>.Present present
                && present.New.Payload.TypeFullName == "DiffFixtureSample.MethodRemovalSample");
        WriteOutcome("Fixture version pair", result);
        foreach (TypeDiff type in new[] { methodRemovalDiff, bodyStateDiff })
        {
            output.WriteLine($"{type.TypeFullName}: {string.Join(", ",
                type.Changes.Select(change => $"{change.Classification}/{change.Kind}"))}");
        }
    }

    // A valid empty public API is a completed comparison, not an unavailable one.
    [Fact]
    public void Execute_ValidEmptyPublicApi_IsCompleteAndExact()
    {
        byte[] bytes = BuildTypedApiSurfaceImage(typeCount: 0);
        AssemblyReferenceIdentity identity = IdentityOf(bytes);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant beforeParticipant = Participant(
            identity,
            bytes,
            openedCallback: null,
            "empty-before",
            policy);
        AssemblyContextParticipant afterParticipant = Participant(
            identity,
            bytes,
            openedCallback: null,
            "empty-after",
            policy);
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.Public,
            GenerousLimits);

        Assert.True(result.Before.IsComplete);
        Assert.True(result.After.IsComplete);
        Assert.Empty(Available(result.Before.Projection.Assemblies).Surface.Types);
        Assert.Empty(Available(result.After.Projection.Assemblies).Surface.Types);
        Assert.True(result.IsComplete);
        Assert.True(result.IsExact);
        WriteOutcome("Empty public API", result);
    }

    // A rejected endpoint on either side must still attempt the opposite endpoint, retaining its
    // complete facts rather than collapsing to an empty successful diff.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execute_RejectedEndpoint_RetainsTheOppositeEndpointWithoutComparison(
        bool rejectBefore)
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);
        AssemblyReferenceIdentity identity = IdentityOf(bytes);
        var policy = new TestBindingPolicy();
        AssemblyContextParticipant healthy = Participant(
            identity,
            bytes,
            openedCallback: null,
            "healthy",
            policy);
        AssemblyContextParticipant rejected = Participant(
            identity with { Name = "WrongIdentity" },
            bytes,
            openedCallback: null,
            "rejected",
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup(
            [rejectBefore ? rejected : healthy]);
        using AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup(
            [rejectBefore ? healthy : rejected]);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            ApiSurfaceScope.Public,
            GenerousLimits);

        Assert.Null(result.Comparison);
        Assert.False(result.IsComplete);
        AssemblyContextApiComparisonEndpoint rejectedEndpoint =
            rejectBefore ? result.Before : result.After;
        AssemblyContextApiComparisonEndpoint healthyEndpoint =
            rejectBefore ? result.After : result.Before;
        Assert.False(rejectedEndpoint.IsComplete);
        Assert.Equal("WrongIdentity", rejectedEndpoint.Subject.Identity.Name);
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            Assert.Single(rejectedEndpoint.Projection.Assemblies.Assemblies));
        Assert.True(healthyEndpoint.IsComplete);
        Assert.NotEmpty(Available(healthyEndpoint.Projection.Assemblies).Surface.Types);
        WriteOutcome(rejectBefore ? "Rejected Before" : "Rejected After", result);
    }

    // A row-level inspection failure can leave the owning projection's own IsComplete true (the
    // surface is the healthy subset beside a recorded failure); the comparison endpoint must
    // still be incomplete and the comparison must not run, so a healthy subset never yields false
    // additions or removals.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execute_RowLevelInspectionFailure_SuppressesComparisonButRetainsFactsAndDiagnostics(
        bool failBefore)
    {
        byte[] partialImage = AssemblyContextApiSurfaceQueryTests.BuildPartialSurfaceImage();
        AssemblyReferenceIdentity partialIdentity = IdentityOf(partialImage);
        byte[] healthyBytes = File.ReadAllBytes(SelfPath);
        var policy = new TestBindingPolicy();
        AssemblyContextParticipant partialParticipant = Participant(
            partialIdentity,
            partialImage,
            openedCallback: null,
            "partial",
            policy);
        AssemblyContextParticipant healthyParticipant = Participant(
            IdentityOf(healthyBytes),
            healthyBytes,
            openedCallback: null,
            "healthy",
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup(
                [failBefore ? partialParticipant : healthyParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup(
                [failBefore ? healthyParticipant : partialParticipant]);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            ApiSurfaceScope.PublicWithNonPublicTypes,
            GenerousLimits);
        AssemblyContextApiComparisonEndpoint partial =
            failBefore ? result.Before : result.After;
        AssemblyContextApiComparisonEndpoint healthy =
            failBefore ? result.After : result.Before;

        // The owning surface projection is complete by its own contract: no bound was exceeded
        // and every participant is Available.
        Assert.Null(partial.Projection.Truncation);
        Assert.True(partial.Projection.IsComplete);
        AssemblyApiSurface partialSurface = Available(partial.Projection.Assemblies);
        Assert.NotEmpty(partialSurface.InspectionFailures);
        Assert.Contains(partialSurface.Surface.Types, type => type.Name == "Sibling");

        // But the comparison endpoint itself must not claim completeness over a subset.
        Assert.False(partial.IsComplete);
        Assert.True(healthy.IsComplete);
        Assert.Null(result.Comparison);
        Assert.False(result.IsComplete);
        WriteOutcome(failBefore ? "Row failure Before" : "Row failure After", result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Execute_DegradedSignature_SuppressesComparisonButRetainsEndpointEvidence(
        bool degradeBefore)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature).FieldSignature().Object();
        var guardedSignature = new BlobBuilder();
        SignatureTypeEncoder fieldType = new BlobEncoder(guardedSignature).FieldSignature();
        for (int depth = 0; depth <= SignatureBlobGuard.DefaultMaxDepth; depth++)
            fieldType = fieldType.SZArray();
        fieldType.Object();
        byte[] degradedImage = BuildTypedApiSurfaceImage(
            1, "SignatureComparison", guardedSignature.ToArray());
        byte[] healthyImage = BuildTypedApiSurfaceImage(
            1, "SignatureComparison", signature.ToArray());
        var policy = new TestBindingPolicy();
        AssemblyContextParticipant degradedParticipant = Participant(
            IdentityOf(degradedImage), degradedImage, null, "degraded", policy);
        AssemblyContextParticipant healthyParticipant = Participant(
            IdentityOf(healthyImage), healthyImage, null, "healthy", policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup(
            [degradeBefore ? degradedParticipant : healthyParticipant]);
        using AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup(
            [degradeBefore ? healthyParticipant : degradedParticipant]);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            ApiSurfaceScope.Public,
            GenerousLimits);
        AssemblyContextApiComparisonEndpoint degraded =
            degradeBefore ? result.Before : result.After;
        AssemblyContextApiComparisonEndpoint healthy =
            degradeBefore ? result.After : result.Before;
        AssemblyApiSurface degradedSurface = Available(degraded.Projection.Assemblies);
        Assert.True(degraded.Projection.IsComplete);
        Assert.Empty(degradedSurface.InspectionFailures);
        ApiType degradedType = Assert.Single(degradedSurface.Surface.Types);
        ApiMember degradedMember = Assert.Single(degradedType.Members);
        Assert.Equal(SignatureDecodeStatus.Degraded, degradedMember.SignatureDecodeStatus);
        Assert.Same(degradedParticipant.Assembly.Registration, degraded.Subject.Registration);
        WriteOutcome(degradeBefore ? "Degraded signature Before" : "Degraded signature After", result);

        Assert.False(degraded.IsComplete);
        Assert.True(healthy.IsComplete);
        Assert.Null(result.Comparison);
        Assert.False(result.IsComplete);
        Assert.False(result.IsExact);
    }

    // Extraction bounds are explicit and per-endpoint: the same nominal per-type budget lets a
    // small enough endpoint complete even though the paired endpoint on the other side overflowed
    // it, proving the budget is never shared or halved across the pair.
    [Fact]
    public void Execute_TightBudget_OmitsOverflowingEndpointButRetainsItsSubjectIdentity()
    {
        byte[] overflowBytes = BuildTypedApiSurfaceImage(typeCount: 2, assemblyName: "Overflow");
        byte[] fittingBytes = BuildTypedApiSurfaceImage(typeCount: 1, assemblyName: "Fitting");
        var policy = new TestBindingPolicy();
        AssemblyContextParticipant beforeParticipant = Participant(
            IdentityOf(overflowBytes),
            overflowBytes,
            openedCallback: null,
            "overflow",
            policy);
        AssemblyContextParticipant afterParticipant = Participant(
            IdentityOf(fittingBytes),
            fittingBytes,
            openedCallback: null,
            "fitting",
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);
        var tightLimits = new ApiSurfaceProjectionLimits(
            64, maxTypes: 1, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.Public,
            tightLimits);

        Assert.False(result.Before.IsComplete);
        Assert.Empty(result.Before.Projection.Assemblies.Assemblies);
        Assert.NotNull(result.Before.Projection.Truncation);
        // The subject survives even though every row of its projection was omitted.
        Assert.Same(
            beforeParticipant.Assembly.Registration,
            result.Before.Subject.Registration);
        Assert.Equal("Overflow", result.Before.Subject.Identity.Name);

        // The opposite endpoint, under the identical nominal per-type budget, still completes:
        // the budget was never spent by the omitted attempt on the other side.
        Assert.True(result.After.IsComplete);
        Assert.Null(result.After.Projection.Truncation);
        Assert.Null(result.Comparison);
        Assert.False(result.IsComplete);
        WriteOutcome("Before type limit", result);
    }

    // Both endpoints get their full declared budget independently: an exact per-type bound that
    // matches each image's own type count admits both sides, rather than the pair sharing one
    // combined budget.
    [Fact]
    public void Execute_ExactPerEndpointLimits_AdmitBothEndpointsIndependently()
    {
        const int typeCount = 3;
        byte[] bytes = BuildTypedApiSurfaceImage(typeCount, assemblyName: "ExactBudget");
        AssemblyReferenceIdentity identity = IdentityOf(bytes);
        var policy = new TestBindingPolicy();
        AssemblyContextParticipant beforeParticipant = Participant(
            identity,
            bytes,
            openedCallback: null,
            "exact-before",
            policy);
        AssemblyContextParticipant afterParticipant = Participant(
            identity,
            bytes,
            openedCallback: null,
            "exact-after",
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);
        var exactLimits = new ApiSurfaceProjectionLimits(
            64, maxTypes: typeCount, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.Public,
            exactLimits);

        Assert.Null(result.Before.Projection.Truncation);
        Assert.Null(result.After.Projection.Truncation);
        Assert.True(result.Before.IsComplete);
        Assert.True(result.After.IsComplete);
        Assert.Equal(typeCount, Available(result.Before.Projection.Assemblies).Surface.Types.Count);
        Assert.Equal(typeCount, Available(result.After.Projection.Assemblies).Surface.Types.Count);
        Assert.True(result.IsComplete);
        Assert.True(result.IsExact);
    }

    // The requested scope applies identically to both endpoints: an include-all request reaches
    // each side's non-public types, and the result reports the scope it was asked for.
    [Fact]
    public void Execute_RetainsTheRequestedScopeForBothEndpoints()
    {
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup = SinglePathGroup(
            workspace, SelfPath, "scope-before", policy);
        using AssemblyContextGroup afterGroup = SinglePathGroup(
            workspace, SelfPath, "scope-after", policy);
        AssemblyContextParticipant beforeParticipant = Assert.Single(beforeGroup.Participants);
        AssemblyContextParticipant afterParticipant = Assert.Single(afterGroup.Participants);

        AssemblyContextApiComparisonResult result = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.IncludeAll,
            GenerousLimits);

        Assert.Equal(ApiSurfaceScope.IncludeAll, result.Scope);
        Assert.Contains(
            Available(result.Before.Projection.Assemblies).Surface.Types,
            type => type.Name == nameof(ApiSurfaceInternalProbe));
        Assert.Contains(
            Available(result.After.Projection.Assemblies).Surface.Types,
            type => type.Name == nameof(ApiSurfaceInternalProbe));
        Assert.True(result.IsExact);
    }

    [Fact]
    public void Execute_ThrowsForNullArguments()
    {
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SinglePathGroup(
            workspace, SelfPath, "null-args", policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);

        Assert.Throws<ArgumentNullException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                null!, participant, group, participant, ApiSurfaceScope.Public, GenerousLimits));
        Assert.Throws<ArgumentNullException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                group, null!, group, participant, ApiSurfaceScope.Public, GenerousLimits));
        Assert.Throws<ArgumentNullException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                group, participant, null!, participant, ApiSurfaceScope.Public, GenerousLimits));
        Assert.Throws<ArgumentNullException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                group, participant, group, null!, ApiSurfaceScope.Public, GenerousLimits));
        Assert.Throws<ArgumentNullException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                group, participant, group, participant, ApiSurfaceScope.Public, null!));
    }

    [Fact]
    public void Execute_ThrowsForUndefinedScope()
    {
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SinglePathGroup(
            workspace, SelfPath, "undefined-scope", policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                group,
                participant,
                group,
                participant,
                (ApiSurfaceScope)99,
                GenerousLimits));
    }

    [Fact]
    public void Execute_ThrowsWhenAParticipantIsNotAMemberOfItsGroup()
    {
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup groupA = SinglePathGroup(
            workspace, SelfPath, "membership-a", policy);
        using AssemblyContextGroup groupB = SinglePathGroup(
            workspace, SelfPath, "membership-b", policy);
        AssemblyContextParticipant participantA = Assert.Single(groupA.Participants);
        AssemblyContextParticipant participantB = Assert.Single(groupB.Participants);

        Assert.Throws<ArgumentException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                groupA,
                participantB,
                groupB,
                participantB,
                ApiSurfaceScope.Public,
                GenerousLimits));
        Assert.Throws<ArgumentException>(
            () => AssemblyContextApiComparisonQuery.Execute(
                groupA,
                participantA,
                groupB,
                participantA,
                ApiSurfaceScope.Public,
                GenerousLimits));
    }

    [Fact]
    public void Definition_UsesTheDeclaredNameAndNetworkFreeCostThroughTheRegistry()
    {
        Assert.Equal(
            "Assembly context API comparison",
            AssemblyContextApiComparisonQuery.Definition.Name);
        Assert.Equal(
            InspectionCost.NetworkFree,
            AssemblyContextApiComparisonQuery.Definition.Cost);

        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SinglePathGroup(
            workspace, SelfPath, "registry", policy);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);
        var registry = new InspectionQueryRegistry<ComparisonContext>()
            .Add(
                AssemblyContextApiComparisonQuery.Definition,
                static (context, _) => AssemblyContextApiComparisonQuery.Execute(
                    context.BeforeGroup,
                    context.BeforeParticipant,
                    context.AfterGroup,
                    context.AfterParticipant,
                    context.Scope,
                    context.Limits));

        InspectionQueryResults results = registry.Run(
            [AssemblyContextApiComparisonQuery.Definition],
            new ComparisonContext(
                group, participant, group, participant, ApiSurfaceScope.Public, GenerousLimits));

        Assert.Same(
            results.Get(AssemblyContextApiComparisonQuery.Definition),
            results.Get(AssemblyContextApiComparisonQuery.Definition));
        Assert.Equal(
            InspectionCost.NetworkFree,
            registry.CostOf(AssemblyContextApiComparisonQuery.Definition));
    }

    sealed record ComparisonContext(
        AssemblyContextGroup BeforeGroup,
        AssemblyContextParticipant BeforeParticipant,
        AssemblyContextGroup AfterGroup,
        AssemblyContextParticipant AfterParticipant,
        ApiSurfaceScope Scope,
        ApiSurfaceProjectionLimits Limits);

    void WriteOutcome(string label, AssemblyContextApiComparisonResult result)
        => output.WriteLine(
            $"{label}: complete={result.IsComplete}, exact={result.IsExact}, "
            + $"Before API complete={result.Before.IsComplete}, "
            + $"After API complete={result.After.IsComplete}, "
            + $"comparison={result.Comparison is not null}");

    static string SelfPath =>
        typeof(AssemblyContextApiComparisonQueryTests).Assembly.Location;

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

    static AssemblyContextParticipant Participant(
        AssemblyReferenceIdentity identity,
        byte[] bytes,
        Action? openedCallback,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => new(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () =>
                {
                    openedCallback?.Invoke();
                    return new MemoryStream(bytes, writable: false);
                },
                AssemblyResolutionProvenance.Local(provenanceLabel)),
            policy);

    static AssemblyReferenceIdentity IdentityOf(byte[] bytes)
    {
        using var reader = new PEReader(new MemoryStream(bytes, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
    }

    static AssemblyApiSurface Available(AssemblyContextResult<AssemblyApiSurface> result)
        => Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                Assert.Single(result.Assemblies))
            .Value;

    /// <summary>
    /// A minimal public API surface with exactly <paramref name="typeCount"/> public types,
    /// an optional field on the last type, and no forwarders or interfaces.
    /// </summary>
    static byte[] BuildTypedApiSurfaceImage(
        int typeCount,
        string assemblyName = "ComparisonBudget",
        byte[]? fieldSignature = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(assemblyName + ".dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        for (int index = 0; index < typeCount; index++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("ComparisonBudgetTypes"),
                metadata.GetOrAddString($"Type{index}"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        if (fieldSignature is not null)
        {
            metadata.AddFieldDefinition(
                FieldAttributes.Public,
                metadata.GetOrAddString("Value"),
                metadata.GetOrAddBlob(fieldSignature));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
