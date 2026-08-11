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
    public void PublicWithNonPublicTypes_AddsNonPublicTypesAndKeepsPublicMemberLists()
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

    static TValue Available<TValue>(AssemblyContextResult<TValue> result)
        => Assert.IsType<AssemblyContextEntry<TValue>.Available>(
                Assert.Single(result.Assemblies))
            .Value;

    static byte[] BuildPartialSurfaceImage()
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
        TypeDefinitionHandle cyclic = metadata.AddTypeDefinition(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Rejected"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddNestedType(cyclic, cyclic);
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

        public AssemblyBindingSelection Select(AssemblyBindingRequest request) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable));
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
