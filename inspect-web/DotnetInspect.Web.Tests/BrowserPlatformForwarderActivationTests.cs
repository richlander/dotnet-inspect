using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspect.Web.Core;
using DotnetInspector.Libraries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using InertText;
using Inspector.Artifacts;

namespace DotnetInspect.Web.Tests;

public sealed class BrowserPlatformForwarderActivationTests
{
    [Fact]
    public async Task RealFacadeActionsAdvanceOneExactHopAtATime()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = Target();
        PlatformSourcePlan sources = Sources();
        LibraryTypeShape selected = await ReadForwarderAsync(
                target,
                sources,
                "System.Xml",
                XmlReaderName(),
                cancellationToken);
        using var activation =
            new BrowserPlatformForwarderActivation(
                (request, token) => ResolveAsync(request, token));

        BrowserPlatformForwarderResultAuthority firstAuthority =
            Authority(
                activation,
                generation: 1,
                target,
                sources,
                Identity("System.Xml"),
                selected.Forwarding!.SourceModuleVersionId);
        BrowserPlatformForwarderAction firstAction =
            Assert.IsType<
                BrowserPlatformForwarderActionPublication.Published>(
                    activation.Publish(firstAuthority, selected)).Action;
        var first = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Activated>(
                await activation.ActivateAsync(
                    firstAction,
                    cancellationToken));
        var intermediate = Assert.IsType<
            BrowserPlatformForwarderDestination.Forwarder>(
                first.Destination);

        Assert.Equal(
            "System.Xml.ReaderWriter",
            intermediate.Library.Identity.Name);
        Assert.Equal(
            "System.Private.Xml",
            intermediate.TargetAssembly.Name);
        Assert.Same(sources, first.Sources);
        Assert.Equal(2, first.Resolution.Hops.Length);

        BrowserPlatformForwarderResultAuthority secondAuthority =
            Authority(
                activation,
                generation: 2,
                target,
                sources,
                intermediate.Library.Identity,
                Assert.IsType<Guid>(
                    intermediate.Library.ModuleVersionId));
        BrowserPlatformForwarderAction secondAction =
            Assert.IsType<
                BrowserPlatformForwarderActionPublication.Published>(
                    activation.Publish(
                        secondAuthority,
                        Shape(intermediate))).Action;
        var second = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Activated>(
                await activation.ActivateAsync(
                    secondAction,
                    cancellationToken));
        var definition = Assert.IsType<
            BrowserPlatformForwarderDestination.Definition>(
                second.Destination);

        Assert.Equal(
            "System.Private.Xml",
            definition.Value.Assembly.Assembly.Identity.Name);
        Assert.Equal(XmlReaderName(), definition.Value.Type);
        Assert.Single(second.Resolution.Hops);
    }

    [Fact]
    public async Task PublicationAndExecutionRejectInexactEvidence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = Target();
        PlatformSourcePlan sources = Sources();
        PlatformTypeDefinitionResolutionResult.Resolved route =
            await ResolveRouteAsync(
                target,
                sources,
                "System.Xml",
                XmlReaderName(),
                cancellationToken);
        PlatformTypeForwardingHopEvidence hop = route.Hops[0];
        using var activation =
            new BrowserPlatformForwarderActivation(
                (request, token) => ResolveAsync(request, token));
        BrowserPlatformForwarderResultAuthority authority =
            Authority(
                activation,
                generation: 1,
                target,
                sources,
                hop.SourceAssembly.Assembly.Identity,
                Assert.IsType<Guid>(
                    hop.SourceAssembly.Assembly.ModuleVersionId));

        var definition = new LibraryTypeShape(
            XmlReaderName(),
            Text("XmlReader"),
            Text("System.Xml"),
            LibraryTypeDeclarationKind.Definition,
            ApiTypeInventoryKind.Class,
            LibraryTypeDefinitionAccessibility.Public,
            isPublicSurface: true,
            forwarding: null,
            memberCount: null);
        Assert.IsType<
            BrowserPlatformForwarderActionPublication.NotApplicable>(
                activation.Publish(authority, definition));

        LibraryTypeShape forwarder = Shape(hop, XmlReaderName());
        LibraryTypeForwardingEvidence evidence = forwarder.Forwarding!;
        LibraryTypeShape wrongMvid = Shape(
            forwarder.Identity,
            new(
                Guid.NewGuid(),
                evidence.Declarations,
                evidence.TargetAssembly));
        Assert.Equal(
            BrowserPlatformForwarderPublicationRefusal
                .SourceModuleVersionIdMismatch,
            Assert.IsType<
                    BrowserPlatformForwarderActionPublication.Refused>(
                    activation.Publish(authority, wrongMvid))
                .Reason);

        LibraryTypeShape wrongTarget = Shape(
            forwarder.Identity,
            new(
                evidence.SourceModuleVersionId,
                evidence.Declarations,
                new(
                    Text("System.Xml.Wrong"),
                    evidence.TargetAssembly.Version,
                    evidence.TargetAssembly.Culture,
                    evidence.TargetAssembly.PublicKeyToken)));
        BrowserPlatformForwarderAction action =
            Assert.IsType<
                BrowserPlatformForwarderActionPublication.Published>(
                    activation.Publish(authority, wrongTarget)).Action;
        var blocked = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Blocked>(
                await activation.ActivateAsync(action, cancellationToken));
        var refused = Assert.IsType<
            BrowserPlatformForwarderActivationBlock.Refused>(
                blocked.Block);

        Assert.Equal(
            BrowserPlatformForwarderRefusedReason.EvidenceMismatch,
            refused.Reason);
        Assert.NotNull(refused.Resolution);

        using var other =
            new BrowserPlatformForwarderActivation(
                (request, token) => ResolveAsync(request, token));
        var foreign = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Blocked>(
                await other.ActivateAsync(action, cancellationToken));
        Assert.Equal(
            BrowserPlatformForwarderRefusedReason.ForeignAction,
            Assert.IsType<
                    BrowserPlatformForwarderActivationBlock.Refused>(
                    foreign.Block)
                .Reason);
    }

    [Fact]
    public async Task SupersededAndCanceledActionsInstallNoDestination()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = Target();
        PlatformSourcePlan sources = Sources();
        PlatformTypeDefinitionResolutionResult.Resolved route =
            await ResolveRouteAsync(
                target,
                sources,
                "System.Xml",
                XmlReaderName(),
                cancellationToken);
        PlatformTypeForwardingHopEvidence hop = route.Hops[0];
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var activation =
            new BrowserPlatformForwarderActivation(
                async (request, token) =>
                {
                    await release.Task.WaitAsync(token);
                    return await ResolveAsync(request, token);
                });
        BrowserPlatformForwarderAction action = Publish(
            activation,
            generation: 1,
            target,
            sources,
            hop,
            XmlReaderName());

        ValueTask<BrowserPlatformForwarderActivationResult> pending =
            activation.ActivateAsync(action, cancellationToken);
        activation.BeginResult(
            generation: 2,
            target,
            "linux-x64",
            sources,
            hop.SourceAssembly.Assembly.Identity,
            Assert.IsType<Guid>(
                hop.SourceAssembly.Assembly.ModuleVersionId));
        release.SetResult();
        var stale = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Blocked>(
                await pending);
        var staleBlock = Assert.IsType<
            BrowserPlatformForwarderActivationBlock.Stale>(
                stale.Block);
        Assert.NotNull(staleBlock.Receipt);
        var staleRoute = Assert.IsType<
            PlatformTypeDefinitionResolutionResult.Resolved>(
                staleBlock.Resolution);
        Assert.Collection(
            staleRoute.Hops,
            first => Assert.Equal(
                "System.Xml.ReaderWriter",
                first.TargetReference.Name),
            second => Assert.Equal(
                "System.Private.Xml",
                second.TargetReference.Name));

        BrowserPlatformForwarderAction canceledAction = Publish(
            activation,
            generation: 3,
            target,
            sources,
            hop,
            XmlReaderName());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Blocked>(
                await activation.ActivateAsync(
                    canceledAction,
                    cancellation.Token));
        Assert.IsType<
            BrowserPlatformForwarderActivationBlock.Canceled>(
                canceled.Block);
    }

    [Fact]
    public async Task HopBoundRetainsTheCompletedRouteEvidence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = Target();
        PlatformSourcePlan sources = Sources();
        PlatformTypeDefinitionResolutionResult.Resolved route =
            await ResolveRouteAsync(
                target,
                sources,
                "System.Xml",
                XmlReaderName(),
                cancellationToken);
        PlatformTypeForwardingHopEvidence hop = route.Hops[0];
        using var activation =
            new BrowserPlatformForwarderActivation(
                (request, token) => ResolveAsync(
                    request,
                    token,
                    maxForwardingHops: 1));
        BrowserPlatformForwarderAction action = Publish(
            activation,
            generation: 1,
            target,
            sources,
            hop,
            XmlReaderName());

        var blocked = Assert.IsType<
            BrowserPlatformForwarderActivationResult.Blocked>(
                await activation.ActivateAsync(
                    action,
                    cancellationToken));
        var incomplete = Assert.IsType<
            BrowserPlatformForwarderActivationBlock.Incomplete>(
                blocked.Block);
        var rejected = Assert.IsType<
            PlatformTypeDefinitionResolutionResult.Rejected>(
                incomplete.Resolution);

        Assert.IsType<
            PlatformTypeResolutionFailureEvidence.HopBudgetExceeded>(
                rejected.Failure);
        Assert.NotEmpty(rejected.Hops);
        Assert.Equal(
            "System.Xml.ReaderWriter",
            rejected.Hops[0].TargetReference.Name);
    }

    [Fact]
    public async Task ActivatedResultGraphIsResourceFree()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = Target();
        PlatformSourcePlan sources = Sources();
        PlatformTypeDefinitionResolutionResult.Resolved route =
            await ResolveRouteAsync(
                target,
                sources,
                "System.Xml",
                XmlReaderName(),
                cancellationToken);
        using var activation =
            new BrowserPlatformForwarderActivation(
                (request, token) => ResolveAsync(request, token));
        BrowserPlatformForwarderAction action = Publish(
            activation,
            generation: 1,
            target,
            sources,
            route.Hops[0],
            XmlReaderName());
        object result = await activation.ActivateAsync(
            action,
            cancellationToken);

        var visited = new HashSet<Type>();
        var pending = new Stack<Type>([result.GetType()]);
        while (pending.TryPop(out Type? type))
        {
            type = Normalize(type);
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || type == typeof(Version))
            {
                continue;
            }

            Assert.False(
                typeof(IDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(Stream).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(Delegate).IsAssignableFrom(type),
                type.FullName);
            Assert.NotEqual(typeof(TypeResolutionContext), type);
            Assert.NotEqual(typeof(TypeResolutionCatalog), type);
            Assert.NotEqual(typeof(LibraryContentOwner), type);
            Assert.NotEqual(typeof(LibraryOperationLease), type);
            if (type.IsArray)
            {
                pending.Push(type.GetElementType()!);
                continue;
            }
            foreach (Type argument in type.GetGenericArguments())
                pending.Push(argument);
            foreach (Type nested in type.GetNestedTypes(
                BindingFlags.Public))
            {
                pending.Push(nested);
            }
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Push(property.PropertyType);
            }
        }
    }

    private static BrowserPlatformForwarderAction Publish(
        BrowserPlatformForwarderActivation activation,
        long generation,
        PlatformFamilyTarget target,
        PlatformSourcePlan sources,
        PlatformTypeForwardingHopEvidence hop,
        MetadataTypeDefinitionName type)
    {
        BrowserPlatformForwarderResultAuthority authority =
            Authority(
                activation,
                generation,
                target,
                sources,
                hop.SourceAssembly.Assembly.Identity,
                Assert.IsType<Guid>(
                    hop.SourceAssembly.Assembly.ModuleVersionId));
        return Assert.IsType<
            BrowserPlatformForwarderActionPublication.Published>(
                activation.Publish(authority, Shape(hop, type))).Action;
    }

    private static BrowserPlatformForwarderResultAuthority Authority(
        BrowserPlatformForwarderActivation activation,
        long generation,
        PlatformFamilyTarget target,
        PlatformSourcePlan sources,
        AssemblyReferenceIdentity sourceAssembly,
        Guid sourceModuleVersionId) =>
        Assert.IsType<
            BrowserPlatformForwarderResultAdmission.Admitted>(
                activation.BeginResult(
                    generation,
                    target,
                    "linux-x64",
                    sources,
                    sourceAssembly,
                    sourceModuleVersionId)).Authority;

    private static LibraryTypeShape Shape(
        BrowserPlatformForwarderDestination.Forwarder destination) =>
        new(
            destination.Type,
            Text(destination.Type.ToEscapedFullName()),
            Text(destination.Type.Namespace),
            LibraryTypeDeclarationKind.Forwarder,
            definitionKind: null,
            definitionAccessibility: null,
            isPublicSurface: true,
            new(
                Assert.IsType<Guid>(
                    destination.Library.ModuleVersionId),
                destination.Declarations,
                LibraryIdentity(destination.TargetAssembly)),
            new LibraryTypeMemberCountOutcome.NotApplicable(
                LibraryTypeMemberCountNotApplicableReason.Forwarder));

    private static LibraryTypeShape Shape(
        PlatformTypeForwardingHopEvidence hop,
        MetadataTypeDefinitionName type) =>
        Shape(
            type,
            new(
                Assert.IsType<Guid>(
                    hop.SourceAssembly.Assembly.ModuleVersionId),
                hop.Declarations,
                LibraryIdentity(hop.TargetReference)));

    private static LibraryTypeShape Shape(
        MetadataTypeDefinitionName type,
        LibraryTypeForwardingEvidence forwarding) =>
        new(
            type,
            Text(type.ToEscapedFullName()),
            Text(type.Namespace),
            LibraryTypeDeclarationKind.Forwarder,
            definitionKind: null,
            definitionAccessibility: null,
            isPublicSurface: true,
            forwarding,
            new LibraryTypeMemberCountOutcome.NotApplicable(
                LibraryTypeMemberCountNotApplicableReason.Forwarder));

    private static LibraryAssemblyIdentity LibraryIdentity(
        AssemblyReferenceIdentity identity) =>
        new(
            Text(identity.Name),
            identity.Version ?? new Version(),
            identity.Culture is null ? null : Text(identity.Culture),
            identity.PublicKeyToken is null
                ? null
                : Text(identity.PublicKeyToken));

    private static InertString Text(string value) =>
        new(TextPolicy.Field, value);

    private static async Task<
        PlatformTypeDefinitionResolutionResult.Resolved> ResolveRouteAsync(
            PlatformFamilyTarget target,
            PlatformSourcePlan sources,
            string sourceAssembly,
            MetadataTypeDefinitionName type,
            CancellationToken cancellationToken)
    {
        AssemblyReferenceIdentity source = Identity(sourceAssembly);
        var request = new BrowserPlatformForwarderResolutionRequest(
            target,
            "linux-x64",
            sources,
            source,
            ModuleVersionId(sourceAssembly),
            type,
            ImmutableArray<ExportedTypeToken>.Empty,
            source);
        var completed = Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<
                PlatformTypeDefinitionResolutionResult>>.Completed>(
                    await ResolveAsync(request, cancellationToken));
        return Assert.IsType<
            PlatformTypeDefinitionResolutionResult.Resolved>(
                completed.Value.Outcome);
    }

    private static async Task<LibraryTypeShape> ReadForwarderAsync(
        PlatformFamilyTarget target,
        PlatformSourcePlan sources,
        string sourceAssembly,
        MetadataTypeDefinitionName type,
        CancellationToken cancellationToken)
    {
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(
                target,
                sources,
                cancellationToken,
                maxForwardingHops: 8);
        try
        {
            PlatformPopulationMember member = Member(
                prepared.Completed,
                Identity(sourceAssembly));
            LibraryContentOwner owner =
                Owner(prepared.Completed, member);
            InspectionEnvelope<LibraryInspectionOutcome> inspection =
                LibraryInspectionOperation.Execute(
                    new(
                        member.Library,
                        new(
                            new(
                                LibraryTypeAccessibility.Public,
                                count: null,
                                new(
                                    maximumRows: 1_000),
                                LibraryTypeDeclarationSelection
                                    .Forwarders),
                            new(
                                maxTypes: 5_000,
                                maxMembers: 100_000,
                                maxInspectionFailures: 1_000,
                                maxTypeForwarders: 10_000,
                                maxMetadataRows: 1_000_000,
                                maxRetainedTextCharacters:
                                    20_000_000))),
                    Assert.IsType<
                        LibraryOperationLeaseIssueOutcome.Issued>(
                            owner.IssueOperationLease(member.Library))
                        .Lease,
                    cancellationToken);
            LibraryDocument document =
                Assert.IsType<LibraryInspectionOutcome.Available>(
                    inspection.Content).Document;
            return Assert.Single(
                Assert.IsType<
                        LibraryTypePopulationRowsOutcome.Read>(
                        document.Types.Rows)
                    .Items,
                item => item.Identity == type);
        }
        finally
        {
            await RetireAsync(prepared.Completed);
        }
    }

    private static async ValueTask<PlatformHouseOutcome<
        PlatformTypeDefinitionValue.Implementation<
            PlatformTypeDefinitionResolutionResult>>> ResolveAsync(
            BrowserPlatformForwarderResolutionRequest activationRequest,
            CancellationToken cancellationToken,
            int maxForwardingHops = 8)
    {
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(
                activationRequest.Target,
                activationRequest.Sources,
                cancellationToken,
                maxForwardingHops);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            activationRequest.SourceAssembly);
        Assert.Equal(
            activationRequest.SourceModuleVersionId,
            ModuleVersionId(activationRequest.SourceAssembly.Name));
        ResolvedAssemblyReference descriptor =
            SnapshotDescriptor(prepared.Completed, starting);
        var metadataRequest =
            new PlatformMetadataRequestEvidence<TypeResolutionRequest>(
                TypeResolutionRequest.FromAssembly(
                    descriptor,
                    AssemblyResolutionScope.Platform,
                    activationRequest.Type),
                "browser-forwarder-activation");
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "browser-forwarder-activation"),
            new PlatformTargetDemand.Exact(activationRequest.Target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "browser-forwarder-activation")),
            new PlatformHouseOperation.ResolveTypeDefinition
                .FromImplementation<PlatformPopulationMember>(
                    metadataRequest,
                    new(
                        starting,
                        "browser-forwarder-start")),
            activationRequest.Sources,
            Work(maxForwardingHops),
            cancellationToken);
        return await PlatformHouseTypeDefinitionResolver
            .ResolveImplementationAsync(
                request,
                prepared.Completed,
                prepared.Consumed);
    }

    private static async Task<PreparedPopulation>
        PrepareRuntimePopulationAsync(
            PlatformFamilyTarget target,
            PlatformSourcePlan sources,
            CancellationToken cancellationToken,
            int maxForwardingHops)
    {
        PlatformSourceCapabilityIdentity capability =
            Assert.Single(
                Assert.Single(sources.Selections).Capabilities);
        var demand = new PlatformTargetDemand.Exact(target);
        var populationDemand =
            new PlatformPopulationDemand.CompletePopulation();
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "browser-forwarder-population"),
            demand,
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "browser-forwarder-population")),
            new PlatformHouseOperation.Realize(
                populationDemand,
                PlatformViewDemand.Implementation),
            sources,
            Work(maxForwardingHops),
            cancellationToken);
        var contribution =
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Implementation,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    "browser-forwarder-source-generation"),
                target,
                PlatformSourceCoordinateIdentity.Create(
                    "browser-forwarder-coordinate"),
                populationDemand,
                PlatformSourceContributionCompleteness.Authoritative);

        string[] names =
        [
            "System.Xml",
            "System.Xml.ReaderWriter",
            "System.Private.Xml",
            "System.Private.CoreLib",
        ];
        var items = new List<
            PlatformPopulationLibraryArtifactMaterializationItem>(
                names.Length);
        long totalBytes = 0;
        foreach (string name in names)
        {
            byte[] image = await File.ReadAllBytesAsync(
                Asset(name),
                cancellationToken);
            totalBytes = checked(totalBytes + image.LongLength);
            items.Add(
                new(
                    new(
                        contribution,
                        new TestProvenance(name),
                        Identity(image),
                        image.LongLength,
                        token =>
                        {
                            token.ThrowIfCancellationRequested();
                            return new MemoryStream(
                                image,
                                writable: false);
                        }),
                    new(
                        target,
                        PlatformPopulationMemberRole.Focus)));
        }

        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: items.Count,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: totalBytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);
        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PlatformHousePopulationArtifactMaterializer
                    .MaterializeImplementationsAsync(
                        request,
                        items,
                        consumed,
                        "browser-forwarder-population"));
        return new(completed, consumed);
    }

    private static PlatformPopulationMember Member(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed,
        AssemblyReferenceIdentity identity) =>
        Assert.Single(
            completed.Population.Value.Members,
            member =>
                member.Library.ApiAssembly.AssemblyIdentity is { } assembly
                && assembly.Identity.IsEquivalentTo(identity));

    private static ResolvedAssemblyReference SnapshotDescriptor(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed,
        PlatformPopulationMember member)
    {
        LibraryContentOwner owner = Owner(completed, member);
        LibraryContentReference content =
            member.Library.ImplementationAssembly!;
        using LibraryOperationLease lease =
            Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                    owner.IssueOperationLease(member.Library))
                .Lease;
        return lease.Snapshot(
            content,
            static (view, _) =>
            {
                byte[] image = view.Content.ToArray();
                return ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                    view.Reference.Registration,
                    () => new MemoryStream(image, writable: false),
                    AssemblyResolutionProvenance.Designated(
                        "Browser forwarder activation test"))
                    ?? throw new BadImageFormatException();
            });
    }

    private static LibraryContentOwner Owner(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed,
        PlatformPopulationMember member)
    {
        int index = completed.Population.Value.Members
            .Select((candidate, candidateIndex) =>
                (candidate, candidateIndex))
            .Single(item => ReferenceEquals(item.candidate, member))
            .candidateIndex;
        return completed.Population.Owners[index];
    }

    private static async ValueTask RetireAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed)
    {
        foreach (LibraryContentOwner owner in completed.Population.Owners)
            await owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    private static AssemblyReferenceIdentity Identity(string assemblyName) =>
        Identity(File.ReadAllBytes(Asset(assemblyName)));

    private static AssemblyReferenceIdentity Identity(byte[] image)
    {
        using var reader =
            new PEReader(new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    private static Guid ModuleVersionId(string assemblyName)
    {
        using var reader = new PEReader(File.OpenRead(Asset(assemblyName)));
        MetadataReader metadata = reader.GetMetadataReader();
        return metadata.GetGuid(
            metadata.GetModuleDefinition().Mvid);
    }

    private static string Asset(string assemblyName) =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PlatformForwarderActivation",
            $"{assemblyName}.dll");

    private static MetadataTypeDefinitionName XmlReaderName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "System.Xml",
                ImmutableArray.Create("XmlReader"))).Name;

    private static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0-rc.1.26425.128"));

    private static PlatformSourcePlan Sources()
    {
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create(
                "browser-forwarder-runtime");
        return new(
            PlatformSourcePlanIdentity.Create(
                "browser-forwarder-sources"),
            PlatformSourcePolicyGeneration.Create(
                "browser-forwarder-generation"),
            [
                new(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [capability]),
            ]);
    }

    private static PlatformHouseWorkBudget Work(
        int maxForwardingHops) =>
        new(
            maxSourceOperations: 4,
            maxTargetCandidates: 4,
            maxAssemblies: 16,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 64 * 1024 * 1024,
            maxForwardingHops,
            maxDuration: TimeSpan.FromSeconds(30));

    private static Type Normalize(Type type) =>
        type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Nullable<>)
            ? type.GetGenericArguments()[0]
            : type;

    private sealed record PreparedPopulation(
        PlatformPopulationArtifactMaterializationOutcome.Completed Completed,
        PlatformHouseConsumedWork Consumed);

    private sealed record TestProvenance(string Name) : IArtifactProvenance;
}
