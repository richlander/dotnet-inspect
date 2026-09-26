using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task TypeLocator_ZeroOneManyAndRepeatedRequestsKeepVectors()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Types", metadata =>
            {
                LocatorDefinition(metadata, "N", "Widget");
                LocatorDefinition(metadata, "N", "widget");
            }));
        var exact = new TypeDeclarationLocatorRequest.Exact(LocatorName("N", "Widget"));
        TypeDeclarationLocatorResult.Evaluated result = Locate(
            CaptureDeclarations(workspace, context),
            new TypeDeclarationLocatorRequest.Pattern("Missing"),
            exact,
            new TypeDeclarationLocatorRequest.Pattern("widget"),
            exact);

        Assert.Equal(4, result.Answers.Length);
        Assert.All(result.Answers, answer => Assert.True(answer.IsComplete));
        Assert.Empty(result.Answers[0].Candidates);
        Assert.Single(result.Answers[1].Candidates);
        Assert.Equal(["Widget", "widget"],
            result.Answers[2].Candidates.Select(candidate => candidate.Name.Segments[0]));
        Assert.Same(exact, result.Answers[3].Request);
        Assert.Equal(result.Answers[1].Candidates.Select(candidate => candidate.Name),
            result.Answers[3].Candidates.Select(candidate => candidate.Name));
    }

    [Fact]
    public async Task TypeLocator_ExactNestingAndPatternArityUseMetadataSemantics()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Nested", metadata =>
            {
                var outer = LocatorDefinition(metadata, "N", "Outer`1");
                foreach (string innerName in new[] { "Inner`2", "Inner`3" })
                {
                    var inner = LocatorDefinition(metadata, "", innerName, TypeAttributes.NestedPublic);
                    metadata.AddNestedType(inner, outer);
                }
                LocatorDefinition(metadata, "N.Outer`1", "Inner`2");
            }));
        MetadataTypeDefinitionName nested = LocatorName("N", "Outer`1", "Inner`2");
        TypeDeclarationLocatorResult.Evaluated result = Locate(
            CaptureDeclarations(workspace, context),
            new TypeDeclarationLocatorRequest.Exact(nested),
            new TypeDeclarationLocatorRequest.Pattern("Outer<T>.Inner<T,U>"),
            new TypeDeclarationLocatorRequest.Pattern("Inner<T,U,V>"),
            new TypeDeclarationLocatorRequest.Pattern("Outer<T>.Inn?r<T,U>"),
            new TypeDeclarationLocatorRequest.Exact(LocatorName("n", "Outer`1", "Inner`2")));

        Assert.Equal(nested, Assert.Single(result.Answers[0].Candidates).Name);
        Assert.Equal(2, result.Answers[1].Candidates.Length);
        Assert.Equal(["N", "N.Outer`1"], result.Answers[1].Candidates.Select(candidate => candidate.Name.Namespace));
        Assert.Equal("Inner`3", Assert.Single(result.Answers[2].Candidates).Name.Segments[^1]);
        Assert.Equal(2, result.Answers[3].Candidates.Length);
        Assert.Empty(result.Answers[4].Candidates);
    }

    [Fact]
    public async Task TypeLocator_NamespaceRequestsUseTypedNamespaceSemantics()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            LocatorImage(
                "Namespaces",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Widget");
                    LocatorDefinition(metadata, "N.Child", "Nested");
                    LocatorDefinition(metadata, "N2", "Other");
                }));
        TypeDeclarationLocatorResult.Evaluated result = Locate(
            CaptureDeclarations(workspace, context),
            new TypeDeclarationLocatorRequest.Namespace(
                "N",
                MetadataNamespaceMatch.Exact),
            new TypeDeclarationLocatorRequest.Namespace(
                "N",
                MetadataNamespaceMatch.ExactOrDescendant),
            new TypeDeclarationLocatorRequest.Namespace(
                ".Child",
                MetadataNamespaceMatch.Suffix));

        Assert.Equal(
            ["N.Widget"],
            result.Answers[0].Candidates.Select(
                candidate => candidate.Name.ToMetadataFullName()));
        Assert.Equal(
            ["N.Widget", "N.Child.Nested"],
            result.Answers[1].Candidates.Select(
                candidate => candidate.Name.ToMetadataFullName()));
        Assert.Equal(
            ["N.Child.Nested"],
            result.Answers[2].Candidates.Select(
                candidate => candidate.Name.ToMetadataFullName()));
    }

    [Fact]
    public async Task TypeLocator_PublicAndAllAreDistinctWithoutFallbackSearches()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Visibility", metadata =>
            {
                LocatorDefinition(metadata, "N", "Public");
                LocatorDefinition(metadata, "N", "Internal", TypeAttributes.NotPublic);
            }));
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, context);
        TypeDeclarationLocatorResult.Evaluated visible = Locate(population,
            new TypeDeclarationLocatorRequest.Pattern("*"),
            new TypeDeclarationLocatorRequest.Pattern("N"),
            new TypeDeclarationLocatorRequest.Pattern("Publi"));
        Assert.Equal("Public", Assert.Single(visible.Answers[0].Candidates).Name.Segments[0]);
        Assert.Empty(visible.Answers[1].Candidates);
        Assert.Empty(visible.Answers[2].Candidates);
        var all = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            TypeDeclarationLocatorQuery.Execute(
                population, [new TypeDeclarationLocatorRequest.Pattern("*")],
                includeAll: true, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(["Internal", "Public"],
            all.Answers[0].Candidates.Select(candidate => candidate.Name.Segments[0]));
        Assert.Equal(
            [1, 0],
            all.Answers[0].Candidates.Select(
                candidate => candidate.DeclarationOrder));
    }

    [Fact]
    public async Task TypeLocator_EqualCoordinatesKeepFeedAndTargetObservations()
    {
        byte[] image = LocatorImage("Same", metadata => LocatorDefinition(metadata, "N", "Widget"));
        byte[] archive = Archive(("lib/net10.0/Same.dll", image), ("lib/net9.0/Same.dll", image));
        var feedA = new PackageSource("same-display", FeedA.Url);
        var feedB = new PackageSource("same-display", FeedB.Url);
        var store = new InMemoryPackageStore();
        foreach (PackageSource source in new[] { feedA, feedB })
        {
            await store.CommitAsync(PackageId, Version, Producer(source),
                new MemoryStream(archive), TestContext.Current.CancellationToken);
        }
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        var contexts = new List<WorkspaceDeclarationContext>();
        foreach (var (source, framework) in new[] { (feedA, "net10.0"), (feedB, "net10.0"), (feedA, "net9.0") })
        {
            contexts.Add(await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace, new() { Framework = framework, Members = [PackageMember(Version)] },
                Options(client, store, sourceAuthorization: new UniformPackageSourceAuthorization([source])),
                TestContext.Current.CancellationToken));
        }
        WorkspaceDeclarationPopulation p1 = CaptureDeclarations(workspace, contexts[0]);
        WorkspaceDeclarationPopulation p2 = CaptureDeclarations(workspace, [.. contexts.AsEnumerable().Reverse()]);
        TypeDeclarationLocatorResult.Evaluated result = Locate(p2, new TypeDeclarationLocatorRequest.Pattern("Widget"));
        var candidates = result.Answers[0].Candidates;
        Assert.Equal(3, candidates.Length);
        Assert.All(candidates, candidate => Assert.Equal(candidates[0].Coordinate, candidate.Coordinate));
        Assert.Equal(3, candidates.Select(candidate => candidate.Observation.Occurrence).Distinct().Count());
        Assert.Equal(
            new (string, string?)[] { (Producer(FeedA), "net10.0"), (Producer(FeedB), "net10.0"), (Producer(FeedA), "net9.0") },
            candidates.Select(candidate =>
            {
                var origin = Assert.IsType<RealizedMemberCoordinate.Package>(ContextOrigin(candidate.Observation).Realized);
                return (origin.Producer, origin.Framework);
            }));
        var permuted = Locate(CaptureDeclarations(workspace, [.. contexts]),
            new TypeDeclarationLocatorRequest.Pattern("Widget"));
        Assert.Equal(candidates.Select(candidate => candidate.Observation.Occurrence),
            permuted.Answers[0].Candidates.Select(candidate => candidate.Observation.Occurrence));
        Assert.Single(Locate(p1, new TypeDeclarationLocatorRequest.Pattern("Widget")).Answers[0].Candidates);
    }

    [Fact]
    public async Task TypeLocator_SourceAndAssemblyOrderingDoNotSelectDefinitionsOverForwarders()
    {
        byte[] target = LocatorImage("Z.Target", metadata => LocatorDefinition(metadata, "N", "Widget"));
        byte[] facade = LocatorImage("A.Facade", metadata =>
        {
            var reference = metadata.AddAssemblyReference(
                metadata.GetOrAddString("Z.Target"), new Version(99, 0, 0, 0), default, default, 0, default);
            metadata.AddExportedType((TypeAttributes)0x00200000,
                metadata.GetOrAddString("N"), metadata.GetOrAddString("Widget"), reference, 0);
        });
        await using var workspace = new InspectionWorkspace();
        var store = new InMemoryPackageStore();
        await store.CommitAsync(RuntimePackPackageId, RuntimePackVersion, Producer(NuGetOrg),
            new MemoryStream(Archive(($"runtimes/linux-x64/lib/{Framework}/Z.Target.dll", target))),
            TestContext.Current.CancellationToken);
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext platform = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new() { Framework = Framework, Members = [WorkspaceMemberCoordinate.Platform("runtime", version: RuntimePackVersion)] },
            Options(client, store), TestContext.Current.CancellationToken);
        WorkspaceDeclarationContext package = await LocatorContext(workspace, target, facade);
        var result = Locate(CaptureDeclarations(workspace, platform, package),
            new TypeDeclarationLocatorRequest.Exact(LocatorName("N", "Widget")));
        var candidates = result.Answers[0].Candidates;
        Assert.True(result.Answers[0].IsComplete);
        Assert.Equal(3, candidates.Length);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(candidates[0].Coordinate);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(candidates[1].Coordinate);
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(candidates[2].Coordinate);
        Assert.Equal("A.Facade", candidates[0].Coordinate.LibraryIdentity.Identity.Name);
        Assert.Equal(AssemblyTypeDeclarationKind.Forwarder, candidates[0].Kind);
        Assert.Equal(AssemblyTypeDeclarationKind.Definition, candidates[1].Kind);
    }

    [Theory]
    [InlineData("neutral", null)]
    [InlineData("NEUTRAL", "")]
    public async Task TypeLocator_EquivalentAssemblyCulturesUseOccurrenceOrder(string firstCulture, string? secondCulture)
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext first = await LocatorContext(workspace,
            LocatorImage("Same", metadata => LocatorDefinition(metadata, "N", "Widget"), firstCulture));
        WorkspaceDeclarationContext second = await LocatorContext(workspace,
            LocatorImage("Same", metadata => LocatorDefinition(metadata, "N", "Widget"), secondCulture));
        var result = Locate(CaptureDeclarations(workspace, second, first),
            new TypeDeclarationLocatorRequest.Pattern("Widget"));
        Assert.Equal(result.Answers[0].Candidates[0].Coordinate, result.Answers[0].Candidates[1].Coordinate);
        Assert.Same(first.Receipt.Members[0].Occurrence, result.Answers[0].Candidates[0].Observation.Occurrence);
        Assert.Same(second.Receipt.Members[0].Occurrence, result.Answers[0].Candidates[1].Observation.Occurrence);
    }

    [Fact]
    public async Task TypeLocator_ModuleExportsAndRejectedInventoriesRemainAttributed()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Healthy", metadata =>
            {
                LocatorDefinition(metadata, "N", "Widget");
                var file = metadata.AddAssemblyFile(metadata.GetOrAddString("Other.netmodule"), default, true);
                metadata.AddExportedType(TypeAttributes.Public,
                    metadata.GetOrAddString("N"), metadata.GetOrAddString("Export"), file, 1);
            }),
            LocatorImage("Broken", metadata =>
            {
                LocatorDefinition(metadata, "N", "Duplicate");
                LocatorDefinition(metadata, "N", "Duplicate");
            }));
        var result = Locate(CaptureDeclarations(workspace, context),
            new TypeDeclarationLocatorRequest.Pattern("*"));
        Assert.Single(result.Answers[0].Candidates);
        Assert.True(result.Answers[0].IsRealizationComplete);
        Assert.False(result.Answers[0].IsEvaluationComplete);
        var searched = Assert.Single(result.Members.OfType<TypeDeclarationLocatorMemberOutcome.Searched>());
        Assert.Equal(AssemblyTypeDeclarationKind.ModuleExport, Assert.Single(searched.UnsupportedDeclarations).Kind);
        var rejected = Assert.Single(result.Members.OfType<TypeDeclarationLocatorMemberOutcome.InventoryRejected>());
        Assert.Equal(CandidateOpenFailureKind.InvalidImage, rejected.Failure.Kind);
        Assert.Equal("Broken", rejected.Member.AssemblyIdentity.Name);
    }

    [Fact]
    public async Task TypeLocator_UpstreamFailuresDifferFromCompleteEmptyPopulations()
    {
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext failed = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new(), Options(client, new InMemoryPackageStore()), TestContext.Current.CancellationToken);
        var empty = Locate(CaptureDeclarations(workspace), new TypeDeclarationLocatorRequest.Pattern("*"));
        var failedEmpty = Locate(CaptureDeclarations(workspace, failed), new TypeDeclarationLocatorRequest.Pattern("*"));
        Assert.Empty(empty.Answers[0].Candidates);
        Assert.True(empty.Answers[0].IsComplete);
        Assert.Empty(failedEmpty.Answers[0].Candidates);
        Assert.False(failedEmpty.Answers[0].IsRealizationComplete);
        Assert.True(failedEmpty.Answers[0].IsEvaluationComplete);
        Assert.Equal(WorkspaceContextLoadFailureKind.EmptyContext,
            Assert.IsType<WorkspaceDeclarationFailure.ContextLoad>(
                Assert.Single(Assert.Single(failedEmpty.Population.Contexts).Failures)).Failure.Kind);
        WorkspaceDeclarationContext healthy = await LocatorContext(workspace,
            LocatorImage("Healthy", metadata => LocatorDefinition(metadata, "N", "Widget")));
        var mixed = Locate(CaptureDeclarations(workspace, healthy, failed),
            new TypeDeclarationLocatorRequest.Pattern("Widget"));
        Assert.Single(mixed.Answers[0].Candidates);
        Assert.False(mixed.Answers[0].IsComplete);
    }

    [Fact]
    public async Task TypeLocator_UnsupportedCoordinateIsNotDroppedOrScanned()
    {
        await using var workspace = new InspectionWorkspace();
        byte[] image = File.ReadAllBytes(EmbeddedPath);
        var provider = new StubEmbeddedContent(image);
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext context = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Members = [EmbeddedMember(image)] },
            Options(client, new InMemoryPackageStore(), provider), TestContext.Current.CancellationToken);
        int opens = provider.OpenCount;
        var result = Locate(CaptureDeclarations(workspace, context), new TypeDeclarationLocatorRequest.Pattern("*"));
        Assert.IsType<TypeDeclarationLocatorMemberOutcome.CoordinateUnavailable>(Assert.Single(result.Members));
        Assert.Empty(result.Answers[0].Candidates);
        Assert.False(result.Answers[0].IsComplete);
        Assert.Equal(opens, provider.OpenCount);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(1, 1, false)]
    [InlineData(2, 2, true)]
    public async Task TypeLocator_WorkBoundNeverCertifiesAbsenceOrUniqueness(int limit, int count, bool complete)
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("First", metadata => LocatorDefinition(metadata, "N", "Widget")),
            LocatorImage("Second", metadata => LocatorDefinition(metadata, "N", "Widget")));
        var result = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            TypeDeclarationLocatorQuery.Execute(CaptureDeclarations(workspace, context),
                [new TypeDeclarationLocatorRequest.Pattern("Widget"), new TypeDeclarationLocatorRequest.Pattern("Missing")],
                maxInventoryReads: limit, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(count, result.Answers[0].Candidates.Length);
        Assert.Empty(result.Answers[1].Candidates);
        Assert.All(result.Answers, answer => Assert.Equal(complete, answer.IsComplete));
        Assert.Equal(2 - limit, result.Members.OfType<TypeDeclarationLocatorMemberOutcome.NotEvaluated>().Count());
        Assert.Equal(limit, result.MaxInventoryReads);
    }

    [Fact]
    public async Task TypeLocator_RequestValidationPrecedesReadsAndCancellationPropagates()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace);
        Assert.Equal(TypeDeclarationLocatorRejectionKind.EmptyRequests,
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
                TypeDeclarationLocatorQuery.Execute(population, [], cancellationToken: TestContext.Current.CancellationToken)).Kind);
        var invalid = Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
            TypeDeclarationLocatorQuery.Execute(population,
                [new TypeDeclarationLocatorRequest.Pattern("Widget"), new TypeDeclarationLocatorRequest.Pattern(" ")],
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(TypeDeclarationLocatorRejectionKind.InvalidRequest, invalid.Kind);
        Assert.Equal(1, invalid.RequestIndex);
        var invalidNamespace =
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
                TypeDeclarationLocatorQuery.Execute(
                    population,
                    [
                        new TypeDeclarationLocatorRequest.Namespace(
                            " ",
                            MetadataNamespaceMatch.Exact),
                    ],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.InvalidRequest,
            invalidNamespace.Kind);
        Assert.Equal(0, invalidNamespace.RequestIndex);
        var invalidNamespaceMatch =
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
                TypeDeclarationLocatorQuery.Execute(
                    population,
                    [
                        new TypeDeclarationLocatorRequest.Namespace(
                            "N",
                            (MetadataNamespaceMatch)int.MaxValue),
                    ],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.InvalidRequest,
            invalidNamespaceMatch.Kind);
        Assert.Equal(0, invalidNamespaceMatch.RequestIndex);
        var invalidNamespaceSuffix =
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
                TypeDeclarationLocatorQuery.Execute(
                    population,
                    [
                        new TypeDeclarationLocatorRequest.Namespace(
                            "N",
                            MetadataNamespaceMatch.Suffix),
                    ],
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        Assert.Equal(
            TypeDeclarationLocatorRejectionKind.InvalidRequest,
            invalidNamespaceSuffix.Kind);
        Assert.Equal(0, invalidNamespaceSuffix.RequestIndex);
        Assert.Equal(TypeDeclarationLocatorRejectionKind.InvalidInventoryReadLimit,
            Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
                TypeDeclarationLocatorQuery.Execute(population, [new TypeDeclarationLocatorRequest.Pattern("*")],
                    maxInventoryReads: -1, cancellationToken: TestContext.Current.CancellationToken)).Kind);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.Throws<OperationCanceledException>(() => TypeDeclarationLocatorQuery.Execute(
            population, [new TypeDeclarationLocatorRequest.Pattern("*")], cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task TypeLocator_DetachedAnswersSurviveCloseAndReleasedMembersRemainVisible()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(workspace,
            LocatorImage("Healthy", metadata => LocatorDefinition(metadata, "N", "Widget")));
        WorkspaceDeclarationPopulation population = CaptureDeclarations(workspace, context);
        var result = Locate(population, new TypeDeclarationLocatorRequest.Pattern("*"));
        ContextLoaded(context).Group.Dispose();
        var released = Locate(population, new TypeDeclarationLocatorRequest.Pattern("*"));
        Assert.Equal(WorkspaceDeclarationPopulationFailure.ContextUnavailable,
            Assert.IsType<TypeDeclarationLocatorMemberOutcome.Unavailable>(Assert.Single(released.Members)).Failure);
        Assert.False(released.Answers[0].IsComplete);
        await workspace.CloseAsync();
        Assert.Equal(LocatorName("N", "Widget"), Assert.Single(result.Answers[0].Candidates).Name);
        var rejected = Assert.IsType<TypeDeclarationLocatorResult.Rejected>(
            TypeDeclarationLocatorQuery.Execute(population, [new TypeDeclarationLocatorRequest.Pattern("*")],
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(TypeDeclarationLocatorRejectionKind.PopulationUnavailable, rejected.Kind);
        Assert.Equal(WorkspaceDeclarationPopulationFailure.WorkspaceClosed, rejected.PopulationFailure);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TypeLocator_RealJsonChoicesAndRuntimeObjectForwarderRemainDistinct()
    {
        await using var workspace = new InspectionWorkspace();
        using var client = new HttpClient();
        var options = Options(client, new InMemoryPackageStore());
        WorkspaceDeclarationContext package = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new() { Framework = Framework, Members = [WorkspaceMemberCoordinate.Package("System.Text.Json", "10.0.0")] },
            options, TestContext.Current.CancellationToken);
        WorkspaceDeclarationContext platform = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new()
            {
                Framework = Framework,
                Members =
                [
                    WorkspaceMemberCoordinate.Platform("runtime", "System.Text.Json", "10.0.10"),
                    WorkspaceMemberCoordinate.Platform("runtime", "netstandard", "10.0.10"),
                    WorkspaceMemberCoordinate.Platform("runtime", "System.Private.CoreLib", "10.0.10"),
                ],
            }, options, TestContext.Current.CancellationToken);
        _ = ContextLoaded(package);
        _ = ContextLoaded(platform);
        var result = Locate(CaptureDeclarations(workspace, platform, package),
            new TypeDeclarationLocatorRequest.Exact(LocatorName("System.Text.Json", "JsonSerializer")),
            new TypeDeclarationLocatorRequest.Exact(LocatorName("System", "Object")));
        Assert.All(result.Answers, answer => Assert.True(answer.IsComplete));
        Assert.Equal(2, result.Answers[0].Candidates.Length);
        Assert.IsType<ExactLibrarySourceCoordinate.Package>(result.Answers[0].Candidates[0].Coordinate);
        Assert.IsType<ExactLibrarySourceCoordinate.Platform>(result.Answers[0].Candidates[1].Coordinate);
        Assert.Equal(2, result.Answers[1].Candidates.Length);
        Assert.Collection(result.Answers[1].Candidates,
            forwarder =>
            {
                Assert.Equal("netstandard", forwarder.Coordinate.LibraryIdentity.Identity.Name);
                Assert.Equal(AssemblyTypeDeclarationKind.Forwarder, forwarder.Kind);
            },
            definition =>
            {
                Assert.Equal("System.Private.CoreLib", definition.Coordinate.LibraryIdentity.Identity.Name);
                Assert.Equal(AssemblyTypeDeclarationKind.Definition, definition.Kind);
                Assert.Equal(
                    AssemblyTypeDefinitionKind.Class,
                    definition.DefinitionKind);
                Assert.True(definition.IsDefinitionPublic);
            });
    }

    static async Task<WorkspaceDeclarationContext> LocatorContext(
        InspectionWorkspace workspace, params byte[][] images)
    {
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext context = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace, new() { Framework = Framework, Members = [PackageMember(Version)] },
            Options(client, await CachedStoreAsync(Version,
                Archive([.. images.Select((image, index) => ($"lib/{Framework}/part{index}.dll", image))]))),
            TestContext.Current.CancellationToken);
        _ = ContextLoaded(context);
        return context;
    }

    static TypeDeclarationLocatorResult.Evaluated Locate(
        WorkspaceDeclarationPopulation population, params TypeDeclarationLocatorRequest[] requests) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(TypeDeclarationLocatorQuery.Execute(
            population, [.. requests], cancellationToken: TestContext.Current.CancellationToken));

    static MetadataTypeDefinitionName LocatorName(string ns, params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(MetadataTypeDefinitionName.Create(ns, [.. segments])).Name;

    static TypeDefinitionHandle LocatorDefinition(
        MetadataBuilder metadata, string ns, string name, TypeAttributes attributes = TypeAttributes.Public) =>
        metadata.AddTypeDefinition(attributes, metadata.GetOrAddString(ns), metadata.GetOrAddString(name),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

    static byte[] LocatorImage(string assemblyName, Action<MetadataBuilder> addDeclarations, string? culture = null)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString($"{assemblyName}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString(assemblyName), new Version(1, 0, 0, 0),
            metadata.GetOrAddString(culture ?? ""), default, 0, 0);
        LocatorDefinition(metadata, "", "<Module>", TypeAttributes.NotPublic);
        addDeclarations(metadata);
        var builder = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true), new BlobBuilder(), flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
