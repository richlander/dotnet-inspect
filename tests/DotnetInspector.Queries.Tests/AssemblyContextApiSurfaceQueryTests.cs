using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Gates the group-scoped API-surface query: the scopes it projects, the product-owned
/// accessibility buckets it returns, the inspection failures it preserves, and the participant
/// ordering, failure isolation, and snapshot reuse every group-scoped query owes its consumer.
/// </summary>
public sealed class AssemblyContextApiSurfaceQueryTests
{
    [Fact]
    public void MetadataTypeIdentity_PreservesStructuredSegments()
    {
        MetadataTypeDefinitionName nestedName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer", "Inner"]))
            .Name;
        MetadataTypeDefinitionName topLevelName = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    ["Outer+Inner"]))
            .Name;
        var nested = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer.Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = nestedName,
        };
        var topLevel = new ApiType
        {
            Namespace = "Sample",
            Name = "Outer+Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = topLevelName,
        };

        Assert.Equal(
            "Sample.Outer+Inner",
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(nested));
        Assert.Equal(
            @"Sample.Outer\+Inner",
            AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(topLevel));
        Assert.Equal(
            @"Sample\\Tools.Outer\.Name+Inner\+Name",
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        @"Sample\Tools",
                        ["Outer.Name", "Inner+Name"]))
                .Name
                .ToEscapedFullName());
    }

    [Fact]
    public void PublicScope_ProjectsPublicTypesAndOnlyTheDefaultBucket()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyContextApiSurfaceResult result =
            AssemblyContextApiSurfaceQuery.Execute(group);

        AssemblyApiSurface surface = Available(result.Assemblies);
        Assert.Contains(
            surface.Surface.Types,
            type => type.FullName == typeof(ApiSurfacePublicProbe).FullName);
        Assert.DoesNotContain(
            surface.Surface.Types,
            type => type.Name == nameof(ApiSurfaceInternalProbe));

        ApiAccessibilityBucket bucket = Assert.Single(result.Accessibility);
        Assert.Equal("public", bucket.Id);
        Assert.True(bucket.IsDefault);
        Assert.Equal(0, bucket.Order);
        Assert.Equal(surface.Surface.Types.Count, bucket.Count);
    }

    [Fact]
    public void PublicWithNonPublicTypes_AddsOnlyNonPublicTypesAndKeepsPublicMemberLists()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyApiSurface composed = Available(
            AssemblyContextApiSurfaceQuery.Execute(
                    group,
                    ApiSurfaceScope.PublicWithNonPublicTypes)
                .Assemblies);
        AssemblyApiSurface publicOnly = Available(
            AssemblyContextApiSurfaceQuery.Execute(group).Assemblies);

        Assert.Contains(
            composed.Surface.Types,
            type => type.Name == nameof(ApiSurfaceInternalProbe));
        Assert.DoesNotContain(
            composed.Surface.Types,
            type => type.FullName == typeof(ApiSurfaceHiddenPublicProbe).FullName);

        // A public type keeps the member list the default surface gave it, so asking for
        // non-public types never silently adds private members to a public type.
        ApiType probe = Assert.Single(
            composed.Surface.Types,
            type => type.FullName == typeof(ApiSurfacePublicProbe).FullName);
        ApiType publicProbe = Assert.Single(
            publicOnly.Surface.Types,
            type => type.FullName == typeof(ApiSurfacePublicProbe).FullName);
        Assert.Equal(
            publicProbe.Members.Select(member => member.Name).Order(),
            probe.Members.Select(member => member.Name).Order());
        Assert.DoesNotContain(
            probe.Members,
            member => member.Name == nameof(ApiSurfacePublicProbe.HiddenSecret));
    }

    [Fact]
    public void IncludeAll_ProjectsNonPublicMembersOfPublicTypes()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyApiSurface surface = Available(
            AssemblyContextApiSurfaceQuery.Execute(group, includeAll: true).Assemblies);

        ApiType probe = Assert.Single(
            surface.Surface.Types,
            type => type.FullName == typeof(ApiSurfacePublicProbe).FullName);
        Assert.Contains(
            probe.Members,
            member => member.Name == nameof(ApiSurfacePublicProbe.HiddenSecret));
    }

    [Fact]
    public void Buckets_AreOrderedAndCountedWithPublicAsTheDefault()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyContextApiSurfaceResult result =
            AssemblyContextApiSurfaceQuery.Execute(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes);

        Assert.Equal(
            result.Accessibility.Select(bucket => bucket.Order).Order(),
            result.Accessibility.Select(bucket => bucket.Order));
        Assert.Equal(
            "public",
            Assert.Single(result.Accessibility, bucket => bucket.IsDefault).Id);
        Assert.Contains(
            result.Accessibility,
            bucket => bucket.Id == "internal" && bucket.Count > 0);
        Assert.Equal(
            Available(result.Assemblies).Surface.Types.Count,
            result.Accessibility.Sum(bucket => bucket.Count));
    }

    [Fact]
    public void CompositeAccessibility_ClassifiesAsTheMostVisibleReachableBucket()
    {
        Assert.Equal("public", ApiAccessibility.Classify(null).Id);
        Assert.Equal("public", ApiAccessibility.Classify("").Id);
        Assert.Equal("protected", ApiAccessibility.Classify("protected internal").Id);
        Assert.Equal("protected", ApiAccessibility.Classify("private protected").Id);
        Assert.Equal("internal", ApiAccessibility.Classify("internal").Id);
        Assert.Equal("private", ApiAccessibility.Classify("private").Id);
    }

    [Fact]
    public void Execute_CarriesRejectedParticipantBesideLaterResultsInGroupOrder()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);
        AssemblyReferenceIdentity identity = IdentityOf(bytes);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        identity with { Name = "WrongIdentity" },
                        path: null,
                        () => new MemoryStream(bytes, writable: false),
                        AssemblyResolutionProvenance.Local("rejected")),
                    policy),
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        SelfPath,
                        AssemblyResolutionProvenance.Local("available")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult result =
            AssemblyContextApiSurfaceQuery.Execute(group);

        Assert.False(result.IsComplete);
        var rejected = Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Rejected>(
            result.Assemblies.Assemblies[0]);
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.Equal("WrongIdentity", rejected.Subject.Identity.Name);
        Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            result.Assemblies.Assemblies[1]);

        // The rejected participant still contributes its bucket-free absence: the default
        // bucket is present because a consumer's default filter must never select nothing.
        Assert.Contains(result.Accessibility, bucket => bucket.IsDefault);
    }

    [Fact]
    public void Execute_ReusesOneSnapshotPerParticipantAcrossRuns()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);
        int opens = 0;
        var policy = new TestBindingPolicy();
        ResolvedAssemblyReference assembly = ResolvedAssemblyReference.Create(
            IdentityOf(bytes),
            path: null,
            () =>
            {
                Interlocked.Increment(ref opens);
                return new MemoryStream(bytes, writable: false);
            },
            AssemblyResolutionProvenance.Local("snapshot reuse"));
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [new AssemblyContextParticipant(assembly, policy)]);

        AssemblyContextApiSurfaceQuery.Execute(group);
        AssemblyContextApiSurfaceQuery.Execute(
            group,
            ApiSurfaceScope.PublicWithNonPublicTypes);

        Assert.Equal(1, Volatile.Read(ref opens));
    }

    [Fact]
    public void ExecuteParticipant_ProjectsOnlyTheRequestedParticipant()
    {
        var policy = new TestBindingPolicy();
        var first = new AssemblyContextParticipant(
            ResolvedAssemblyReference.CreateFromPath(
                SelfPath,
                AssemblyResolutionProvenance.Local("first")),
            policy);
        var second = new AssemblyContextParticipant(
            ResolvedAssemblyReference.CreateFromPath(
                SelfPath,
                AssemblyResolutionProvenance.Local("second")),
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([first, second]);

        var available = Assert.IsType<
            AssemblyContextEntry<AssemblyApiSurface>.Available>(
            AssemblyContextApiSurfaceQuery.ExecuteParticipant(
                group,
                second,
                ApiSurfaceScope.IncludeAll));

        Assert.Same(second.Assembly.Registration, available.Subject.Registration);
        Assert.Contains(
            available.Value.Surface.Types,
            type => type.FullName == typeof(ApiSurfacePublicProbe).FullName);
    }

    [Fact]
    public void Execute_PreservesApiSurfaceInspectionFailuresBesideHealthyTypes()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"api-surface-partial-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildPartialSurfaceImage());
        try
        {
            using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        ResolvedAssemblyReference.CreateFromPath(
                            path,
                            AssemblyResolutionProvenance.Local("partial")),
                        new TestBindingPolicy()),
                ]);

            AssemblyApiSurface surface = Available(
                AssemblyContextApiSurfaceQuery.Execute(
                        group,
                        ApiSurfaceScope.PublicWithNonPublicTypes)
                    .Assemblies);

            Assert.Contains(surface.Surface.Types, type => type.Name == "Sibling");
            Assert.NotEmpty(surface.InspectionFailures);
            Assert.Equal(
                surface.InspectionFailures,
                [.. surface.Surface.InspectionFailures]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string SelfPath =>
        typeof(AssemblyContextApiSurfaceQueryTests).Assembly.Location;

    // The composed scope is one extraction, so it must still answer exactly what the two-pass
    // composition answered: the public surface's types with their public member lists, plus the
    // non-public types with their complete ones, and nothing else.
    [Fact]
    public void PublicWithNonPublicTypes_EqualsThePublicAndNonPublicPartsOfBothScopes()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyApiSurface composed = Available(
            AssemblyContextApiSurfaceQuery.Execute(
                    group,
                    ApiSurfaceScope.PublicWithNonPublicTypes)
                .Assemblies);
        AssemblyApiSurface publicOnly = Available(
            AssemblyContextApiSurfaceQuery.Execute(group).Assemblies);
        AssemblyApiSurface all = Available(
            AssemblyContextApiSurfaceQuery.Execute(group, includeAll: true).Assemblies);

        string[] expected =
        [
            .. publicOnly.Surface.Types
                .Select(AssemblyContextApiSurfaceQuery.MetadataTypeIdentity)
                .Concat(all.Surface.Types
                    .Where(type => ApiAccessibility.Classify(type.Accessibility).Id != "public")
                    .Select(AssemblyContextApiSurfaceQuery.MetadataTypeIdentity))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        Assert.Equal(
            expected,
            composed.Surface.Types
                .Select(AssemblyContextApiSurfaceQuery.MetadataTypeIdentity)
                .Order(StringComparer.Ordinal));

        // Member lists come from the matching pass, per type.
        foreach (ApiType type in composed.Surface.Types)
        {
            string identity = AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type);
            IEnumerable<ApiType> source =
                ApiAccessibility.Classify(type.Accessibility).Id == "public"
                    ? publicOnly.Surface.Types
                    : all.Surface.Types;
            ApiType counterpart = Assert.Single(
                source,
                candidate =>
                    AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(candidate) == identity);
            Assert.Equal(
                counterpart.Members.Select(member => member.Name).Order(),
                type.Members.Select(member => member.Name).Order());
        }
    }

    [Fact]
    public void ExecuteBounded_WithGenerousLimitsMatchesTheUnboundedProjection()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyContextApiSurfaceResult unbounded =
            AssemblyContextApiSurfaceQuery.Execute(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes);
        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(
                    64,
                    100_000,
                    1_000_000,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        Assert.Null(bounded.Truncation);
        Assert.True(bounded.IsComplete);
        ApiSurface unboundedSurface = Available(unbounded.Assemblies).Surface;
        ApiSurface boundedSurface = Available(bounded.Assemblies).Surface;
        Assert.Equal(unboundedSurface.Types.Count, boundedSurface.Types.Count);

        // Identity, not just cardinality: an accounting error inside the bounded walk would drop
        // or reorder rows the unbounded walk keeps.
        Assert.Equal(
            unboundedSurface.Types.Select(
                type => (
                    AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                    type.Members.Count)),
            boundedSurface.Types.Select(
                type => (
                    AssemblyContextApiSurfaceQuery.MetadataTypeIdentity(type),
                    type.Members.Count)));
        Assert.Equal(
            unbounded.Accessibility.Select(bucket => (bucket.Id, bucket.Count)),
            bounded.Accessibility.Select(bucket => (bucket.Id, bucket.Count)));
    }

    // The exact-fit case: bounds equal to the unbounded projection's own totals must still project
    // the whole surface. An off-by-one in the budget would truncate here.
    [Fact]
    public void ExecuteBounded_AtExactlyTheProjectionSizeIsNotTruncated()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        ApiSurface unbounded =
            Available(
                    AssemblyContextApiSurfaceQuery.Execute(
                            group,
                            ApiSurfaceScope.PublicWithNonPublicTypes)
                        .Assemblies)
                .Surface;
        int types = unbounded.Types.Count;
        int members = unbounded.Types.Sum(type => type.Members.Count);

        AssemblyContextApiSurfaceResult exact =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(
                    64,
                    types,
                    members,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        Assert.Null(exact.Truncation);
        Assert.True(exact.IsComplete);
        Assert.Equal(types, Available(exact.Assemblies).Surface.Types.Count);

        // One less than it needs, and the participant is omitted rather than shortened.
        AssemblyContextApiSurfaceResult short_ =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.PublicWithNonPublicTypes,
                new ApiSurfaceProjectionLimits(
                    64,
                    types,
                    members - 1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        Assert.NotNull(short_.Truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Members, short_.Truncation!.Limit);
        Assert.Empty(short_.Assemblies.Assemblies);
    }

    // Non-vacuity: the bound is reachable and reported, and the reported result stays honest —
    // the only participant does not fit, so it is omitted rather than returned over the bound,
    // IsComplete is false, and the counts describe exactly the rows the result carries.
    [Fact]
    public void ExecuteBounded_OmitsAParticipantThatCannotFitTheTypeBound()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1,
                    1_000_000,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Types, truncation.Limit);
        Assert.Equal(1, truncation.Bound);
        Assert.Equal(0, truncation.ProjectedParticipants);
        Assert.Equal(1, truncation.OmittedParticipants);
        Assert.Equal(0, truncation.ProjectedTypes);
        Assert.Equal(0, truncation.ProjectedMembers);
        Assert.False(bounded.IsComplete);

        // No partial participant surface: the over-budget participant contributes no rows at all.
        Assert.Empty(bounded.Assemblies.Assemblies);
        AssertWithinBounds(bounded, maxTypes: 1, maxMembers: 1_000_000);
    }

    [Fact]
    public void ExecuteBounded_OmitsAParticipantThatCannotFitTheMemberBound()
    {
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = SelfGroup(workspace);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1_000_000,
                    1,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Members, truncation.Limit);
        Assert.Equal(1, truncation.Bound);
        Assert.Equal(0, truncation.ProjectedTypes);
        Assert.Equal(0, truncation.ProjectedMembers);
        Assert.Empty(bounded.Assemblies.Assemblies);
        AssertWithinBounds(bounded, maxTypes: 1_000_000, maxMembers: 1);
    }

    [Fact]
    public void ExecuteBounded_OmitsAParticipantAtTheInspectionFailureBound()
    {
        byte[] image = BuildPartialSurfaceImage(cyclicTypeCount: 2);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(image),
                        path: null,
                        () => new MemoryStream(image, writable: false),
                        AssemblyResolutionProvenance.Local("malformed")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1_000_000,
                    1_000_000,
                    1,
                    int.MaxValue,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.InspectionFailures, truncation.Limit);
        Assert.Equal(1, truncation.Bound);
        Assert.Empty(bounded.Assemblies.Assemblies);
        AssertWithinBounds(
            bounded,
            maxTypes: 1_000_000,
            maxMembers: 1_000_000,
            maxInspectionFailures: 1);
    }

    [Fact]
    public void ExecuteBounded_OmitsAParticipantAtTheTypeForwarderBound()
    {
        byte[] image = BuildBoundedSurfaceImage(
            typeCount: 1,
            typeForwarderCount: 1);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(image),
                        path: null,
                        () => new MemoryStream(image, writable: false),
                        AssemblyResolutionProvenance.Local("forwarder")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1_000_000,
                    1_000_000,
                    int.MaxValue,
                    0,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.TypeForwarders, truncation.Limit);
        Assert.Equal(0, truncation.Bound);
        Assert.Empty(bounded.Assemblies.Assemblies);
        AssertWithinBounds(
            bounded,
            maxTypes: 1_000_000,
            maxMembers: 1_000_000,
            maxTypeForwarders: 0);
    }

    [Fact]
    public void ExecuteBounded_MetadataRowsBoundNestedInterfaceFanOut()
    {
        byte[] image = BuildBoundedSurfaceImage(
            typeCount: 1,
            interfaceCount: 100);
        int metadataRows = MetadataRows(image);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(image),
                        path: null,
                        () => new MemoryStream(image, writable: false),
                        AssemblyResolutionProvenance.Local("interfaces")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1,
                    1,
                    0,
                    0,
                    metadataRows - 1));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.MetadataRows, truncation.Limit);
        Assert.Equal(metadataRows - 1, truncation.Bound);
        Assert.Equal(0, truncation.InspectedMetadataRows);
        Assert.Empty(bounded.Assemblies.Assemblies);
    }

    [Fact]
    public void ExecuteBounded_SpendsMetadataRowsAcrossParticipants()
    {
        byte[] firstImage = BuildBoundedSurfaceImage(
            typeCount: 1,
            interfaceCount: 2,
            assemblyName: "First");
        byte[] secondImage = BuildBoundedSurfaceImage(
            typeCount: 1,
            interfaceCount: 2,
            assemblyName: "Second");
        int metadataRows = MetadataRows(firstImage);
        Assert.Equal(metadataRows, MetadataRows(secondImage));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(firstImage),
                        path: null,
                        () => new MemoryStream(firstImage, writable: false),
                        AssemblyResolutionProvenance.Local("first")),
                    policy),
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(secondImage),
                        path: null,
                        () => new MemoryStream(secondImage, writable: false),
                        AssemblyResolutionProvenance.Local("second")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    2,
                    2,
                    0,
                    0,
                    metadataRows));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.MetadataRows, truncation.Limit);
        Assert.Equal(metadataRows, truncation.InspectedMetadataRows);
        Assert.Single(bounded.Assemblies.Assemblies);
        Assert.Equal(1, truncation.ProjectedParticipants);
        Assert.Equal(1, truncation.OmittedParticipants);
    }

    [Fact]
    public void ExecuteBounded_SpendsRetainedTextAcrossParticipants()
    {
        byte[] firstImage = BuildBoundedSurfaceImage(
            typeCount: 1,
            assemblyName: "First");
        byte[] secondImage = BuildBoundedSurfaceImage(
            typeCount: 1,
            assemblyName: "Third");
        int retainedTextCharacters = RetainedTextCharacters(firstImage);
        Assert.Equal(
            retainedTextCharacters,
            RetainedTextCharacters(secondImage));
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(firstImage),
                        path: null,
                        () => new MemoryStream(firstImage, writable: false),
                        AssemblyResolutionProvenance.Local("first")),
                    policy),
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(secondImage),
                        path: null,
                        () => new MemoryStream(secondImage, writable: false),
                        AssemblyResolutionProvenance.Local("second")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    2,
                    1,
                    0,
                    0,
                    int.MaxValue,
                    retainedTextCharacters));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(
            ApiSurfaceProjectionLimit.RetainedTextCharacters,
            truncation.Limit);
        Assert.Equal(retainedTextCharacters, truncation.Bound);
        Assert.Equal(
            retainedTextCharacters,
            truncation.ProjectedRetainedTextCharacters);
        Assert.Single(bounded.Assemblies.Assemblies);
        Assert.Equal(1, truncation.ProjectedParticipants);
        Assert.Equal(1, truncation.OmittedParticipants);
    }

    // The budget is spent across participants, so a participant that fits the remaining budget is
    // projected whole and the one that would overflow it is omitted — the projected rows stay
    // inside both bounds instead of overshooting by one image's worth of surface.
    [Fact]
    public void ExecuteBounded_ProjectsWhatFitsAndOmitsTheParticipantThatWouldOverflow()
    {
        byte[] small = BuildBoundedSurfaceImage(typeCount: 2);
        byte[] self = File.ReadAllBytes(SelfPath);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(small),
                        path: null,
                        () => new MemoryStream(small, writable: false),
                        AssemblyResolutionProvenance.Local("small")),
                    policy),
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        SelfPath,
                        AssemblyResolutionProvenance.Local("self")),
                    policy),
            ]);

        const int maxTypes = 3;
        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    maxTypes,
                    1_000_000,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Types, truncation.Limit);
        Assert.Equal(maxTypes, truncation.Bound);
        Assert.Equal(1, truncation.ProjectedParticipants);
        Assert.Equal(1, truncation.OmittedParticipants);
        Assert.False(bounded.IsComplete);

        AssemblyApiSurface surface = Available(bounded.Assemblies);
        Assert.Equal(2, surface.Surface.Types.Count);
        Assert.Equal(truncation.ProjectedTypes, surface.Surface.Types.Count);
        AssertWithinBounds(bounded, maxTypes, maxMembers: 1_000_000);
        Assert.Equal(
            IdentityOf(small).Name,
            Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
                    Assert.Single(bounded.Assemblies.Assemblies))
                .Subject.Identity.Name);
    }

    // The bound applies to members as well as types when the type bound is generous: the small
    // image fits its members, the large one does not, and nothing partial is returned.
    [Fact]
    public void ExecuteBounded_KeepsProjectedMembersWithinTheMemberBound()
    {
        byte[] small = BuildBoundedSurfaceImage(typeCount: 2);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(small),
                        path: null,
                        () => new MemoryStream(small, writable: false),
                        AssemblyResolutionProvenance.Local("small")),
                    policy),
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        SelfPath,
                        AssemblyResolutionProvenance.Local("self")),
                    policy),
            ]);

        const int maxMembers = 4;
        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1_000_000,
                    maxMembers,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Members, truncation.Limit);
        Assert.Equal(maxMembers, truncation.Bound);
        Assert.Equal(1, truncation.OmittedParticipants);
        AssertWithinBounds(bounded, maxTypes: 1_000_000, maxMembers);
    }

    [Fact]
    public void ExecuteBounded_StopsFanOutAndReportsUnprojectedParticipants()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);
        var policy = new TestBindingPolicy();
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        SelfPath,
                        AssemblyResolutionProvenance.Local("first")),
                    policy),
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.Create(
                        IdentityOf(bytes) with { Name = "Second" },
                        path: null,
                        () => new MemoryStream(bytes, writable: false),
                        AssemblyResolutionProvenance.Local("second")),
                    policy),
            ]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    1,
                    1_000_000,
                    1_000_000,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue));

        ApiSurfaceProjectionTruncation truncation = bounded.Truncation!;
        Assert.NotNull(truncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Participants, truncation.Limit);
        Assert.Equal(1, truncation.OmittedParticipants);
        Assert.Single(bounded.Assemblies.Assemblies);
        Assert.False(bounded.IsComplete);
        AssertWithinBounds(
            bounded,
            maxTypes: 1_000_000,
            maxMembers: 1_000_000);
    }

    // Selecting participants is how a host projects one package out of a multi-package workspace
    // without materializing the rest.
    [Fact]
    public void ExecuteBounded_ProjectsOnlyTheSelectedParticipants()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);
        var policy = new TestBindingPolicy();
        int selectedOpens = 0;
        int otherOpens = 0;
        var selected = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                IdentityOf(bytes),
                path: null,
                () =>
                {
                    Interlocked.Increment(ref selectedOpens);
                    return new MemoryStream(bytes, writable: false);
                },
                AssemblyResolutionProvenance.Local("selected")),
            policy);
        var other = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                IdentityOf(bytes) with { Name = "Other" },
                path: null,
                () =>
                {
                    Interlocked.Increment(ref otherOpens);
                    return new MemoryStream(bytes, writable: false);
                },
                AssemblyResolutionProvenance.Local("other")),
            policy);
        using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([selected, other]);

        AssemblyContextApiSurfaceResult bounded =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                ApiSurfaceScope.Public,
                new ApiSurfaceProjectionLimits(
                    64,
                    1_000_000,
                    1_000_000,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue),
                [selected]);

        Assert.Null(bounded.Truncation);
        var available = Assert.IsType<AssemblyContextEntry<AssemblyApiSurface>.Available>(
            Assert.Single(bounded.Assemblies.Assemblies));
        Assert.Same(selected.Assembly.Registration, available.Subject.Registration);
        Assert.Equal(1, selectedOpens);
        Assert.Equal(0, otherOpens);
    }

    [Fact]
    public void ExecuteBounded_RejectsNonPositiveLimits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(0, 1, 1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(1, 0, 1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(1, 1, 0, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(1, 1, 1, -1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(1, 1, 1, 1, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(1, 1, 1, 1, 1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ApiSurfaceProjectionLimits(1, 1, 1, 1, 1, 1, -1));
    }

    [Fact]
    public void BoundedDefinition_IsNotClassifiedUnbounded()
    {
        Assert.Equal(
            InspectionCost.Unbounded,
            AssemblyContextApiSurfaceQuery.Definition.Cost);
        Assert.Equal(
            InspectionCost.NetworkFree,
            AssemblyContextApiSurfaceQuery.BoundedDefinition.Cost);
    }

    static AssemblyContextGroup SelfGroup(InspectionWorkspace workspace)
        => workspace.CreateAssemblyContextGroup(
            [
                new AssemblyContextParticipant(
                    ResolvedAssemblyReference.CreateFromPath(
                        SelfPath,
                        AssemblyResolutionProvenance.Local("api surface tests")),
                    new TestBindingPolicy()),
            ]);

    static AssemblyReferenceIdentity IdentityOf(byte[] bytes)
    {
        using var reader = new PEReader(new MemoryStream(bytes, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
    }

    /// <summary>
    /// The rows a bounded projection actually carries must satisfy the bounds it declared. This
    /// reads the returned surfaces rather than the truncation report, so a report that disagreed
    /// with the projected rows would not satisfy it.
    /// </summary>
    static void AssertWithinBounds(
        AssemblyContextApiSurfaceResult result,
        int maxTypes,
        int maxMembers,
        int maxInspectionFailures = int.MaxValue,
        int maxTypeForwarders = int.MaxValue,
        int maxMetadataRows = int.MaxValue)
    {
        ApiType[] types =
        [
            .. result.Assemblies.Assemblies
                .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
                .SelectMany(entry => entry.Value.Surface.Types),
        ];
        int members = types.Sum(type => type.Members.Count);
        int inspectionFailures = result.Assemblies.Assemblies
            .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
            .Sum(entry => entry.Value.InspectionFailures.Length);
        int typeForwarders = result.Assemblies.Assemblies
            .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
            .Sum(entry => entry.Value.Surface.TypeForwarders.Count);
        Assert.True(
            types.Length <= maxTypes,
            $"projected {types.Length} types over a bound of {maxTypes}");
        Assert.True(
            members <= maxMembers,
            $"projected {members} members over a bound of {maxMembers}");
        Assert.True(
            inspectionFailures <= maxInspectionFailures,
            $"projected {inspectionFailures} failures over a bound of {maxInspectionFailures}");
        Assert.True(
            typeForwarders <= maxTypeForwarders,
            $"projected {typeForwarders} forwarders over a bound of {maxTypeForwarders}");
        if (result.Truncation is { } truncation)
        {
            Assert.Equal(types.Length, truncation.ProjectedTypes);
            Assert.Equal(members, truncation.ProjectedMembers);
            Assert.Equal(
                inspectionFailures,
                truncation.ProjectedInspectionFailures);
            Assert.Equal(typeForwarders, truncation.ProjectedTypeForwarders);
            Assert.True(
                truncation.InspectedMetadataRows <= maxMetadataRows,
                $"inspected {truncation.InspectedMetadataRows} metadata rows "
                    + $"over a bound of {maxMetadataRows}");
        }
    }

    /// <summary>
    /// A synthetic image carrying exactly <paramref name="typeCount"/> public, member-less types,
    /// so a bound can be set between it and a real assembly.
    /// </summary>
    static byte[] BuildBoundedSurfaceImage(
        int typeCount,
        int typeForwarderCount = 0,
        int interfaceCount = 0,
        string assemblyName = "BoundedSurface")
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
        TypeDefinitionHandle firstType = default;
        for (int index = 0; index < typeCount; index++)
        {
            TypeDefinitionHandle type = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Bounded"),
                metadata.GetOrAddString($"Type{index}"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            if (firstType.IsNil)
                firstType = type;
        }
        AssemblyReferenceHandle target = default;
        if (typeForwarderCount > 0 || interfaceCount > 0)
        {
            target = metadata.AddAssemblyReference(
                metadata.GetOrAddString("Forwarder.Target"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        }
        if (typeForwarderCount > 0)
        {
            for (int index = 0; index < typeForwarderCount; index++)
            {
                metadata.AddExportedType(
                    TypeAttributes.Public | (TypeAttributes)0x00200000,
                    metadata.GetOrAddString("Forwarded"),
                    metadata.GetOrAddString($"Type{index}"),
                    target,
                    typeDefinitionId: 0);
            }
        }
        if (interfaceCount > 0)
        {
            TypeReferenceHandle interfaceType = metadata.AddTypeReference(
                    target,
                    metadata.GetOrAddString("Contracts"),
                    metadata.GetOrAddString("IMarker"));
            for (int index = 0; index < interfaceCount; index++)
                metadata.AddInterfaceImplementation(firstType, interfaceType);
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

    static int MetadataRows(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader metadata = reader.GetMetadataReader();
        return Enum.GetValues<TableIndex>().Sum(metadata.GetTableRowCount);
    }

    static int RetainedTextCharacters(byte[] image)
    {
        using var reader = new PEReader(new MemoryStream(image, writable: false));
        var extracted = Assert.IsType<ApiSurfaceExtractionResult.Extracted>(
            ApiSurfaceExtractor.ExtractBounded(
                reader,
                ApiSurfaceExtractionScope.Public,
                new ApiSurfaceExtractionBounds(
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue,
                    int.MaxValue)));
        return extracted.RetainedTextCharacters;
    }

    static TValue Available<TValue>(AssemblyContextResult<TValue> result)
        => Assert.IsType<AssemblyContextEntry<TValue>.Available>(
                Assert.Single(result.Assemblies))
            .Value;

    // Internal (not private) so AssemblyContextApiComparisonQueryTests can reuse this synthetic
    // partial-surface image instead of duplicating this builder.
    internal static byte[] BuildPartialSurfaceImage(int cyclicTypeCount = 1)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("SyntheticSurface.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("SyntheticSurface"),
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
        for (int index = 0; index < cyclicTypeCount; index++)
        {
            TypeDefinitionHandle cyclic = metadata.AddTypeDefinition(
                TypeAttributes.NestedPublic,
                default,
                metadata.GetOrAddString($"Rejected{index}"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            metadata.AddNestedType(cyclic, cyclic);
        }
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("Sibling"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

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
        {
            return new AssemblyBindingSelectionSnapshot(
                Version,
                SelectCore());

            AssemblyBindingSelection SelectCore() =>
                AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                AssemblyBindingFailureKind.CandidateUnavailable));
        }
    }
}

/// <summary>A public probe type with one public and one non-public member.</summary>
public sealed class ApiSurfacePublicProbe
{
    public int Visible => 1;

    internal int HiddenSecret => 2;
}

/// <summary>A non-public probe type reached only by an include-all surface.</summary>
internal sealed class ApiSurfaceInternalProbe
{
    public int Visible => 3;
}

/// <summary>A public probe the default consumer surface deliberately suppresses.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public sealed class ApiSurfaceHiddenPublicProbe
{
    public int Visible => 4;

    private int HiddenSecret => 5;
}
