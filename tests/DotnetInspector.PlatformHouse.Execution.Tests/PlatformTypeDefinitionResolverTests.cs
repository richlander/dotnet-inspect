using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Libraries;
using DotnetInspector.Platforms;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.PlatformHouse.Tests;

public sealed class PlatformTypeDefinitionResolverTests
{
    [Fact]
    public async Task
        ResolveImplementationAsync_PreservesRealTwoHopForwardingRoute()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken);
        LibraryContentOwner startingOwner =
            Owner(prepared.Completed, starting);

        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
            outcome =
                await PlatformHouseTypeDefinitionResolver
                    .ResolveImplementationAsync(
                        request,
                        prepared.Completed,
                        prepared.Consumed);

        var completed = Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed>(
                    outcome);
        Assert.True(
            completed.Value.Outcome
                is TypeResolutionOutcome.Resolved,
            completed.Value.Outcome
                is TypeResolutionOutcome.Rejected rejected
                    ? rejected.Failure.ToString()
                    : completed.Value.Outcome
                        is TypeResolutionOutcome.UnboundBinding unbound
                            ? $"{unbound.Target}; hops: {string.Join(", ", unbound.Hops.Select(hop => hop.TargetReference.Name))}"
                    : completed.Value.Outcome.GetType().FullName);
        var resolved =
            (TypeResolutionOutcome.Resolved)completed.Value.Outcome;

        Assert.Collection(
            resolved.Hops,
            hop =>
            {
                Assert.Equal(
                    "System.Xml",
                    hop.SourceAssembly.Assembly.Identity.Name);
                Assert.Equal(
                    "System.Xml.ReaderWriter",
                    hop.TargetReference.Name);
                Assert.NotEmpty(hop.Declarations);
            },
            hop =>
            {
                Assert.Equal(
                    "System.Xml.ReaderWriter",
                    hop.SourceAssembly.Assembly.Identity.Name);
                Assert.Equal(
                    "System.Private.Xml",
                    hop.TargetReference.Name);
                Assert.NotEmpty(hop.Declarations);
            });
        Assert.Equal(
            "System.Private.Xml",
            resolved.Definition.Assembly.Assembly.Identity.Name);
        Assert.Equal(XmlReaderName(), resolved.Definition.Type);
        Assert.Equal(2, completed.Receipt.ConsumedWork.ForwardingHops);

        var completion =
            Assert.IsType<PlatformHouseCompletion.TypeDefinition>(
                completed.Receipt.Completion);
        Assert.Equal(
            PlatformTypeDefinitionCompletionKind.Implementation,
            completion.Kind);
        Assert.Null(completion.ReferenceOutcome);
        Assert.NotNull(completion.ImplementationOutcome);
        Assert.Null(completion.Correspondence);
        Assert.Empty(completion.SelectedContributions);
        Assert.IsType<LibraryOperationLeaseIssueOutcome.OwnerReleased>(
            startingOwner.IssueOperationLease(starting.Library));
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_PreservesDirectDefinitionWithoutHops()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Private.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken);

        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
            outcome =
                await PlatformHouseTypeDefinitionResolver
                    .ResolveImplementationAsync(
                        request,
                        prepared.Completed,
                        prepared.Consumed);

        var completed = Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed>(
                    outcome);
        Assert.True(
            completed.Value.Outcome
                is TypeResolutionOutcome.Resolved,
            completed.Value.Outcome
                is TypeResolutionOutcome.Rejected rejected
                    ? rejected.Failure.ToString()
                    : completed.Value.Outcome.GetType().FullName);
        var resolved =
            (TypeResolutionOutcome.Resolved)completed.Value.Outcome;
        Assert.Empty(resolved.Hops);
        Assert.Equal(
            "System.Private.Xml",
            resolved.Definition.Assembly.Assembly.Identity.Name);
        Assert.Equal(XmlReaderName(), resolved.Definition.Type);
        Assert.Equal(0, completed.Receipt.ConsumedWork.ForwardingHops);
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_ResolvesForwardedTypeWithExternalBase()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(
                cancellationToken,
                "System",
                "System.ObjectModel",
                "System.Private.CoreLib");
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        MetadataTypeDefinitionName type =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System.Collections.ObjectModel",
                    ImmutableArray.Create("ObservableCollection`1"))).Name;
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            type,
            cancellationToken);

        var completed = Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed>(
                    await PlatformHouseTypeDefinitionResolver
                        .ResolveImplementationAsync(
                            request,
                            prepared.Completed,
                            prepared.Consumed));
        Assert.True(
            completed.Value.Outcome is TypeResolutionOutcome.Resolved,
            completed.Value.Outcome is TypeResolutionOutcome.Rejected rejected
                ? rejected.Failure.ToString()
                : completed.Value.Outcome.GetType().FullName);
        var resolved =
            (TypeResolutionOutcome.Resolved)completed.Value.Outcome;
        TypeForwardingHop hop = Assert.Single(resolved.Hops);
        Assert.Equal("System", hop.SourceAssembly.Assembly.Identity.Name);
        Assert.Equal("System.ObjectModel", hop.TargetReference.Name);
        Assert.Equal(
            "System.ObjectModel",
            resolved.Definition.Assembly.Assembly.Identity.Name);
        Assert.Equal(type, resolved.Definition.Type);
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_PreservesMissingOutcome()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Private.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        MetadataTypeDefinitionName missing =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "System.Xml",
                    ImmutableArray.Create("NoSuchRuntimeType"))).Name;
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            missing,
            cancellationToken);

        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
            outcome =
                await PlatformHouseTypeDefinitionResolver
                    .ResolveImplementationAsync(
                        request,
                        prepared.Completed,
                        prepared.Consumed);
        Assert.True(
            outcome is PlatformHouseOutcome<
                PlatformTypeDefinitionValue
                    .Implementation<TypeResolutionOutcome>>.Completed,
            $"{outcome.GetType().FullName}; {outcome.Receipt.SettlementKind}");
        var completed = (PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed)outcome;
        var notFound = Assert.IsType<TypeResolutionOutcome.NotFound>(
            completed.Value.Outcome);
        Assert.Empty(notFound.Hops);
        Assert.Equal(
            "System.Private.Xml",
            notFound.LastAssembly.Assembly.Identity.Name);
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_PreservesForwardingEvidenceWhenTargetIsUnavailable()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(
                cancellationToken,
                "System.Xml",
                "System.Private.Xml",
                "System.Private.CoreLib");
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken);

        var completed = Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed>(
                    await PlatformHouseTypeDefinitionResolver
                        .ResolveImplementationAsync(
                            request,
                            prepared.Completed,
                            prepared.Consumed));
        var unbound = Assert.IsType<TypeResolutionOutcome.UnboundBinding>(
            completed.Value.Outcome);
        TypeForwardingHop hop = Assert.Single(unbound.Hops);
        Assert.Equal(
            "System.Xml",
            hop.SourceAssembly.Assembly.Identity.Name);
        Assert.Equal("System.Xml.ReaderWriter", hop.TargetReference.Name);
        Assert.Equal(
            "System.Xml.ReaderWriter",
            Assert.IsType<AssemblyBindingTarget.AssemblyReference>(
                    unbound.Target)
                .Identity.Name);
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_PreservesHopBoundedOutcome()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken,
            Work(maxForwardingHops: 1));

        PlatformHouseOutcome<
            PlatformTypeDefinitionValue.Implementation<TypeResolutionOutcome>>
            outcome =
                await PlatformHouseTypeDefinitionResolver
                    .ResolveImplementationAsync(
                        request,
                        prepared.Completed,
                        prepared.Consumed);
        Assert.True(
            outcome is PlatformHouseOutcome<
                PlatformTypeDefinitionValue
                    .Implementation<TypeResolutionOutcome>>.Completed,
            $"{outcome.GetType().FullName}; {outcome.Receipt.SettlementKind}");
        var completed = (PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Completed)outcome;
        var rejected = Assert.IsType<TypeResolutionOutcome.Rejected>(
            completed.Value.Outcome);
        Assert.IsType<TypeResolutionFailure.HopBudgetExceeded>(
            rejected.Failure);
        Assert.Equal(2, rejected.Hops.Length);
        Assert.Equal(1, completed.Receipt.ConsumedWork.ForwardingHops);
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_RejectsSameAssemblyFromAnotherPopulation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation expected =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PreparedPopulation foreign =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember expectedMember = Member(
            expected.Completed,
            "System.Xml");
        PlatformPopulationMember foreignMember = Member(
            foreign.Completed,
            "System.Xml");
        ResolvedAssemblyReference foreignStart =
            SnapshotDescriptor(foreign.Completed, foreignMember);
        PlatformHouseRequest request = TypeRequest(
            expected,
            expectedMember,
            foreignStart,
            XmlReaderName(),
            cancellationToken);
        LibraryContentOwner expectedOwner =
            Owner(expected.Completed, expectedMember);

        try
        {
            Assert.IsType<PlatformHouseOutcome<
                PlatformTypeDefinitionValue
                    .Implementation<TypeResolutionOutcome>>.Rejected>(
                        await PlatformHouseTypeDefinitionResolver
                            .ResolveImplementationAsync(
                                request,
                                expected.Completed,
                                expected.Consumed));
            Assert.IsType<LibraryOperationLeaseIssueOutcome.OwnerReleased>(
                expectedOwner.IssueOperationLease(
                    expectedMember.Library));
        }
        finally
        {
            await RetireAsync(foreign.Completed);
        }
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_RejectsDifferentSourcePlanBeforeMetadata()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        var foreignSources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create("foreign-plan"),
            PlatformSourcePolicyGeneration.Create(
                "foreign-plan-generation"),
            []);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken,
            sources: foreignSources);
        LibraryContentOwner startingOwner =
            Owner(prepared.Completed, starting);

        Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Rejected>(
                    await PlatformHouseTypeDefinitionResolver
                        .ResolveImplementationAsync(
                            request,
                            prepared.Completed,
                            prepared.Consumed));
        Assert.IsType<LibraryOperationLeaseIssueOutcome.OwnerReleased>(
            startingOwner.IssueOperationLease(starting.Library));
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_RejectsUnsettledTargetWithoutThrowing()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest exactRequest = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken);
        var selecting = new PlatformTargetDemand.Selecting(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            new PlatformVersionSelectionDemand.Requirement(
                PlatformVersionRequirementIdentity.Create("net11")),
            [PlatformSourceCapabilityIdentity.Create("target-discovery")],
            new PlatformTargetDiscoveryBudget(4, 8));
        var request = new PlatformHouseRequest(
            exactRequest.Identity,
            selecting,
            exactRequest.Origin,
            exactRequest.Operation,
            exactRequest.Sources,
            exactRequest.Work,
            cancellationToken);
        LibraryContentOwner startingOwner =
            Owner(prepared.Completed, starting);

        var rejected = Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Rejected>(
                    await PlatformHouseTypeDefinitionResolver
                        .ResolveImplementationAsync(
                            request,
                            prepared.Completed,
                            prepared.Consumed));
        var settlement =
            Assert.IsType<PlatformTargetSettlement.Unsettled>(
                rejected.Receipt.TargetSettlement);
        Assert.Same(selecting, settlement.Demand);
        Assert.IsType<LibraryOperationLeaseIssueOutcome.OwnerReleased>(
            startingOwner.IssueOperationLease(starting.Library));
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_ReturnsIncompleteWhenPriorWorkExceedsBudget()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken,
            Work(maxAssemblies: 3));
        LibraryContentOwner startingOwner =
            Owner(prepared.Completed, starting);

        Assert.IsType<PlatformHouseOutcome<
            PlatformTypeDefinitionValue
                .Implementation<TypeResolutionOutcome>>.Incomplete>(
                    await PlatformHouseTypeDefinitionResolver
                        .ResolveImplementationAsync(
                            request,
                            prepared.Completed,
                            prepared.Consumed));
        Assert.IsType<LibraryOperationLeaseIssueOutcome.OwnerReleased>(
            startingOwner.IssueOperationLease(starting.Library));
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_RetiresPopulationBeforeCancellationEscapes()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        using var cancellation = new CancellationTokenSource();
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellation.Token);
        LibraryContentOwner startingOwner =
            Owner(prepared.Completed, starting);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await PlatformHouseTypeDefinitionResolver
                .ResolveImplementationAsync(
                    request,
                    prepared.Completed,
                    prepared.Consumed));
        Assert.IsType<LibraryOperationLeaseIssueOutcome.OwnerReleased>(
            startingOwner.IssueOperationLease(starting.Library));
    }

    [Fact]
    public async Task
        ResolveImplementationAsync_ReturnsResourceFreePublicResult()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PreparedPopulation prepared =
            await PrepareRuntimePopulationAsync(cancellationToken);
        PlatformPopulationMember starting = Member(
            prepared.Completed,
            "System.Private.Xml");
        ResolvedAssemblyReference start =
            SnapshotDescriptor(prepared.Completed, starting);
        PlatformHouseRequest request = TypeRequest(
            prepared,
            starting,
            start,
            XmlReaderName(),
            cancellationToken);

        object outcome =
            await PlatformHouseTypeDefinitionResolver
                .ResolveImplementationAsync(
                    request,
                    prepared.Completed,
                    prepared.Consumed);

        var visited = new HashSet<Type>();
        var pending = new Stack<Type>([outcome.GetType()]);
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
                || type == typeof(Version)
                || type == typeof(Func<Stream>))
            {
                continue;
            }

            Assert.False(
                typeof(IDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(
                typeof(IAsyncDisposable).IsAssignableFrom(type),
                type.FullName);
            Assert.False(typeof(Stream).IsAssignableFrom(type), type.FullName);
            Assert.False(typeof(Delegate).IsAssignableFrom(type), type.FullName);
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
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public))
            {
                pending.Push(property.PropertyType);
            }
        }
    }

    static async Task<PreparedPopulation> PrepareRuntimePopulationAsync(
        CancellationToken cancellationToken,
        params string[] assemblyNames)
    {
        PlatformFamilyTarget target = Target();
        var demand = new PlatformTargetDemand.Exact(target);
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create(
                "runtime-implementation");
        var sources = new PlatformSourcePlan(
            PlatformSourcePlanIdentity.Create(
                "runtime-implementation-sources"),
            PlatformSourcePolicyGeneration.Create(
                "runtime-implementation-generation"),
            [
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [capability]),
            ]);
        var populationDemand =
            new PlatformPopulationDemand.CompletePopulation();
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "runtime-implementation-population"),
            demand,
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "runtime-implementation-population")),
            new PlatformHouseOperation.Realize(
                populationDemand,
                PlatformViewDemand.Implementation),
            sources,
            Work());
        var contribution =
            new PlatformSourceContribution.Realization(
                PlatformSourceFacet.Implementation,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(
                    "runtime-implementation-source-generation"),
                target,
                PlatformSourceCoordinateIdentity.Create(
                    "runtime-implementation-coordinate"),
                populationDemand,
                PlatformSourceContributionCompleteness.Authoritative);

        string[] names = assemblyNames.Length == 0
            ? [
                "System.Xml",
                "System.Xml.ReaderWriter",
                "System.Private.Xml",
                "System.Private.CoreLib",
            ]
            : assemblyNames;
        var items = new List<
            PlatformPopulationLibraryArtifactMaterializationItem>(
                names.Length);
        long totalBytes = 0;
        foreach (string name in names)
        {
            byte[] image = await File.ReadAllBytesAsync(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "RealAssets",
                    "PlatformTypeResolution",
                    $"{name}.dll"),
                cancellationToken);
            AssemblyReferenceIdentity identity = Identity(image);
            totalBytes = checked(totalBytes + image.LongLength);
            items.Add(
                new PlatformPopulationLibraryArtifactMaterializationItem(
                    new PlatformLibraryArtifactMaterializationItem(
                        contribution,
                        new TestProvenance(name),
                        identity,
                        image.LongLength,
                        token =>
                        {
                            token.ThrowIfCancellationRequested();
                            return new MemoryStream(
                                image,
                                writable: false);
                        }),
                    new PlatformPopulationMemberAttribution(
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
                        "runtime-implementation-population"));
        return new(completed, target, sources, consumed);
    }

    static PlatformHouseRequest TypeRequest(
        PreparedPopulation prepared,
        PlatformPopulationMember starting,
        ResolvedAssemblyReference descriptor,
        MetadataTypeDefinitionName type,
        CancellationToken cancellationToken,
        PlatformHouseWorkBudget? work = null,
        PlatformSourcePlan? sources = null)
    {
        var metadataRequest =
            new PlatformMetadataRequestEvidence<TypeResolutionRequest>(
                TypeResolutionRequest.FromAssembly(
                    descriptor,
                    AssemblyResolutionScope.Platform,
                    type),
                "implementation-type-request");
        var operation = new PlatformHouseOperation.ResolveTypeDefinition
            .FromImplementation<PlatformPopulationMember>(
                metadataRequest,
                new PlatformTypeResolutionCandidateEvidence<
                    PlatformPopulationMember>(
                        starting,
                        "implementation-starting-member"));
        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "implementation-type-resolution"),
            new PlatformTargetDemand.Exact(prepared.Target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "implementation-type-resolution")),
            operation,
            sources ?? prepared.Sources,
            work ?? Work(),
            cancellationToken);
    }

    static ResolvedAssemblyReference SnapshotDescriptor(
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
                return ResolvedAssemblyReference
                    .CreateFromArtifactIfManaged(
                        view.Reference.Registration,
                        () => new MemoryStream(image, writable: false),
                        AssemblyResolutionProvenance.Designated(
                            "PlatformHouse type-resolution test"))
                    ?? throw new BadImageFormatException();
            });
    }

    static PlatformPopulationMember Member(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed,
        string assemblyName) =>
        Assert.Single(
            completed.Population.Value.Members,
            member => string.Equals(
                member.Library.ApiAssembly.AssemblyIdentity?.Identity.Name,
                assemblyName,
                StringComparison.Ordinal));

    static LibraryContentOwner Owner(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed,
        PlatformPopulationMember member)
    {
        int index = completed.Population.Value.Members
            .Select((candidate, index) => (candidate, index))
            .Single(item => ReferenceEquals(item.candidate, member))
            .index;
        return completed.Population.Owners[index];
    }

    static AssemblyReferenceIdentity Identity(byte[] image)
    {
        using var reader = new PEReader(
            new MemoryStream(image, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    static MetadataTypeDefinitionName XmlReaderName() =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                "System.Xml",
                ImmutableArray.Create("XmlReader"))).Name;

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0-rc.1.26425.128"));

    static PlatformHouseWorkBudget Work(
        int maxForwardingHops = 8,
        int maxAssemblies = 16) =>
        new(
            maxSourceOperations: 4,
            maxTargetCandidates: 4,
            maxAssemblies,
            maxXmlDocuments: 0,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 64 * 1024 * 1024,
            maxForwardingHops,
            maxDuration: TimeSpan.FromSeconds(30));

    static async ValueTask RetireAsync(
        PlatformPopulationArtifactMaterializationOutcome.Completed completed)
    {
        foreach (LibraryContentOwner owner in completed.Population.Owners)
            await owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
    }

    static Type Normalize(Type type) =>
        type.IsGenericType
            && type.GetGenericTypeDefinition() == typeof(Nullable<>)
            ? type.GetGenericArguments()[0]
            : type;

    sealed record PreparedPopulation(
        PlatformPopulationArtifactMaterializationOutcome.Completed Completed,
        PlatformFamilyTarget Target,
        PlatformSourcePlan Sources,
        PlatformHouseConsumedWork Consumed);

    sealed record TestProvenance(string Name) : IArtifactProvenance;
}
