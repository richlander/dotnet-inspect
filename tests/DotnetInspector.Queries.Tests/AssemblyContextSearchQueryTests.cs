using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class AssemblyContextSearchQueryTests
{
    [Fact]
    public async Task RegistryRun_ProducesAllSearchFacetsFromOneParticipant()
    {
        string path = typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);
        var registry =
            new InspectionQueryRegistry<AssemblyContextGroup>()
                .Add(
                    AssemblyContextExtensionMethodsQuery.Definition,
                    context =>
                        AssemblyContextExtensionMethodsQuery.Execute(
                            context,
                            includeAll: true))
                .Add(
                    AssemblyContextImplementersQuery.Definition,
                    context =>
                        AssemblyContextImplementersQuery.Execute(
                            context,
                            typeof(IWorkspaceQueryMarker).FullName!,
                            includeAll: true))
                .Add(
                    AssemblyContextTypeInventoryQuery.Definition,
                    context =>
                        AssemblyContextTypeInventoryQuery.Execute(
                            context,
                            includeAll: true))
                .Add(
                    AssemblyContextMemberMatchesQuery.Definition,
                    context =>
                        AssemblyContextMemberMatchesQuery.Execute(
                            context,
                            [nameof(WorkspaceQueryImplementation.WorkspaceQueryMember)],
                            includeAll: true));

        InspectionQueryResults results = registry.Run(
            [
                AssemblyContextExtensionMethodsQuery.Definition,
                AssemblyContextImplementersQuery.Definition,
                AssemblyContextTypeInventoryQuery.Definition,
                AssemblyContextMemberMatchesQuery.Definition,
            ],
            group);

        ImmutableArray<ExtensionMethodInfo> extensions =
            Available(
                results.Get(
                    AssemblyContextExtensionMethodsQuery.Definition));
        Assert.Contains(
            extensions,
            method =>
                method.MethodName
                == nameof(WorkspaceQueryExtensions.WorkspaceQueryExtension));

        ImmutableArray<TypeRelationship> implementers =
            Available(
                results.Get(
                    AssemblyContextImplementersQuery.Definition));
        Assert.Contains(
            implementers,
            relationship =>
                relationship.TypeName.Contains(
                    nameof(WorkspaceQueryImplementation),
                    StringComparison.Ordinal));

        AssemblyTypeInventory typeInventory =
            Available(
                results.Get(
                    AssemblyContextTypeInventoryQuery.Definition));
        Assert.Contains(
            typeInventory.Types,
            type =>
                type.FullName
                == typeof(WorkspaceQueryImplementation).FullName);
        Assert.Empty(typeInventory.InspectionFailures);

        AssemblyMemberMatches memberMatches =
            Available(
                results.Get(
                    AssemblyContextMemberMatchesQuery.Definition));
        Assert.Contains(
            memberMatches.Members,
            member =>
                member.MemberName
                == nameof(
                    WorkspaceQueryImplementation.WorkspaceQueryMember));
        Assert.Empty(memberMatches.InspectionFailures);

        Assert.True(group.RetainedImageBytes > 0);
    }

    [Fact]
    public async Task TypeInventory_CarriesRejectedParticipantBesideAvailableResult()
    {
        string path = typeof(WorkspaceQueryImplementation).Assembly.Location;
        byte[] bytes = File.ReadAllBytes(path);
        AssemblyReferenceIdentity actualIdentity;
        using (var reader = new PEReader(
                   new MemoryStream(bytes, writable: false)))
        {
            actualIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    reader.GetMetadataReader());
        }

        var policy = new TestBindingPolicy();
        ResolvedAssemblyReference rejected =
            ResolvedAssemblyReference.Create(
                actualIdentity with { Name = "WrongIdentity" },
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local("rejected"));
        ResolvedAssemblyReference available =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local("available"));
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(rejected, policy),
                    new AssemblyContextParticipant(available, policy),
                ]);

        AssemblyContextResult<AssemblyTypeInventory> result =
            AssemblyContextTypeInventoryQuery.Execute(
                group,
                includeAll: true);

        var rejectedEntry = Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeInventory>.Rejected>(
                    result.Assemblies[0]);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejectedEntry.Failure.Kind);
        Assert.IsType<
            AssemblyContextEntry<
                AssemblyTypeInventory>.Available>(
                    result.Assemblies[1]);
    }

    [Fact]
    public async Task TypeInventory_StopAvoidsLaterParticipants()
    {
        string first =
                    typeof(WorkspaceQueryImplementation).Assembly.Location;
        string second = typeof(string).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
                    CreateGroup(workspace, first, second);
        var entries =
                    new List<AssemblyContextEntry<AssemblyTypeInventory>>();

        bool complete = AssemblyContextTypeInventoryQuery.ExecuteEach(
                    group,
                    includeAll: true,
                    entry => entries.Add(entry),
                    () => entries.Count == 1);

        Assert.False(complete);
        Assert.Single(entries);
    }

    [Fact]
    public async Task TypeInventory_StopAvoidsLaterTypesInParticipant()
    {
        string first =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, first);
        var entries =
            new List<AssemblyContextEntry<AssemblyTypeInventory>>();
        int observedTypes = 0;

        bool complete = AssemblyContextTypeInventoryQuery.ExecuteEach(
            group,
            includeAll: true,
            entry => entries.Add(entry),
            stopAfterType: (_, _) => ++observedTypes == 1);

        Assert.False(complete);
        Assert.Equal(1, observedTypes);
        var available = Assert.IsType<
            AssemblyContextEntry<AssemblyTypeInventory>.Available>(
                Assert.Single(entries));
        Assert.Single(available.Value.Types);
    }

    [Fact]
    public async Task MemberMatches_LimitIsSharedAcrossParticipants()
    {
        string first =
                    typeof(WorkspaceQueryImplementation).Assembly.Location;
        string second = typeof(string).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
                    CreateGroup(workspace, first, second);

        AssemblyContextResult<AssemblyMemberMatches> result =
                    AssemblyContextMemberMatchesQuery.Execute(
                        group,
                        ["*"],
                        includeAll: true,
                        limit: 1);

        AssemblyMemberMatches matches =
                    Assert.IsType<
                        AssemblyContextEntry<
                            AssemblyMemberMatches>.Available>(
                                Assert.Single(result.Assemblies))
                        .Value;
        Assert.Single(matches.Members);
    }

    [Fact]
    public async Task MemberMatches_WindowCrossesParticipants()
    {
        string first =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        string second = typeof(string).Assembly.Location;
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, first, second);

        AssemblyContextResult<AssemblyMemberMatches> result =
            AssemblyContextMemberMatchesQuery.ExecuteWindow(
                group,
                [
                    nameof(
                        WorkspaceQueryImplementation.WorkspaceQueryMember),
                    nameof(string.Concat),
                ],
                includeAll: true,
                window: new MemberSearchWindow(2, 3));

        var available = result.Assemblies
            .OfType<
                AssemblyContextEntry<
                    AssemblyMemberMatches>.Available>()
            .ToArray();
        Assert.Equal(2, available.Length);
        Assert.Empty(available[0].Value.Members);
        Assert.Equal(1, available[0].Value.AcceptedCount);
        Assert.Equal(2, available[1].Value.AcceptedCount);
        Assert.Equal(2, available[1].Value.Members.Length);
        Assert.All(
            available[1].Value.Members,
            member => Assert.Equal(
                nameof(string.Concat),
                member.MemberName));
    }

    [Fact]
    public async Task MemberMatches_StopAvoidsLaterFailingParticipant()
    {
        string first =
            typeof(WorkspaceQueryImplementation).Assembly.Location;
        string second = Path.Combine(
            Path.GetTempPath(),
            $"workspace-query-partial-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(second, BuildPartialSurfaceImage());
        try
        {
            await using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                CreateGroup(workspace, first, second);
            var entries =
                new List<
                    AssemblyContextEntry<AssemblyMemberMatches>>();

            bool complete =
                AssemblyContextMemberMatchesQuery.ExecuteEach(
                    group,
                    [
                        nameof(
                            WorkspaceQueryImplementation
                                .WorkspaceQueryMember),
                    ],
                    includeAll: true,
                    consume: entries.Add,
                    stop: () => entries.Count == 1);

            Assert.False(complete);
            var available = Assert.IsType<
                AssemblyContextEntry<
                    AssemblyMemberMatches>.Available>(
                        Assert.Single(entries));
            Assert.Single(available.Value.Members);
            Assert.Empty(available.Value.InspectionFailures);
        }
        finally
        {
            File.Delete(second);
        }
    }

    [Fact]
    public async Task SurfaceQueries_PreserveHealthyRowsAndInspectionFailures()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"workspace-query-partial-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildPartialSurfaceImage());
        try
        {
            await using var workspace = new InspectionWorkspace();
            using AssemblyContextGroup group =
                CreateGroup(workspace, path);

            AssemblyTypeInventory types = Available(
                AssemblyContextTypeInventoryQuery.Execute(
                    group,
                    includeAll: true));
            Assert.Contains(
                types.Types,
                type => type.TypeName == "Sibling");
            Assert.Single(types.InspectionFailures);

            AssemblyMemberMatches members = Available(
                AssemblyContextMemberMatchesQuery.Execute(
                    group,
                    ["*"],
                    includeAll: true));
            Assert.Empty(members.Members);
            Assert.Single(members.InspectionFailures);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtensionReachability_MatchesPathBasedTraversal()
    {
        string path = typeof(WorkspaceReachabilityRoot).Assembly.Location;
        string target = typeof(WorkspaceReachabilityRoot).FullName!;
        List<(string Type, string Path)> expected =
            ExtensionMethodScanner.FindReachableTypes(
                target,
                [path],
                maxDepth: 2);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            CreateGroup(workspace, path);

        AssemblyContextExtensionReachabilityResult actual =
            AssemblyContextExtensionReachabilityQuery.Execute(
                group,
                target,
                maxDepth: 2);

        Assert.Equal(
            expected,
            actual.ReachableTypes.Select(
                row => (row.Type, row.Path)));
        Assert.True(actual.TypeInventories.IsComplete);
    }

    private static TValue Available<TValue>(
        AssemblyContextResult<TValue> result)
        => Assert.IsType<
                AssemblyContextEntry<TValue>.Available>(
                    Assert.Single(result.Assemblies))
            .Value;

    private static AssemblyContextGroup CreateGroup(
        InspectionWorkspace workspace,
        params string[] paths)
    {
        var policy = new TestBindingPolicy();
        return workspace.CreateAssemblyContextGroup(
            [
                .. paths.Select(path =>
                    new AssemblyContextParticipant(
                        ResolvedAssemblyReference.CreateFromPath(
                            path,
                            AssemblyResolutionProvenance.Local(
                                "query tests")),
                        policy)),
            ]);
    }

    private static byte[] BuildPartialSurfaceImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
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

    private sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
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

public interface IWorkspaceQueryMarker;

public sealed class WorkspaceQueryImplementation :
    IWorkspaceQueryMarker
{
    public WorkspaceReachableType WorkspaceQueryMember() => new();
}

public sealed class WorkspaceReachabilityRoot
{
    public WorkspaceReachableType Reachable { get; } = new();
}

public sealed class WorkspaceReachableType;

public static class WorkspaceQueryExtensions
{
    public static string WorkspaceQueryExtension(
        this WorkspaceReachableType value)
        => value.ToString()!;
}
