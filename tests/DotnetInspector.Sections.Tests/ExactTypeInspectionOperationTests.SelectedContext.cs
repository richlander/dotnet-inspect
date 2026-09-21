using System.Reflection;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    const string HostingPackageId = "Microsoft.Extensions.Hosting";
    const string LoggingPackageId =
        "Microsoft.Extensions.Logging.Abstractions";

    [Fact]
    public async Task SelectedContext_ResolvesTypeFromNonFirstPackageMember()
    {
        const string typeName = "Microsoft.Extensions.Logging.ILogger";
        var store = new InMemoryPackageStore();
        await CommitPackageAsync(
            store,
            HostingPackageId,
            ("lib/net11.0/Microsoft.Extensions.Hosting.dll",
                BuildAssembly(
                    "Microsoft.Extensions.Hosting",
                    "Microsoft.Extensions.Hosting.Host",
                    typeof(IDisposable))));
        await CommitPackageAsync(
            store,
            LoggingPackageId,
            ("lib/net11.0/Microsoft.Extensions.Logging.Abstractions.dll",
                BuildAssembly(
                    "Microsoft.Extensions.Logging.Abstractions",
                    typeName,
                    typeof(IAsyncDisposable))));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = SelectedContextInput(
            HostingPackageId,
            LoggingPackageId);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        (WorkspaceRealizationCandidate candidate,
            WorkspaceDeclarationContext context) =
            await PrepareSelectedContextCandidateAsync(
                coordinator,
                input,
                LoadOptions(client, store));
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);

        InspectionEnvelope<SelectedContextExactTypeInspectionResult> envelope =
            SelectedContextExactTypeInspectionOperation.Execute(
                authority,
                context,
                new SelectedContextExactTypeInspectionRequest(typeName));

        ExactTypeInspectionResult inspection = envelope.Content.Inspection;
        Assert.Equal(
            ExactTypeInspectionOutcome.Available,
            inspection.Outcome);
        Assert.Equal(typeName, inspection.Type?.FullName);
        Assert.Contains(
            typeof(IAsyncDisposable).FullName!,
            inspection.Type!.Interfaces);
        SelectedContextExactTypeSource source =
            Assert.Single(envelope.Content.DefiningSources);
        var package =
            Assert.IsType<
                TypeDeclarationLocatorSectionCoordinate.PackageCoordinate>(
                    source.Library);
        Assert.Equal(
            LoggingPackageId.ToLowerInvariant(),
            package.Package.PackageId);
        Assert.Equal(
            "Microsoft.Extensions.Logging.Abstractions",
            package.LibraryIdentity.Name);
        Assert.Equal(0, source.Observation.ContextOrder);
        Assert.Equal(1, source.Observation.MemberOrder);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
    }

    [Fact]
    public async Task SelectedContext_AmbiguityRetainsEveryDefiningLibrary()
    {
        const string typeName = "Shared.Ambiguous";
        var store = new InMemoryPackageStore();
        await CommitPackageAsync(
            store,
            HostingPackageId,
            ("lib/net11.0/First.dll",
                BuildAssembly(
                    "First",
                    typeName,
                    typeof(IDisposable))));
        await CommitPackageAsync(
            store,
            LoggingPackageId,
            ("lib/net11.0/Second.dll",
                BuildAssembly(
                    "Second",
                    typeName,
                    typeof(IAsyncDisposable))));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = SelectedContextInput(
            HostingPackageId,
            LoggingPackageId);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        (WorkspaceRealizationCandidate candidate,
            WorkspaceDeclarationContext context) =
            await PrepareSelectedContextCandidateAsync(
                coordinator,
                input,
                LoadOptions(client, store));
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);

        InspectionEnvelope<SelectedContextExactTypeInspectionResult> envelope =
            SelectedContextExactTypeInspectionOperation.Execute(
                authority,
                context,
                new SelectedContextExactTypeInspectionRequest(typeName));

        Assert.Equal(
            ExactTypeInspectionOutcome.Ambiguous,
            envelope.Content.Inspection.Outcome);
        Assert.Equal(
            [
                HostingPackageId.ToLowerInvariant(),
                LoggingPackageId.ToLowerInvariant(),
            ],
            envelope.Content.DefiningSources.Select(source =>
                Assert.IsType<
                    TypeDeclarationLocatorSectionCoordinate.PackageCoordinate>(
                        source.Library)
                    .Package.PackageId));
        Assert.Equal(
            [0, 1],
            envelope.Content.DefiningSources.Select(
                source => source.Observation.MemberOrder));
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code == "exact-type.ambiguous");
    }

    [Fact]
    public async Task SelectedContext_AmbiguityRetainsDistinctTypesInOneLibrary()
    {
        var store = new InMemoryPackageStore();
        await CommitPackageAsync(
            store,
            LoggingPackageId,
            ("lib/net11.0/Collisions.dll",
                BuildAssembly(
                    "Collisions",
                    ("First.Widget", typeof(IDisposable)),
                    ("Second.Widget", typeof(IAsyncDisposable)))));
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input =
            SelectedContextInput(LoggingPackageId);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        (WorkspaceRealizationCandidate candidate,
            WorkspaceDeclarationContext context) =
            await PrepareSelectedContextCandidateAsync(
                coordinator,
                input,
                LoadOptions(client, store));
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);

        InspectionEnvelope<SelectedContextExactTypeInspectionResult> envelope =
            SelectedContextExactTypeInspectionOperation.Execute(
                authority,
                context,
                new SelectedContextExactTypeInspectionRequest("Widget"));

        Assert.Equal(
            ExactTypeInspectionOutcome.Ambiguous,
            envelope.Content.Inspection.Outcome);
        Assert.Equal(
            ["First", "Second"],
            envelope.Content.DefiningSources.Select(
                source => source.Type.Namespace));
        Assert.All(
            envelope.Content.DefiningSources,
            source => Assert.Equal(0, source.Observation.MemberOrder));
    }

    [Fact]
    public async Task
        SelectedContext_ForwardedDeclarationAmbiguityNamesTerminalLibrary()
    {
        const string typeNamespace = "Exact";
        const string typeName = "Collision";
        var target = new AssemblyReferenceIdentity(
            "Target",
            new Version(1, 0, 0, 0),
            null,
            null);
        var other = new AssemblyReferenceIdentity(
            "Other",
            new Version(1, 0, 0, 0),
            null,
            null);
        ResolvedAssemblyReference facade = Assembly(
            "Facade",
            BuildMetadataAssembly(
                "Facade",
                Guid.NewGuid(),
                definesType: false,
                typeNamespace,
                typeName,
                target));
        ResolvedAssemblyReference ambiguousTarget = Assembly(
            "Target",
            BuildMetadataAssembly(
                "Target",
                Guid.NewGuid(),
                definesType: true,
                typeNamespace,
                typeName,
                other));
        ResolvedAssemblyReference otherTarget = Assembly(
            "Other",
            BuildMetadataAssembly(
                "Other",
                Guid.NewGuid(),
                definesType: true,
                typeNamespace,
                typeName));
        ResolvedAssemblyReference[] assemblies =
            [facade, ambiguousTarget, otherTarget];
        IAcquisitionFreeAssemblyBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                assemblies.Select(assembly => (
                    assembly,
                    (IAcquisitionFreeAssemblyBindingPolicy)
                        NoResolverAssemblyBindingPolicy.Instance)));
        WorkspaceContextInput input =
            SelectedContextInput(LoggingPackageId);
        WorkspaceMemberCoordinate declared = Assert.Single(input.Members);
        var realized = new RealizedMemberCoordinate.Package(
            LoggingPackageId.ToLowerInvariant(),
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            Framework,
            runtimeIdentifier: null);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    new WorkspacePlan([], [input]),
                    TestContext.Current.CancellationToken))
            .Candidate;
        WorkspaceDeclarationContext context;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            AssemblyContextParticipant[] participants =
            [
                .. assemblies.Select(assembly =>
                    new AssemblyContextParticipant(assembly, policy)),
            ];
            AssemblyContextGroup group =
                construction.Workspace.CreateAssemblyContextGroup(
                    participants);
            var loaded = new WorkspaceContextLoadOutcome.Loaded(
                construction.Workspace.Identity,
                group,
                [
                    .. participants.Select(participant =>
                        new WorkspaceContextMember(
                            declared,
                            realized,
                            participant)),
                ],
                [],
                Framework,
                runtimeIdentifier: null);
            context = construction.Workspace.CompleteDeclarationContext(
                construction.Workspace.BeginDeclarationContext(),
                input,
                loaded);
        }
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);

        InspectionEnvelope<SelectedContextExactTypeInspectionResult> envelope =
            SelectedContextExactTypeInspectionOperation.Execute(
                authority,
                context,
                new SelectedContextExactTypeInspectionRequest(
                    $"{typeNamespace}.{typeName}"));

        Assert.Equal(
            ExactTypeInspectionOutcome.Ambiguous,
            envelope.Content.Inspection.Outcome);
        Assert.Equal(
            ["Target", "Other"],
            envelope.Content.DefiningSources.Select(source =>
                Assert.IsType<
                    TypeDeclarationLocatorSectionCoordinate.PackageCoordinate>(
                        source.Library)
                    .LibraryIdentity.Name));
        Assert.Equal(
            [1, 2],
            envelope.Content.DefiningSources.Select(
                source => source.Observation.MemberOrder));

        static ResolvedAssemblyReference Assembly(
            string name,
            byte[] image) =>
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    name,
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(name));
    }

    [Fact]
    public async Task
        SelectedContext_MissingDefiningCoordinateIsUnavailable()
    {
        const string assemblyName = "CoordinateUnavailable";
        const string typeName = "Exact.CoordinateUnavailable";
        byte[] image = BuildAssembly(
            assemblyName,
            typeName,
            typeof(IDisposable));
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    assemblyName,
                    new Version(0, 0, 0, 0),
                    null,
                    null),
                path: null,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(assemblyName));
        var input = new WorkspaceContextInput
        {
            Members =
            [
                WorkspaceMemberCoordinate.Embedded(
                    "assemblies/CoordinateUnavailable.dll",
                    new string('0', 64),
                    assemblyName),
            ],
        };
        WorkspaceMemberCoordinate declared = Assert.Single(input.Members);
        var realized = new RealizedMemberCoordinate.Embedded(
            "assemblies/CoordinateUnavailable.dll",
            new string('0', 64),
            assemblyName);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    new WorkspacePlan([], [input]),
                    TestContext.Current.CancellationToken))
            .Candidate;
        WorkspaceDeclarationContext context;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            var participant = new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance);
            AssemblyContextGroup group =
                construction.Workspace.CreateAssemblyContextGroup(
                    [participant]);
            var loaded = new WorkspaceContextLoadOutcome.Loaded(
                construction.Workspace.Identity,
                group,
                [new WorkspaceContextMember(
                    declared,
                    realized,
                    participant)],
                [],
                framework: null,
                runtimeIdentifier: null);
            context = construction.Workspace.CompleteDeclarationContext(
                construction.Workspace.BeginDeclarationContext(),
                input,
                loaded);
        }
        Assert.Null(Assert.Single(context.Receipt.Members).Coordinate);
        await ActivateAsync(coordinator, candidate);
        using WorkspaceRealizationOperationLease authority =
            await AdmitAsync(coordinator);

        InspectionEnvelope<SelectedContextExactTypeInspectionResult> envelope =
            SelectedContextExactTypeInspectionOperation.Execute(
                authority,
                context,
                new SelectedContextExactTypeInspectionRequest(typeName));

        ExactTypeInspectionResult inspection = envelope.Content.Inspection;
        Assert.Equal(
            ExactTypeInspectionOutcome.Unavailable,
            inspection.Outcome);
        Assert.Null(inspection.Type);
        Assert.Null(inspection.RequestedAssembly);
        Assert.Null(inspection.SupplierAssembly);
        Assert.Empty(inspection.ForwardingHops);
        Assert.Empty(inspection.Suggestions);
        Assert.Empty(envelope.Content.DefiningSources);
        Assert.Contains(
            inspection.Failures,
            failure => failure.Kind
                == ExactTypeInspectionFailureKind
                    .DefiningSourceUnavailable);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic => diagnostic.Code
                == "exact-type.defining-source-unavailable");
    }

    [Fact]
    public void SelectedContextEnvelope_CarriesNoLiveAuthorityOrContent()
    {
        var visited = new HashSet<Type>();
        Visit(typeof(
            InspectionEnvelope<SelectedContextExactTypeInspectionResult>));

        void Visit(Type type)
        {
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Guid)
                || type == typeof(Version))
            {
                return;
            }

            Assert.False(typeof(Stream).IsAssignableFrom(type));
            Assert.False(typeof(Delegate).IsAssignableFrom(type));
            Assert.False(typeof(InspectionWorkspace).IsAssignableFrom(type));
            Assert.False(
                typeof(WorkspaceRealizationOperationLease)
                    .IsAssignableFrom(type));
            Assert.False(
                typeof(AssemblyContextGroup).IsAssignableFrom(type));
            Assert.False(type.IsByRefLike);
            foreach (Type argument in type.GetGenericArguments())
                Visit(argument);
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.PropertyType != typeof(ResourcePath)
                    && property.Name != "ResourcePathValue")
                {
                    Assert.DoesNotContain(
                        "Path",
                        property.Name,
                        StringComparison.OrdinalIgnoreCase);
                }
                Visit(property.PropertyType);
            }
        }
    }

    static WorkspaceContextInput SelectedContextInput(
        params string[] packageIds) =>
        new()
        {
            Framework = Framework,
            Members =
            [
                .. packageIds.Select(packageId =>
                    WorkspaceMemberCoordinate.Package(
                        packageId,
                        Version)),
            ],
        };

    static async Task CommitPackageAsync(
        InMemoryPackageStore store,
        string packageId,
        params (string EntryPath, byte[] Content)[] entries)
    {
        byte[] package = Archive(entries);
        await store.CommitAsync(
            packageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
    }

    static async Task<(
        WorkspaceRealizationCandidate Candidate,
        WorkspaceDeclarationContext Context)>
        PrepareSelectedContextCandidateAsync(
            WorkspaceReplacementCoordinator coordinator,
            WorkspaceContextInput input,
            WorkspaceContextLoadOptions options)
    {
        WorkspaceRealizationCandidate candidate =
            Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    new WorkspacePlan([], [input]),
                    TestContext.Current.CancellationToken))
            .Candidate;
        WorkspaceDeclarationContext context;
        using (WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction())
        {
            context =
                await WorkspaceContextLoader.LoadDeclarationContextAsync(
                    construction.Workspace,
                    input,
                    options,
                    TestContext.Current.CancellationToken);
        }

        Assert.IsType<WorkspaceContextLoadOutcome.Loaded>(
            context.ContextLoadOutcome);
        return (candidate, context);
    }

    static async Task ActivateAsync(
        WorkspaceReplacementCoordinator coordinator,
        WorkspaceRealizationCandidate candidate)
    {
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await coordinator.CompleteCandidateAsync(
                candidate,
                TestContext.Current.CancellationToken));
        Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
            coordinator.CutOver(candidate));
    }

    static async Task<WorkspaceRealizationOperationLease> AdmitAsync(
        WorkspaceReplacementCoordinator coordinator) =>
        Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
            await coordinator.EnterOperationAsync(
                TestContext.Current.CancellationToken))
        .Lease;
}
