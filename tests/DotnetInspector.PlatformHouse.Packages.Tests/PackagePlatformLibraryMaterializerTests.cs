using System.Reflection;
using DotnetInspector.DocumentationHouse;
using DotnetInspector.DocumentationHouse.Platform;
using DotnetInspector.EcosystemLoading;
using DotnetInspector.Ecosystems;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Resources;

namespace DotnetInspector.PlatformHouse.Packages.Tests;

public sealed class PackagePlatformLibraryMaterializerTests
{
    private static readonly ApiSurfaceExtractionBounds
        s_documentationApiSurfaceBounds =
            new(
                maxTypes: 5_000,
                maxMembers: 100_000,
                maxInspectionFailures: 1_000,
                maxTypeForwarders: 10_000,
                maxMetadataRows: 1_000_000,
                maxRetainedTextCharacters: 20_000_000);

    [Fact]
    public async Task
        PublicRuntimeLoaderConsumesPackageBackedPopulationCapability()
    {
        CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
                PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        await using var workspace = new InspectionWorkspace(
                EcosystemPackCatalog.CreateWorkspacePlan(
                    [EcosystemPackIds.Runtime]));
        WorkspaceRegistrationRevision revision =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot()).Revision;
        WorkspaceEcosystemRegistrationDeclaration registration =
                Assert.IsType<WorkspaceRegistration.Ecosystem>(
                    Assert.Single(revision.Registrations)).Declaration;
        var known = Assert.IsType<
                EcosystemPopulationLoaderSelection.Known<
                    RuntimeEcosystemPopulationLoadInputs>>(
                        EcosystemPackCatalog.SelectPopulationLoader(
                            revision,
                            registration,
                            EcosystemPopulationDemand
                                .WholePopulation.Instance));
        EcosystemPopulationCapabilityPlanIdentity capabilityPlan =
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "package-runtime-capability");
        var capability = new PackagePopulationCapability(
                capabilityPlan,
                environment,
                adapter);
        var inputs = new RuntimeEcosystemPopulationLoadInputs(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "package-runtime-policy"),
                capabilityPlan,
                EcosystemPopulationWorkIdentity.Create(
                    "package-runtime-work"),
                capability);

        var outcome =
                Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                    await EcosystemPopulationLoadOperation.InvokeAsync(
                        known.CreateRequest(inputs, cancellationToken)));
        EcosystemPopulationAdmissionResult admission =
                await EcosystemPopulationAdmissionOperation.AdmitAsync(
                    workspace,
                    outcome);

        Assert.Equal(2, admission.Libraries.Count);
        Assert.All(
                admission.Libraries,
                static correspondence =>
                {
                    Assert.Equal(
                        EcosystemPopulationLibraryRole.Focus,
                        correspondence.LoadedLibrary.Roles);
                    Assert.IsType<PackageReferenceArtifactProvenance>(
                        Assert.IsType<PlatformLibraryArtifactProvenance>(
                                correspondence.LoadedLibrary
                                    .Reference.ApiAssembly
                                    .ArtifactReference.Provenance)
                            .SourceProvenance);
                });
        Assert.Equal(2, admission.Contributions.Count);
        var accepted =
                Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                    Assert.IsType<
                            EcosystemPopulationChildAdmission.Attempted>(
                            Assert.Single(admission.ChildAdmissions))
                        .Outcome);
        Assert.Same(revision, accepted.Receipt.RegistrationRevision);
        Assert.All(
                admission.Libraries,
                correspondence =>
                {
                    Assert.Same(outcome.Receipt, correspondence.LoadReceipt);
                    Assert.Same(
                        accepted.Receipt,
                        correspondence.Admission);
                    Assert.Same(
                        correspondence.LoadedLibrary.Reference,
                        correspondence.Occurrence.Library);
                });
        Assert.Equal(
                PlatformFamily.DotNetRuntime,
                Assert.Single(outcome.Receipt.Children)
                    .PlatformEvidence!
                    .Request
                    .Target
                    .Family);

        await workspace.CloseAsync();
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        UnmeasuredPackageFailureConsumesDelegatedWorkBeforeFallback()
    {
        const string version = "10.0.12";
        const string assemblyPath =
            "ref/net10.0/System.Text.Json.dll";
        const string documentationPath =
            "ref/net10.0/System.Text.Json.xml";
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        KeyValuePair<string, byte[]>[] entries =
        [
            PackagePlatformTestData.Entry(assemblyPath, image),
            PackagePlatformTestData.Entry(
                documentationPath,
                [1, 2, 3]),
        ];
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        version,
                        entries)),
            ]);
        var content = new TrackingPackageContent(
            environment.Clients[0].Source.Producer.Key,
            entries,
            [documentationPath],
            path => new IOException(
                $"Entry {path} could not be opened."));
        environment.Store = new StaticPackageStore(content);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformSourceCapabilityIdentity fallbackReference =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-reference");
        long maximumBytes = image.LongLength + 3;
        PlatformHouseRequest request =
            SelectedPackageReferenceFailureRequest(
                adapter,
                fallbackReference,
                PackagePlatformTestData.Identity(image),
                maximumBytes,
                cancellationToken);
        int fallbackInvocations = 0;
        var realizationFallback =
            new PlatformLibraryRealizationSource(
                fallbackReference,
                PlatformSourceFacet.Reference,
                (operation, target, _, _) =>
                {
                    fallbackInvocations++;
                    PlatformLibraryRealizationSourceAttempt attempt =
                        new PlatformLibraryRealizationSourceAttempt
                            .NotSucceeded(
                                new PlatformSourceContribution
                                    .Unavailable(
                                        PlatformSourceFacet.Reference,
                                        fallbackReference,
                                        operation.Snapshot,
                                        PlatformSourceGeneration.Create(
                                            "fallback-reference-generation"),
                                        target,
                                        PlatformSourceUnavailabilityKind
                                            .Absent));
                    return ValueTask.FromResult(attempt);
                });

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [
                        PackagePlatformTargetDiscovery.CreateSource(
                            adapter,
                            (operation, remainingWork) =>
                                environment.IssueOperation(
                                    operation.CancellationToken,
                                    operationTimeout:
                                        remainingWork.MaxDuration)),
                    ],
                    [
                        PackagePlatformSelectedLibraryRealization
                            .CreateReferenceSource(
                                adapter,
                                (operation, remainingWork) =>
                                    environment.IssueOperation(
                                        operation.CancellationToken,
                                        operationTimeout:
                                            remainingWork.MaxDuration),
                                PlatformHouseCandidateIdentity.Create(
                                    "package-reference-candidate")),
                        realizationFallback,
                    ],
                    "package-selected-library"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, fallbackInvocations);
        Assert.Contains(assemblyPath, content.OpenedEntries);
        Assert.Contains(documentationPath, content.OpenedEntries);
        PlatformHouseReceipt receipt =
            terminal.TerminalRealization.Outcome.Receipt;
        Assert.Contains(
            receipt.SourceSettlements,
            settlement =>
                settlement.Contribution
                    is PlatformSourceContribution.Failed
                && ReferenceEquals(
                    adapter.ReferenceRealization,
                    settlement.Contribution.Capability));
        PlatformHouseConsumedWork consumed = receipt.ConsumedWork;
        Assert.Equal(1, consumed.Assemblies);
        Assert.Equal(1, consumed.XmlDocuments);
        Assert.Equal(maximumBytes, consumed.Bytes);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationManifestBytesConsumeAllowanceBeforeFallback()
    {
        const string version = "10.0.12";
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] requested =
            PackagePlatformTestData.Assembly("System.Text.Json");
        byte[] available =
            PackagePlatformTestData.Assembly("System.Runtime");
        byte[] runtimeConfiguration =
            PackagePlatformTestData.RuntimeConfiguration();
        byte[] dependencyManifest =
            PackagePlatformTestData.DependencyManifestForTarget(
                ".NETCoreApp,Version=v10.0/linux-x64",
                "System.Runtime.dll");
        IReadOnlyList<KeyValuePair<string, byte[]>> entries =
            PackagePlatformTestData.RuntimePackEntries(
                "Microsoft.NETCore.App",
                "net10.0",
                runtimeConfiguration,
                dependencyManifest,
                ("System.Runtime.dll", available));
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        version,
                        [
                            PackagePlatformTestData.Entry(
                                "ref/net10.0/System.Runtime.dll",
                                available),
                        ]),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        version,
                        entries)),
            ]);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformSourceCapabilityIdentity fallbackImplementation =
            PlatformSourceCapabilityIdentity.Create(
                "fallback-implementation");
        long maximumBytes =
            available.LongLength
            + runtimeConfiguration.LongLength
            + dependencyManifest.LongLength;
        PlatformHouseRequest request =
            ImplementationFallbackRequest(
                adapter,
                fallbackImplementation,
                PackagePlatformTestData.Identity(requested),
                maximumBytes,
                cancellationToken);
        int fallbackInvocations = 0;
        var realizationFallback =
            new PlatformLibraryRealizationSource(
                fallbackImplementation,
                PlatformSourceFacet.Implementation,
                (operation, target, _, _) =>
                {
                    fallbackInvocations++;
                    PlatformLibraryRealizationSourceAttempt attempt =
                        new PlatformLibraryRealizationSourceAttempt
                            .NotSucceeded(
                                new PlatformSourceContribution
                                    .Unavailable(
                                        PlatformSourceFacet.Implementation,
                                        fallbackImplementation,
                                        operation.Snapshot,
                                        PlatformSourceGeneration.Create(
                                            "fallback-implementation-generation"),
                                        target,
                                        PlatformSourceUnavailabilityKind
                                            .Absent));
                    return ValueTask.FromResult(attempt);
                });

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [
                        PackagePlatformTargetDiscovery.CreateSource(
                            adapter,
                            (operation, remainingWork) =>
                                environment.IssueOperation(
                                    operation.CancellationToken,
                                    operationTimeout:
                                        remainingWork.MaxDuration)),
                    ],
                    [
                        PackagePlatformSelectedLibraryRealization
                            .CreateImplementationSource(
                                adapter,
                                "linux-x64",
                                (operation, remainingWork) =>
                                    environment.IssueOperation(
                                        operation.CancellationToken,
                                        operationTimeout:
                                            remainingWork.MaxDuration),
                                PlatformHouseCandidateIdentity.Create(
                                    "package-implementation-candidate")),
                        realizationFallback,
                    ],
                    "package-selected-library"));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        Assert.Equal(0, fallbackInvocations);
        PlatformHouseConsumedWork consumed =
            terminal.TerminalRealization.Outcome.Receipt.ConsumedWork;
        Assert.Equal(1, consumed.Assemblies);
        Assert.Equal(maximumBytes, consumed.Bytes);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        SelectedPackageAssociationCompletesPairedSystemTextJson()
    {
        const string version = "10.0.12";
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        version,
                        [
                            PackagePlatformTestData.Entry(
                                "ref/net10.0/System.Text.Json.dll",
                                image),
                        ]),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        version,
                        PackagePlatformTestData.RuntimePackEntries(
                            "Microsoft.NETCore.App",
                            "net10.0",
                            PackagePlatformTestData.RuntimeConfiguration(),
                            PackagePlatformTestData
                                .DependencyManifestForTarget(
                                    ".NETCoreApp,Version=v10.0/linux-x64",
                                    "System.Text.Json.dll"),
                            ("System.Text.Json.dll", image)))),
            ]);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = SelectedPackageRequest(
            adapter,
            PackagePlatformTestData.Identity(image),
            cancellationToken);

        PlatformLibraryArtifactMaterializationOutcome outcome =
            await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [
                        PackagePlatformTargetDiscovery.CreateSource(
                            adapter,
                            (operation, remainingWork) =>
                                environment.IssueOperation(
                                    operation.CancellationToken,
                                    operationTimeout:
                                        remainingWork.MaxDuration)),
                    ],
                    [
                        PackagePlatformSelectedLibraryRealization
                            .CreateReferenceSource(
                                adapter,
                                (operation, remainingWork) =>
                                    environment.IssueOperation(
                                        operation.CancellationToken,
                                        operationTimeout:
                                            remainingWork.MaxDuration),
                                PlatformHouseCandidateIdentity.Create(
                                    "package-reference-candidate")),
                        PackagePlatformSelectedLibraryRealization
                            .CreateImplementationSource(
                                adapter,
                                "linux-x64",
                                (operation, remainingWork) =>
                                    environment.IssueOperation(
                                        operation.CancellationToken,
                                        operationTimeout:
                                            remainingWork.MaxDuration),
                                PlatformHouseCandidateIdentity.Create(
                                    "package-implementation-candidate")),
                    ],
                    "package-selected-library");
        if (outcome
            is PlatformLibraryArtifactMaterializationOutcome.Terminal
                terminal)
        {
            PlatformHouseReceipt receipt =
                terminal.TerminalRealization.Outcome.Receipt;
            string detail = receipt.Termination
                is PlatformHouseTermination.Rejected
                {
                    Rejection:
                        PlatformHouseRejection.OwnerEvidence owner,
                }
                    ? $"{owner.Kind}:{owner.Evidence.Name}"
                    : receipt.Termination?.GetType().Name ?? "none";
            Assert.Fail(
                $"Unexpected {receipt.SettlementKind}: {detail}; "
                + string.Join(
                    ", ",
                    receipt.SourceSettlements.Select(
                        settlement =>
                            $"{settlement.Contribution.Facet}/"
                            + $"{settlement.Contribution.Kind}/"
                            + $"{settlement.Disposition}")));
        }
        var completed = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Completed>(
                outcome);

        Assert.Equal(
            version,
            completed.Library.Outcome.Receipt.TargetSettlement
                .SettledTarget!.Version.Value);
        Assert.Equal(
            3,
            completed.Library.Outcome.Receipt.SourceSettlements.Count);
        LibraryReference library = completed.Library.Value.Reference;
        Assert.NotNull(library.ImplementationAssembly);
        using (LibraryOperationLease operation =
            Issued(completed.Library.Owner, library))
        {
            Assert.Equal(
                ((byte)'M', (byte)'M'),
                operation.SnapshotPair(
                    library.ApiAssembly,
                    library.ImplementationAssembly!,
                    static (view, _) =>
                        (view.First.Content[0],
                            view.Second.Content[0]),
                    cancellationToken));
        }

        await environment.AssertSettledAsync();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        await completed.Library.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        SelectedPackageReferencePopulationCompletesFromDiscoveryAssociation()
    {
        const string version = "10.0.12";
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] runtime =
            PackagePlatformTestData.Assembly("System.Runtime");
        byte[] json =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        version,
                        [
                            PackagePlatformTestData.Entry(
                                "ref/net10.0/System.Runtime.dll",
                                runtime),
                            PackagePlatformTestData.Entry(
                                "ref/net10.0/System.Text.Json.dll",
                                json),
                        ])),
            ]);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            SelectedPackageReferencePopulationRequest(
                adapter,
                cancellationToken);

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PlatformHouseSelectedReferencePopulationExecutor
                    .ExecuteAsync(
                        request,
                        [
                            PackagePlatformTargetDiscovery.CreateSource(
                                adapter,
                                (operation, remainingWork) =>
                                    environment.IssueOperation(
                                        operation.CancellationToken,
                                        operationTimeout:
                                            remainingWork.MaxDuration)),
                        ],
                        [
                            PackagePlatformSelectedReferencePopulationRealization
                                .CreateSource(
                                    adapter,
                                    (operation, remainingWork) =>
                                        environment.IssueOperation(
                                            operation.CancellationToken,
                                            operationTimeout:
                                                remainingWork.MaxDuration),
                                    PlatformHouseCandidateIdentity.Create(
                                        "package-reference-population-candidate")),
                        ],
                        "package-selected-reference-population"));

        Assert.Equal(
            ["System.Runtime", "System.Text.Json"],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(
            version,
            completed.Population.Outcome.Receipt.TargetSettlement
                .SettledTarget!.Version.Value);
        Assert.Collection(
            completed.Population.Outcome.Receipt.SourceSettlements,
            discovery => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                discovery.Disposition),
            realization => Assert.Equal(
                PlatformSourceSettlementDisposition.Selected,
                realization.Disposition));

        await environment.AssertSettledAsync();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        foreach (LibraryContentOwner owner
            in completed.Population.Owners)
        {
            await owner.DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
    }

    [Fact]
    public async Task
        ForeignPopulationAssociationRejectsBeforePackageOperation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            SelectedPackageReferencePopulationRequest(
                adapter,
                cancellationToken);
        PlatformFamilyTarget target = new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.12"));
        int issuedOperations = 0;
        var foreignDiscovery = new PlatformTargetDiscoverySource(
            adapter.TargetDiscovery,
            (operation, _) =>
            {
                var contribution =
                    new PlatformSourceContribution.TargetDiscovery(
                        adapter.TargetDiscovery,
                        operation.Snapshot,
                        PlatformSourceGeneration.Create(
                            "foreign-package-generation"),
                        [target]);
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.Succeeded(
                        contribution,
                        [
                            new PlatformTargetDiscoveryCandidate<
                                ForeignAssociation>(
                                    target,
                                    new()),
                        ]);
                return ValueTask.FromResult(attempt);
            },
            adapter.AssociationRoute);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedReferencePopulationExecutor
                    .ExecuteAsync(
                        request,
                        [foreignDiscovery],
                        [
                            PackagePlatformSelectedReferencePopulationRealization
                                .CreateSource(
                                    adapter,
                                    (operation, remainingWork) =>
                                    {
                                        issuedOperations++;
                                        return environment.IssueOperation(
                                            operation.CancellationToken,
                                            operationTimeout:
                                                remainingWork.MaxDuration);
                                    },
                                    PlatformHouseCandidateIdentity.Create(
                                        "package-reference-population-candidate")),
                        ],
                        "package-selected-reference-population"));

        Assert.Equal(0, issuedOperations);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task
        ForeignAssociationOrRouteRejectsBeforePackageOperation(
            bool matchingRoute)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = SelectedPackageReferenceRequest(
            adapter,
            PackagePlatformTestData.Identity(image),
            cancellationToken);
        PlatformFamilyTarget target = new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net10.0"),
            PlatformVersion.Parse("10.0.12"));
        int issuedOperations = 0;
        var foreignDiscovery = new PlatformTargetDiscoverySource(
            adapter.TargetDiscovery,
            (operation, _) =>
            {
                var contribution =
                    new PlatformSourceContribution.TargetDiscovery(
                        adapter.TargetDiscovery,
                        operation.Snapshot,
                        PlatformSourceGeneration.Create(
                            "foreign-package-generation"),
                        [target]);
                PlatformTargetDiscoveryAttempt attempt =
                    new PlatformTargetDiscoveryAttempt.Succeeded(
                        contribution,
                        [
                            new PlatformTargetDiscoveryCandidate<
                                ForeignAssociation>(
                                    target,
                                    new()),
                        ]);
                return ValueTask.FromResult(attempt);
            },
            matchingRoute
                ? adapter.AssociationRoute
                : PlatformSourceAssociationRouteIdentity.Create(
                    "foreign-package-route"));

        var terminal = Assert.IsType<
            PlatformLibraryArtifactMaterializationOutcome.Terminal>(
                await PlatformHouseSelectedLibraryExecutor.ExecuteAsync(
                    request,
                    [foreignDiscovery],
                    [
                        PackagePlatformSelectedLibraryRealization
                            .CreateReferenceSource(
                                adapter,
                                (operation, remainingWork) =>
                                {
                                    issuedOperations++;
                                    return environment.IssueOperation(
                                        operation.CancellationToken,
                                        operationTimeout:
                                            remainingWork.MaxDuration);
                                },
                                PlatformHouseCandidateIdentity.Create(
                                    "package-reference-candidate")),
                    ],
                    "package-selected-library"));

        Assert.Equal(0, issuedOperations);
        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageReferencePopulation_TransfersOrderedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        Assert.Equal(
            ["System.Runtime", "System.Text.Json"],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(
            reference.Value.Libraries.Length,
            completed.Population.Owners.Count);
        Assert.Same(
            reference.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        for (int index = 0;
            index < reference.Value.Libraries.Length;
            index++)
        {
            PackageReferenceLibrary source =
                reference.Value.Libraries[index];
            LibraryReference library =
                completed.Population.Value.Libraries[index];
            Assert.Same(
                library,
                completed.Population.Owners[index].Reference);
            Assert.Null(library.ImplementationAssembly);
            Assert.Single(library.Contents);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    library.ApiAssembly.AssemblyIdentity!.Identity));
            var provenance =
                Assert.IsType<PackageReferenceArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ApiAssembly.ArtifactReference
                                .Provenance)
                        .SourceProvenance);
            Assert.Same(
                reference.Value.Generation,
                provenance.SourceGeneration);
            Assert.Same(reference.Value.Coordinate, provenance.Coordinate);
            Assert.Equal(source.Path, provenance.Path);
            Assert.Same(reference.Value.Candidate, provenance.Candidate);
            Assert.Same(reference.Value.Authority, provenance.Authority);
            Assert.Same(reference.Value.Source, provenance.Source);
            Assert.Same(
                reference.Value.ContentGeneration,
                provenance.ContentGeneration);
            Assert.Equal(reference.Value.Origin, provenance.Origin);
            using LibraryOperationLease operation = Issued(
                completed.Population.Owners[index],
                library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        await environment.AssertSettledAsync();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Population.Owners[0].DisposeAsync();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Population.Owners[1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        PackageReferencePopulation_RejectsForeignContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest sourceRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        PlatformHouseRequest materializationRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        materializationRequest,
                        reference,
                        Consumed(reference.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageReferencePopulation_IncompleteWorkTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseConsumedWork consumed = Consumed(reference.Value);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        new PlatformHouseConsumedWork(
                            consumed.SourceOperations,
                            consumed.TargetCandidates,
                            request.Work.MaxAssemblies + 1,
                            consumed.XmlDocuments,
                            consumed.PortablePdbs,
                            consumed.SourceDocuments,
                            consumed.Bytes,
                            consumed.ForwardingHops,
                            consumed.TargetComparisons,
                            consumed.Elapsed)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageReferencePopulation_CancellationTransfersNoAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellation.Token,
            PlatformViewDemand.Reference);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferencePopulationAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_TransfersOrderedLibraryAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        Consumed(implementation.Value)));

        Assert.Equal(
            ["System.Net.Http", "System.Text.Json"],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(
            implementation.Value.Libraries.Select(
                static library => library.Identity),
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity),
            AssemblyReferenceIdentity.EquivalentComparer);
        Assert.Equal(
            implementation.Value.Libraries.Length,
            completed.Population.Owners.Count);
        Assert.Same(
            implementation.Contribution,
            Assert.Single(
                    completed.Population.Receipt.HouseReceipt
                        .SourceSettlements)
                .Contribution);
        Assert.NotNull(
            Assert.IsType<PlatformHouseCompletion.Realization>(
                    completed.Population.Receipt.HouseReceipt.Completion)
                .ViewCorrespondence);
        for (int index = 0;
            index < implementation.Value.Libraries.Length;
            index++)
        {
            PackageImplementationLibrary source =
                implementation.Value.Libraries[index];
            LibraryReference library =
                completed.Population.Value.Libraries[index];
            Assert.Same(
                library,
                completed.Population.Owners[index].Reference);
            Assert.Same(
                library.ApiAssembly,
                library.ImplementationAssembly);
            Assert.Equal(2, library.ApiAssembly.Roles.Count);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    library.ApiAssembly.AssemblyIdentity!.Identity));
            var provenance =
                Assert.IsType<PackageImplementationArtifactProvenance>(
                    Assert.IsType<PlatformLibraryArtifactProvenance>(
                            library.ApiAssembly.ArtifactReference
                                .Provenance)
                        .SourceProvenance);
            Assert.Same(
                implementation.Value.Generation,
                provenance.SourceGeneration);
            Assert.Same(
                implementation.Value.Coordinate,
                provenance.Coordinate);
            Assert.Equal(source.Framework.Name, provenance.FrameworkName);
            Assert.Equal(
                source.Framework.Family,
                provenance.FrameworkFamily);
            Assert.Equal(
                source.Framework.Version,
                provenance.FrameworkVersion);
            Assert.Equal(source.Framework.PackageId, provenance.PackageId);
            Assert.Equal(
                source.Framework.RuntimeIdentifier,
                provenance.RuntimeIdentifier);
            Assert.Equal(
                source.ManifestCoordinate,
                provenance.ManifestCoordinate);
            Assert.Same(source.Framework.Candidate, provenance.Candidate);
            Assert.Same(source.Framework.Authority, provenance.Authority);
            Assert.Same(source.Framework.Source, provenance.Source);
            Assert.Same(
                source.Framework.ContentGeneration,
                provenance.ContentGeneration);
            Assert.Equal(source.Framework.Origin, provenance.Origin);
            Assert.True(
                AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.Identity,
                    provenance.Identity));
            Assert.Equal(source.ContentDigest, provenance.ContentDigest);
            using LibraryOperationLease operation = Issued(
                completed.Population.Owners[index],
                library);
            Assert.Equal(
                (byte)'M',
                operation.Snapshot(
                    library.ApiAssembly,
                    static (view, _) => view.Content[0],
                    cancellationToken));
        }

        await environment.AssertSettledAsync();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Population.Owners[0].DisposeAsync();
        Assert.False(artifactRetirement.IsCompleted);
        await completed.Population.Owners[1].DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_RejectsForeignContribution()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest sourceRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        PlatformHouseRequest materializationRequest = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        sourceRequest,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        materializationRequest,
                        implementation,
                        Consumed(implementation.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_IncompleteWorkTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellationToken,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseConsumedWork consumed =
            Consumed(implementation.Value);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        new PlatformHouseConsumedWork(
                            consumed.SourceOperations,
                            consumed.TargetCandidates,
                            request.Work.MaxAssemblies + 1,
                            consumed.XmlDocuments,
                            consumed.PortablePdbs,
                            consumed.SourceDocuments,
                            consumed.Bytes,
                            consumed.ForwardingHops,
                            consumed.TargetComparisons,
                            consumed.Elapsed)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageImplementationPopulation_CancellationTransfersNoAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = PopulationRequest(
            adapter,
            cancellation.Token,
            PlatformViewDemand.Implementation);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationPopulationAsync(
                        request,
                        implementation,
                        Consumed(implementation.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackagePairedPopulation_TransfersLosslessUnionAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            reference.Value,
                            implementation.Value)));

        Assert.Equal(
            [
                "System.Runtime",
                "System.Text.Json",
                "System.Net.Http",
            ],
            completed.Population.Value.Libraries.Select(
                static library =>
                    library.ApiAssembly.AssemblyIdentity!.Identity.Name));
        Assert.Equal(3, completed.Population.Owners.Count);
        Assert.Equal(
            [reference.Contribution, implementation.Contribution],
            completed.Population.Receipt.HouseReceipt.SourceSettlements
                .Select(static settlement => settlement.Contribution));

        LibraryReference referenceOnly =
            completed.Population.Value.Libraries[0];
        Assert.Null(referenceOnly.ImplementationAssembly);
        Assert.Single(referenceOnly.Contents);
        var referenceProvenance =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                Assert.IsType<PlatformLibraryArtifactProvenance>(
                        referenceOnly.ApiAssembly.ArtifactReference.Provenance)
                    .SourceProvenance);
        Assert.Same(
            reference.Value.Candidate,
            referenceProvenance.Candidate);
        Assert.Same(
            reference.Value.Authority,
            referenceProvenance.Authority);
        Assert.Same(
            reference.Value.ContentGeneration,
            referenceProvenance.ContentGeneration);

        LibraryReference paired =
            completed.Population.Value.Libraries[1];
        Assert.Equal(2, paired.Contents.Count);
        Assert.NotSame(
            paired.ApiAssembly,
            paired.ImplementationAssembly);

        LibraryReference implementationOnly =
            completed.Population.Value.Libraries[2];
        Assert.Same(
            implementationOnly.ApiAssembly,
            implementationOnly.ImplementationAssembly);
        Assert.Equal(2, implementationOnly.ApiAssembly.Roles.Count);
        PackageImplementationLibrary sourceImplementation =
            Assert.Single(
                implementation.Value.Libraries,
                static library =>
                    library.Identity.Name == "System.Net.Http");
        var implementationProvenance =
            Assert.IsType<PackageImplementationArtifactProvenance>(
                Assert.IsType<PlatformLibraryArtifactProvenance>(
                        implementationOnly.ApiAssembly.ArtifactReference
                            .Provenance)
                    .SourceProvenance);
        Assert.Same(
            sourceImplementation.Framework.Candidate,
            implementationProvenance.Candidate);
        Assert.Same(
            sourceImplementation.Framework.Authority,
            implementationProvenance.Authority);
        Assert.Same(
            sourceImplementation.Framework.ContentGeneration,
            implementationProvenance.ContentGeneration);
        Assert.Equal(
            sourceImplementation.ContentDigest,
            implementationProvenance.ContentDigest);

        await environment.AssertSettledAsync();
        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        for (int index = 0;
            index < completed.Population.Owners.Count;
            index++)
        {
            Assert.Same(
                completed.Population.Value.Libraries[index],
                completed.Population.Owners[index].Reference);
            await completed.Population.Owners[index].DisposeAsync();
        }
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        PackagePairedPopulation_RejectsForeignImplementation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);
        PlatformHouseRequest foreign =
            PopulationRequest(adapter, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        foreign,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                foreign.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            reference.Value,
                            implementation.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackagePairedPopulation_IncompleteWorkTransfersNoAuthority()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PlatformHouseConsumedWork consumed =
            Consumed(reference.Value, implementation.Value);

        var terminal = Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        new PlatformHouseConsumedWork(
                            consumed.SourceOperations,
                            consumed.TargetCandidates,
                            request.Work.MaxAssemblies + 1,
                            consumed.XmlDocuments,
                            consumed.PortablePdbs,
                            consumed.SourceDocuments,
                            consumed.Bytes,
                            consumed.ForwardingHops,
                            consumed.TargetComparisons,
                            consumed.Elapsed)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformPopulationRealizationValue>.Incomplete>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackagePairedPopulation_CancellationTransfersNoAuthority()
    {
        using var cancellation = new CancellationTokenSource();
        await using PackagePlatformTestEnvironment environment =
            PopulationEnvironment();
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request =
            PopulationRequest(adapter, cancellation.Token);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationPopulationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(
                            reference.Value,
                            implementation.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        PackageSystemTextJson_TransfersLibraryAndArtifactAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] referenceImage =
            PackagePlatformTestData.Assembly(
                "System.Text.Json",
                new Version(11, 0, 0, 0));
        byte[] implementationImage =
            PackagePlatformTestData.Assembly(
                "System.Text.Json",
                new Version(11, 0, 0, 0));
        await using PackagePlatformTestEnvironment environment =
            Environment(referenceImage, implementationImage);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(referenceImage);
        PlatformHouseRequest request = Request(
            adapter,
            identity,
            PlatformViewDemand.ReferenceAndImplementation,
            cancellationToken);

        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAndImplementationAsync(
                        request,
                        reference,
                        implementation,
                        Consumed(reference.Value, implementation.Value)));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            library.ImplementationAssembly!.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.True(
            AssemblyReferenceIdentity.EquivalentComparer.Equals(
                identity,
                Assert.IsType<ManagedMetadataIdentity.Assembly>(
                        library.ApiAssembly.AssemblyIdentity)
                    .Identity));

        var referenceProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ApiAssembly.ArtifactReference.Provenance);
        Assert.Same(
            reference.Contribution,
            referenceProvenance.Contribution);
        var packageReference =
            Assert.IsType<PackageReferenceArtifactProvenance>(
                referenceProvenance.SourceProvenance);
        Assert.Same(
            reference.Value.Generation,
            packageReference.SourceGeneration);
        Assert.Same(
            reference.Value.Candidate,
            packageReference.Candidate);
        Assert.Same(
            reference.Value.Authority,
            packageReference.Authority);
        Assert.Same(
            reference.Value.Source,
            packageReference.Source);
        Assert.Same(
            reference.Value.ContentGeneration,
            packageReference.ContentGeneration);

        var implementationProvenance =
            Assert.IsType<PlatformLibraryArtifactProvenance>(
                library.ImplementationAssembly.ArtifactReference
                    .Provenance);
        Assert.Same(
            implementation.Contribution,
            implementationProvenance.Contribution);
        var packageImplementation =
            Assert.IsType<PackageImplementationArtifactProvenance>(
                implementationProvenance.SourceProvenance);
        PackageImplementationLibrary implementationLibrary =
            Assert.Single(implementation.Value.Libraries);
        Assert.Same(
            implementation.Value.Generation,
            packageImplementation.SourceGeneration);
        Assert.Same(
            implementationLibrary.Framework.Candidate,
            packageImplementation.Candidate);
        Assert.Same(
            implementationLibrary.Framework.ContentGeneration,
            packageImplementation.ContentGeneration);
        Assert.Equal(
            implementationLibrary.ContentDigest,
            packageImplementation.ContentDigest);

        Task artifactRetirement =
            completed.Artifacts.DisposeAsync().AsTask();
        Assert.False(artifactRetirement.IsCompleted);
        using LibraryOperationLease operation = Issued(
            completed.Library.Owner,
            library);
        Assert.Equal(
            ((byte)'M', (byte)'M'),
            operation.SnapshotPair(
                library.ApiAssembly,
                library.ImplementationAssembly,
                static (view, _) =>
                    (view.First.Content[0],
                        view.Second.Content[0]),
                cancellationToken));
        operation.Dispose();
        await completed.Library.Owner.DisposeAsync();
        await artifactRetirement.WaitAsync(cancellationToken);
        Assert.Empty(completed.Artifacts.CleanupFailures);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task PackageReferenceOnly_ClosesOneApiRole()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        Assert.Null(
            completed.Library.Value.Reference
                .ImplementationAssembly);
        Assert.Single(
            completed.Library.Value.Reference.Contents);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        InMemoryReferenceDocumentation_BecomesExactPlatformCandidate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] referenceImage = await File.ReadAllBytesAsync(
            FindReferenceAsset("System.Text.Json.dll"),
            cancellationToken);
        byte[] documentation = await File.ReadAllBytesAsync(
            FindReferenceAsset("System.Text.Json.xml"),
            cancellationToken);
        await using PackagePlatformTestEnvironment environment =
            Environment(
                referenceImage,
                referenceDocumentation: documentation);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(referenceImage),
            PlatformViewDemand.Reference,
            cancellationToken,
            includeCompiledXmlDocumentation: true);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PackageReferenceLibrary sourceLibrary =
            Assert.Single(reference.Value.Libraries);
        Assert.NotNull(sourceLibrary.Documentation);
        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        try
        {
            LibraryReference library =
                completed.Library.Value.Reference;
            CompiledXmlContribution contribution =
                PlatformDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        completed.Library.Receipt,
                        Subject(completed.Library));

            Assert.Equal(
                CompiledXmlContributionKind.Candidate,
                contribution.Kind);
            Assert.Same(
                library.ApiAssembly,
                contribution.CompiledXmlContent!
                    .AssociatedAssembly);
            var provenance =
                Assert.IsType<
                    PlatformLibraryArtifactProvenance>(
                        contribution.CompiledXmlContent
                            .Provenance);
            var package =
                Assert.IsType<
                    PackageReferenceDocumentationArtifactProvenance>(
                        provenance.SourceProvenance);
            Assert.Equal(
                "ref/net11.0/System.Text.Json.xml",
                package.Path);
        }
        finally
        {
            await completed.Library.Owner.DisposeAsync();
            await completed.Artifacts.DisposeAsync();
        }
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task
        InMemoryEmptyReferenceDocumentation_RemainsExactPlatformCandidate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] referenceImage = await File.ReadAllBytesAsync(
            FindReferenceAsset("System.Text.Json.dll"),
            cancellationToken);
        await using PackagePlatformTestEnvironment environment =
            Environment(
                referenceImage,
                referenceDocumentation: []);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(referenceImage),
            PlatformViewDemand.Reference,
            cancellationToken,
            includeCompiledXmlDocumentation: true);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));
        PackageReferenceLibrary sourceLibrary =
            Assert.Single(reference.Value.Libraries);
        Assert.Equal(
            0,
            Assert.IsType<PackageReferenceDocumentation>(
                    sourceLibrary.Documentation)
                .ContentLength);
        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        try
        {
            LibraryReference library =
                completed.Library.Value.Reference;
            CompiledXmlContribution contribution =
                PlatformDocumentationHouseAdapter
                    .CreateCompiledXmlContribution(
                        completed.Library.Receipt,
                        Subject(completed.Library));

            Assert.Equal(
                CompiledXmlContributionKind.Candidate,
                contribution.Kind);
            using LibraryOperationLease operation = Issued(
                completed.Library.Owner,
                library);
            Assert.Equal(
                0,
                operation.Snapshot(
                    contribution.CompiledXmlContent!,
                    static (view, _) => view.Content.Length,
                    cancellationToken));
        }
        finally
        {
            await completed.Library.Owner.DisposeAsync();
            await completed.Artifacts.DisposeAsync();
        }
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task PackageImplementationOnly_AssignsBothRoles()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(referenceImage: null, implementationImage: image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Implementation,
            cancellationToken);
        var implementation = Assert.IsType<
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded>(
                    await adapter.RealizeImplementationAsync(
                        request,
                        "linux-x64",
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var completed = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Completed>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeImplementationAsync(
                        request,
                        implementation,
                        Consumed(implementation.Value)));

        LibraryReference library = completed.Library.Value.Reference;
        Assert.Same(
            library.ApiAssembly,
            library.ImplementationAssembly);
        Assert.Equal(2, library.ApiAssembly.Roles.Count);
        await completed.Library.Owner.DisposeAsync();
        await completed.Artifacts.DisposeAsync();
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task ForeignSuccessfulResult_IsRejectedBeforePublication()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        AssemblyReferenceIdentity identity =
            PackagePlatformTestData.Identity(image);
        PlatformHouseRequest sourceRequest = Request(
            adapter,
            identity,
            PlatformViewDemand.Reference,
            cancellationToken);
        PlatformHouseRequest materializationRequest = Request(
            adapter,
            identity,
            PlatformViewDemand.Reference,
            cancellationToken);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        sourceRequest,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                sourceRequest.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        materializationRequest,
                        reference,
                        Consumed(reference.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task MissingPriorSourceEvidence_ReturnsTerminalAfterCleanup()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Reference,
            cancellationToken,
            packageFirst: false);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellationToken,
                            operationTimeout:
                                request.Work.MaxDuration)));

        var terminal = Assert.IsType<
            PackagePlatformLibraryMaterializationResult.Terminal>(
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));

        Assert.IsType<
            PlatformHouseOutcome<
                PlatformLibraryRealizationValue>.Rejected>(
                    terminal.TerminalRealization.Outcome);
        await environment.AssertSettledAsync();
    }

    [Fact]
    public async Task CancellationPrecedesArtifactOwnership()
    {
        byte[] image =
            PackagePlatformTestData.Assembly("System.Text.Json");
        await using PackagePlatformTestEnvironment environment =
            Environment(image);
        PackagePlatformHouseAdapter adapter = Adapter(environment);
        using var cancellation = new CancellationTokenSource();
        PlatformHouseRequest request = Request(
            adapter,
            PackagePlatformTestData.Identity(image),
            PlatformViewDemand.Reference,
            cancellation.Token);
        var reference = Assert.IsType<
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded>(
                    await adapter.RealizeReferenceAsync(
                        request,
                        environment.IssueOperation(
                            cancellation.Token,
                            operationTimeout:
                                request.Work.MaxDuration)));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () =>
                await PackagePlatformLibraryMaterializer
                    .MaterializeReferenceAsync(
                        request,
                        reference,
                        Consumed(reference.Value)));
        await environment.AssertSettledAsync();
    }

    [Fact]
    public void PackageArtifactProvenance_IsResourceFree()
    {
        Type[] types =
        [
            typeof(PackageReferenceArtifactProvenance),
            typeof(PackageImplementationArtifactProvenance),
            typeof(PackagePlatformLibraryMaterializationResult.Terminal),
            typeof(PlatformPopulationArtifactMaterializationOutcome.Terminal),
        ];

        foreach (Type type in types)
        {
            Assert.Null(
                type.GetCustomAttribute<ResourceOwnershipAttribute>());
            Assert.All(
                type.GetFields(
                    BindingFlags.Instance
                    | BindingFlags.Public
                    | BindingFlags.NonPublic),
                field =>
                {
                    Assert.False(
                        typeof(IDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(IAsyncDisposable).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Stream).IsAssignableFrom(
                            field.FieldType));
                    Assert.False(
                        typeof(Delegate).IsAssignableFrom(
                            field.FieldType));
                });
        }
    }

    [Fact]
    public void SuccessfulSourcePairing_IsAdapterIssued()
    {
        Type[] succeededResults =
        [
            typeof(PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded),
            typeof(PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded),
        ];

        foreach (Type result in succeededResults)
        {
            Assert.Empty(
                result.GetConstructors(
                    BindingFlags.Instance
                    | BindingFlags.Public));
        }
    }

    static PackagePlatformTestEnvironment Environment(
        byte[]? referenceImage,
        byte[]? implementationImage = null,
        byte[]? referenceDocumentation = null)
    {
        var packages = new List<(
            string PackageId,
            string Version,
            IReadOnlyList<KeyValuePair<string, byte[]>> Entries)>();
        if (referenceImage is not null)
        {
            var entries =
                new List<KeyValuePair<string, byte[]>>
                {
                    PackagePlatformTestData.Entry(
                        "ref/net11.0/System.Text.Json.dll",
                        referenceImage),
                };
            if (referenceDocumentation is not null)
            {
                entries.Add(
                    PackagePlatformTestData.Entry(
                        "ref/net11.0/System.Text.Json.xml",
                        referenceDocumentation));
            }
            packages.Add(
                (
                    PackagePlatformTestEnvironment.RuntimePackageId,
                    PackagePlatformTestEnvironment.Version,
                    entries));
        }
        if (implementationImage is not null)
        {
            packages.Add(
                (
                    PackagePlatformTestEnvironment
                        .RuntimeImplementationPackageId,
                    PackagePlatformTestEnvironment.Version,
                    PackagePlatformTestData.RuntimePackEntries(
                        "Microsoft.NETCore.App",
                        PackagePlatformTestData.RuntimeConfiguration(),
                        PackagePlatformTestData.DependencyManifest(
                            "System.Text.Json.dll"),
                        ("System.Text.Json.dll", implementationImage))));
        }
        return PackagePlatformTestEnvironment.Create(
            [TestSourceBehavior.CreatePackages([.. packages])]);
    }

    static PackagePlatformTestEnvironment PopulationEnvironment()
    {
        byte[] json =
            PackagePlatformTestData.Assembly("System.Text.Json");
        byte[] runtime =
            PackagePlatformTestData.Assembly("System.Runtime");
        byte[] http =
            PackagePlatformTestData.Assembly("System.Net.Http");
        var referenceEntries =
            new List<KeyValuePair<string, byte[]>>
            {
                PackagePlatformTestData.Entry(
                    "ref/net11.0/System.Runtime.dll",
                    runtime),
                PackagePlatformTestData.Entry(
                    "ref/net11.0/System.Text.Json.dll",
                    json),
            };
        return PackagePlatformTestEnvironment.Create(
            [
                TestSourceBehavior.CreatePackages(
                    (
                        PackagePlatformTestEnvironment.RuntimePackageId,
                        PackagePlatformTestEnvironment.Version,
                        referenceEntries),
                    (
                        PackagePlatformTestEnvironment
                            .RuntimeImplementationPackageId,
                        PackagePlatformTestEnvironment.Version,
                        PackagePlatformTestData.RuntimePackEntries(
                            "Microsoft.NETCore.App",
                            PackagePlatformTestData.RuntimeConfiguration(),
                            PackagePlatformTestData.DependencyManifest(
                                "System.Text.Json.dll",
                                "System.Net.Http.dll"),
                            ("System.Text.Json.dll", json),
                            ("System.Net.Http.dll", http)))),
            ]);
    }

    static PackagePlatformHouseAdapter Adapter(
        PackagePlatformTestEnvironment environment) =>
        new(environment.CreateSource(), "package-materializer");

    sealed class PackagePopulationCapability :
        IEcosystemPlatformPopulationCapability
    {
        readonly PackagePlatformTestEnvironment _environment;
        readonly PackagePlatformHouseAdapter _adapter;

        internal PackagePopulationCapability(
            EcosystemPopulationCapabilityPlanIdentity planIdentity,
            PackagePlatformTestEnvironment environment,
            PackagePlatformHouseAdapter adapter)
        {
            PlanIdentity = planIdentity;
            _environment = environment;
            _adapter = adapter;
        }

        public EcosystemPopulationCapabilityPlanIdentity PlanIdentity
        {
            get;
        }

        public async ValueTask<
            PlatformPopulationArtifactMaterializationOutcome> RealizeAsync(
                PlatformLibraryPopulationDeclaration declaration,
                CancellationToken cancellationToken)
        {
            Assert.Equal(PlatformFamily.DotNetRuntime, declaration.Family);
            PlatformHouseRequest request = PopulationRequest(
                _adapter,
                cancellationToken,
                PlatformViewDemand.Reference);
            var reference = Assert.IsType<
                PackagePlatformHouseResult<
                    PackageReferenceRealization>.Succeeded>(
                        await _adapter.RealizeReferenceAsync(
                            request,
                            _environment.IssueOperation(
                                cancellationToken,
                                operationTimeout:
                                    request.Work.MaxDuration)));
            return await PackagePlatformLibraryMaterializer
                .MaterializeReferencePopulationAsync(
                    request,
                    reference,
                    Consumed(reference.Value));
        }
    }

    static PlatformHouseRequest ImplementationFallbackRequest(
        PackagePlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity fallbackImplementation,
        AssemblyReferenceIdentity identity,
        long maximumBytes,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "package-implementation-fallback"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred: null,
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [adapter.TargetDiscovery])),
                new PlatformTargetDiscoveryBudget(32, 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "package-implementation-fallback")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.Implementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "package-implementation-fallback-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [adapter.TargetDiscovery]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.ImplementationRealization,
                            fallbackImplementation,
                        ]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 3,
                maxTargetCandidates: 32,
                maxAssemblies: 2,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: maximumBytes,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedPackageReferenceFailureRequest(
        PackagePlatformHouseAdapter adapter,
        PlatformSourceCapabilityIdentity fallbackReference,
        AssemblyReferenceIdentity identity,
        long maximumBytes,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-package-reference-failure"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred: null,
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [adapter.TargetDiscovery])),
                new PlatformTargetDiscoveryBudget(32, 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-package-reference-failure")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.Reference,
                PlatformLibraryContentDemand
                    .CompiledXmlDocumentation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-package-reference-failure-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [adapter.TargetDiscovery]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Fallback,
                        [
                            adapter.ReferenceRealization,
                            fallbackReference,
                        ]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 3,
                maxTargetCandidates: 32,
                maxAssemblies: 1,
                maxXmlDocuments: 1,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: maximumBytes,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedPackageReferenceRequest(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-package-reference"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred: null,
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [adapter.TargetDiscovery])),
                new PlatformTargetDiscoveryBudget(32, 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-package-reference")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-package-reference-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [adapter.TargetDiscovery]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 4,
                maxTargetCandidates: 32,
                maxAssemblies: 8,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 256L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedPackageReferencePopulationRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-package-reference-population"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred: null,
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [adapter.TargetDiscovery])),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 32,
                    maxComparisons: 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-package-reference-population")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                PlatformViewDemand.Reference),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-package-reference-population-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [adapter.TargetDiscovery]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 4,
                maxTargetCandidates: 32,
                maxAssemblies: 512,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 64L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest SelectedPackageRequest(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        CancellationToken cancellationToken) =>
        new(
            PlatformHouseRequestIdentity.Create(
                "selected-package-library"),
            new PlatformTargetDemand.FamilyDefault(
                PlatformFamily.DotNetRuntime,
                new PlatformVersionlessRuntimeTargetPolicy(
                    PlatformTargetSelectionPolicyIdentity.Create(
                        "versionless-runtime-default"),
                    PlatformTargetSelectionPolicyGeneration.Create(
                        "generation-1"),
                    PlatformVersion.Parse("10.0.1"),
                    preferred: null,
                    new PlatformTargetDiscoveryStage(
                        new PlatformTargetDiscoveryScope.ExactFramework(
                            PlatformTargetFramework.Parse("net10.0")),
                        [adapter.TargetDiscovery])),
                new PlatformTargetDiscoveryBudget(
                    maxCandidates: 32,
                    maxComparisons: 128)),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "selected-package-library")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                PlatformViewDemand.ReferenceAndImplementation),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "selected-package-sources"),
                PlatformSourcePolicyGeneration.Create("generation-1"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.TargetDiscovery,
                        PlatformSourceSelectionMode.Fallback,
                        [adapter.TargetDiscovery]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Reference,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ReferenceRealization]),
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [adapter.ImplementationRealization]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 8,
                maxTargetCandidates: 32,
                maxAssemblies: 16,
                maxXmlDocuments: 0,
                maxPortablePdbs: 0,
                maxSourceDocuments: 0,
                maxBytes: 256L * 1024 * 1024,
                maxForwardingHops: 0,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);

    static PlatformHouseRequest Request(
        PackagePlatformHouseAdapter adapter,
        AssemblyReferenceIdentity identity,
        PlatformViewDemand view,
        CancellationToken cancellationToken,
        bool packageFirst = true,
        bool includeCompiledXmlDocumentation = false)
    {
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.ReferenceRealization,
                        packageFirst)));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    Capabilities(
                        adapter.ImplementationRealization,
                        packageFirst)));
        }

        return new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("package-library"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.Library(
                    new PlatformLibraryDemand.Assembly(identity)),
                view,
                includeCompiledXmlDocumentation
                    ? PlatformLibraryContentDemand
                        .CompiledXmlDocumentation
                    : PlatformLibraryContentDemand.None),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "package-policy"),
                selections),
            Work(
                includeCompiledXmlDocumentation
                    ? 1
                    : 0),
            cancellationToken);
    }

    static PlatformHouseRequest PopulationRequest(
        PackagePlatformHouseAdapter adapter,
        CancellationToken cancellationToken,
        PlatformViewDemand view =
            PlatformViewDemand.ReferenceAndImplementation)
    {
        var selections = new List<PlatformSourceSelection>();
        if (view is PlatformViewDemand.Reference
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Reference,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ReferenceRealization]));
        }
        if (view is PlatformViewDemand.Implementation
            or PlatformViewDemand.ReferenceAndImplementation)
        {
            selections.Add(
                new PlatformSourceSelection(
                    PlatformSourceFacet.Implementation,
                    PlatformSourceSelectionMode.Precedence,
                    [adapter.ImplementationRealization]));
        }

        return
        new(
            PlatformHouseRequestIdentity.Create("package-population"),
            new PlatformTargetDemand.Exact(Target()),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create("test")),
            new PlatformHouseOperation.Realize(
                new PlatformPopulationDemand.CompletePopulation(),
                view),
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create("package-population-plan"),
                PlatformSourcePolicyGeneration.Create(
                    "package-population-policy"),
                selections),
            Work(),
            cancellationToken);
    }

    static IReadOnlyList<PlatformSourceCapabilityIdentity> Capabilities(
        PlatformSourceCapabilityIdentity package,
        bool packageFirst) =>
        packageFirst
            ? [package]
            :
            [
                PlatformSourceCapabilityIdentity.Create("prior"),
                package,
            ];

    static PlatformHouseWorkBudget Work(
        int maxXmlDocuments = 0) =>
        new(
            maxSourceOperations: 8,
            maxTargetCandidates: 0,
            maxAssemblies: 512,
            maxXmlDocuments,
            maxPortablePdbs: 0,
            maxSourceDocuments: 0,
            maxBytes: 64 * 1024 * 1024,
            maxForwardingHops: 0,
            maxDuration: TimeSpan.FromSeconds(30));

    static PlatformHouseConsumedWork Consumed(
        PackageReferenceRealization reference) =>
        Consumed(
            sourceOperations: 1,
            assemblies: reference.Libraries.Length,
            bytes: reference.Libraries.Sum(
                static library =>
                    library.TotalContentLength),
            xmlDocuments: reference.Libraries.Count(
                static library =>
                    library.Documentation is not null));

    static PlatformHouseConsumedWork Consumed(
        PackageImplementationRealization implementation) =>
        Consumed(
            sourceOperations: implementation.Frameworks.Length,
            assemblies: implementation.Libraries.Length,
            bytes: implementation.Libraries.Sum(
                static library => library.ContentLength));

    static PlatformHouseConsumedWork Consumed(
        PackageReferenceRealization reference,
        PackageImplementationRealization implementation) =>
        Consumed(
            sourceOperations: 1 + implementation.Frameworks.Length,
            assemblies:
                reference.Libraries.Length
                + implementation.Libraries.Length,
            bytes:
                reference.Libraries.Sum(
                    static library => library.ContentLength)
                + implementation.Libraries.Sum(
                    static library => library.ContentLength));

    static PlatformHouseConsumedWork Consumed(
        int sourceOperations,
        int assemblies,
        long bytes,
        int xmlDocuments = 0) =>
        new(
            sourceOperations,
            targetCandidates: 0,
            assemblies,
            xmlDocuments,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    static DocumentationSubjectReference Subject(
        PlatformLibraryRealizationResult.Completed materialized)
    {
        LibraryReference library = materialized.Value.Reference;
        using LibraryOperationLease operation =
            Issued(materialized.Owner, library);
        var request = new LibraryApiSurfaceInspectionRequest(
            library,
            ApiSurfaceExtractionScope.Public,
            s_documentationApiSurfaceBounds);
        LibraryApiSurfaceCorrespondence correspondence =
            Assert.IsType<
                LibraryApiSurfaceInspectionOutcome.Completed>(
                    LibraryApiSurfaceInspection.Execute(
                        request,
                        operation,
                        TestContext.Current
                            .CancellationToken))
                .Correspondence;
        ApiType type = Assert.Single(
            correspondence.Surface.Types,
            candidate =>
                candidate.FullName
                    == "System.Text.Json.JsonSerializer");
        return DocumentationSubjectReference.ForType(
            correspondence,
            type);
    }

    static string FindReferenceAsset(string fileName)
    {
        DirectoryInfo runtimeVersion =
            new FileInfo(typeof(object).Assembly.Location)
                .Directory
            ?? throw new InvalidOperationException(
                "The runtime assembly location has no directory.");
        DirectoryInfo dotnetRoot =
            runtimeVersion.Parent?.Parent?.Parent
            ?? throw new InvalidOperationException(
                "The runtime assembly location is outside a dotnet root.");
        string referenceRoot = Path.Combine(
            dotnetRoot.FullName,
            "packs",
            "Microsoft.NETCore.App.Ref");
        return Directory.EnumerateFiles(
                referenceRoot,
                fileName,
                SearchOption.AllDirectories)
            .Where(
                path => string.Equals(
                    new FileInfo(path).Directory?.Name,
                    "net11.0",
                    StringComparison.Ordinal))
            .OrderByDescending(
                static path => path,
                StringComparer.Ordinal)
            .First();
    }

    static PlatformFamilyTarget Target() =>
        new(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse(
                PackagePlatformTestEnvironment.Version));

    static LibraryOperationLease Issued(
        LibraryContentOwner owner,
        LibraryReference reference) =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                owner.IssueOperationLease(reference))
            .Lease;

    sealed class ForeignAssociation;
}
