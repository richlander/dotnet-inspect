using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceTopLevelInventoryTests
{
    [Fact]
    public async Task Query_ProjectsCompleteOrderedInventoryAndOverlap()
    {
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateMixedAsync();

        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                fixture.Lease.Scope,
                WorkspaceTopLevelInventoryRequest.All);

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Outcome);
        WorkspaceTopLevelInventoryDocument document = available.Document;
        Assert.Null(document.Filter);
        Assert.Equal(6, document.TotalEntryCount);
        Assert.Equal(6, document.SelectedEntryCount);
        Assert.Equal(
            [
                WorkspaceTopLevelInventoryEntryKind.Package,
                WorkspaceTopLevelInventoryEntryKind.Package,
                WorkspaceTopLevelInventoryEntryKind.Package,
                WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
                WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
                WorkspaceTopLevelInventoryEntryKind.Ecosystem,
            ],
            document.Entries.Select(static entry => entry.Kind));
        Assert.Equal(
            [
                "package:0",
                "package:1",
                "package:2",
                "registration:0",
                "registration:1",
                "registration:2",
            ],
            document.Entries.Select(static entry => entry.Key.Value));
        Assert.Equal(
            [1, 2, 3, 1, 2, 3],
            document.Entries.Select(static entry => entry.SourceOrder));

        var package = Assert.IsType<WorkspaceTopLevelPackageEntry>(
            document.Entries[0]);
        Assert.Equal("system.text.json", package.PackageId);
        Assert.Equal("1.0.0", package.PackageVersion);
        Assert.Equal("tests", package.Producer);
        Assert.IsType<WorkspaceTopLevelPackageState.Ready>(package.State);

        var library = Assert.IsType<WorkspaceTopLevelExactLibraryEntry>(
            document.Entries[3]);
        var libraryPackage = Assert.IsType<
            WorkspaceTopLevelExactLibraryCoordinate.Package>(
                library.Coordinate);
        Assert.Equal("system.text.json", libraryPackage.PackageId);
        Assert.Equal("1.0.0", libraryPackage.PackageVersion);
        Assert.Equal(
            "System.Text.Json",
            libraryPackage.LibraryIdentity.Name);

        var prefix = Assert.IsType<WorkspaceTopLevelPackagePrefixEntry>(
            document.Entries[4]);
        Assert.Equal("Microsoft.Extensions.", prefix.Prefix.Prefix);

        var ecosystem = Assert.IsType<WorkspaceTopLevelEcosystemEntry>(
            document.Entries[5]);
        Assert.Equal("ecosystem.runtime", ecosystem.Id);
        Assert.Equal(["System", "Microsoft"], ecosystem.NamespaceRoots);
        Assert.Equal(
            ["System.Text.Json", "Microsoft.Extensions.Logging"],
            ecosystem.CorePackages.Select(
                static package => package.PackageId));
        Assert.Collection(
            ecosystem.Populations,
            population => Assert.IsType<
                WorkspaceTopLevelEcosystemPopulation.ExactLibrary>(
                    population),
            population => Assert.IsType<
                WorkspaceTopLevelEcosystemPopulation.Platform>(
                    population),
            population => Assert.IsType<
                WorkspaceTopLevelEcosystemPopulation.PackagePrefix>(
                    population));
        Assert.True(ecosystem.HasIntegrationScanner);
        Assert.True(execution.Selection.HasAuthority);
        Assert.Equal(6, execution.Selection.Count);
    }

    [Fact]
    public async Task Query_NormalizesFilterAndReceiptMapsOnlySelectedEntries()
    {
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        var request = new WorkspaceTopLevelInventoryRequest(
            new WorkspaceTopLevelInventoryKindFilter(
            [
                WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
                WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
                WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
            ]));

        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                fixture.Lease.Scope,
                request);

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Outcome);
        Assert.Equal(
            [
                WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
                WorkspaceTopLevelInventoryEntryKind.PackagePrefix,
            ],
            available.Document.Filter!.Kinds);
        Assert.Equal(6, available.Document.TotalEntryCount);
        Assert.Equal(2, available.Document.SelectedEntryCount);
        Assert.Equal(
            ["registration:0", "registration:1"],
            available.Document.Entries.Select(
                static entry => entry.Key.Value));
        Assert.Equal(2, execution.Selection.Count);

        var selected = Assert.IsType<
            WorkspaceTopLevelInventorySelectionResolution.Selected>(
                execution.Selection.Resolve(
                    fixture.Lease,
                    available.Document.Entries[0].Key));
        var registration = Assert.IsType<
            WorkspaceTopLevelInventorySelection.Registration>(
                selected.Selection);
        Assert.Equal(
            WorkspaceTopLevelInventoryEntryKind.ExactLibrary,
            registration.RegistrationKind);
        Assert.Equal(0, registration.SourceIndex);
        Assert.IsType<
            WorkspaceTopLevelInventorySelectionResolution.Absent>(
                execution.Selection.Resolve(
                    fixture.Lease,
                    new("package:0")));
    }

    [Fact]
    public async Task Query_ValidNoMatchReturnsAvailableEmptySelection()
    {
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateAsync(
                [ExactLibraryRegistration()],
                []);
        var request = new WorkspaceTopLevelInventoryRequest(
            new WorkspaceTopLevelInventoryKindFilter(
            [
                WorkspaceTopLevelInventoryEntryKind.Package,
            ]));

        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                fixture.Lease.Scope,
                request);

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Outcome);
        Assert.Equal(1, available.Document.TotalEntryCount);
        Assert.Equal(0, available.Document.SelectedEntryCount);
        Assert.Empty(available.Document.Entries);
        Assert.True(execution.Selection.HasAuthority);
        Assert.Equal(0, execution.Selection.Count);
    }

    [Theory]
    [MemberData(nameof(InvalidFilters))]
    public async Task Query_InvalidFilterIsRejected(
        WorkspaceTopLevelInventoryKindFilter filter)
    {
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateMixedAsync();

        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                fixture.Lease.Scope,
                new(filter));

        var rejected =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Rejected>(
                execution.Outcome);
        Assert.Equal(
            WorkspaceTopLevelInventoryRejection.InvalidFilter,
            rejected.Reason);
        Assert.False(execution.Selection.HasAuthority);
        Assert.Equal(0, execution.Selection.Count);
    }

    public static TheoryData<WorkspaceTopLevelInventoryKindFilter>
        InvalidFilters =>
        new()
        {
            new WorkspaceTopLevelInventoryKindFilter(
                Array.Empty<WorkspaceTopLevelInventoryEntryKind>()),
            new WorkspaceTopLevelInventoryKindFilter(
                [(WorkspaceTopLevelInventoryEntryKind)int.MaxValue]),
        };

    [Fact]
    public async Task Query_InvalidAuthorityPrecedesInvalidFilter()
    {
        await using WorkspaceInventoryFixture first =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        await using WorkspaceInventoryFixture second =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        var request = new WorkspaceTopLevelInventoryRequest(
            new WorkspaceTopLevelInventoryKindFilter([]));

        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                first.Lease.Definition,
                second.Lease.Scope,
                request);

        var unavailable =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Unavailable>(
                execution.Outcome);
        Assert.Equal(
            WorkspaceTopLevelInventoryUnavailableReason.InvalidAuthority,
            unavailable.Reason);
        Assert.False(execution.Selection.HasAuthority);
    }

    [Fact]
    public async Task Query_ProjectsAllPackageStatesAndPreparationWithoutAuthority()
    {
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        WorkspaceScopeSnapshot original = fixture.Lease.Scope;
        ImmutableArray<WorkspacePackageOccurrenceDescriptor> packages =
        [
            WithStatus(
                original.Packages[0],
                original.Packages[0].Realization.Status),
            WithStatus(
                original.Packages[1],
                new ArtifactRootRealizationStatus.Pending()),
            WithStatus(
                original.Packages[2],
                new ArtifactRootRealizationStatus.Failed(
                    ArtifactRootFailure.PreparationFailed)),
        ];
        DateTimeOffset deadline =
            new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var scope = new WorkspaceScopeSnapshot(
            original.Revision,
            original.PhysicalComposition,
            packages,
            original.Closure,
            new WorkspaceScopePreparationDescriptor(
                fixture.Lease.Definition.Workspace,
                new WorkspaceScopePublicationOperationIdentity(),
                WorkspaceScopeOperationKind.Add,
                requestedPackageCount: 2,
                deadline));

        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                scope,
                WorkspaceTopLevelInventoryRequest.All);

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Outcome);
        Assert.IsType<WorkspaceTopLevelPackageState.Ready>(
            Assert.IsType<WorkspaceTopLevelPackageEntry>(
                available.Document.Entries[0]).State);
        Assert.IsType<WorkspaceTopLevelPackageState.Pending>(
            Assert.IsType<WorkspaceTopLevelPackageEntry>(
                available.Document.Entries[1]).State);
        var failed = Assert.IsType<WorkspaceTopLevelPackageState.Failed>(
            Assert.IsType<WorkspaceTopLevelPackageEntry>(
                available.Document.Entries[2]).State);
        Assert.Equal(ArtifactRootFailure.PreparationFailed, failed.Failure);
        Assert.Equal(
            new WorkspaceTopLevelPreparation(
                WorkspaceScopeOperationKind.Add,
                2,
                deadline),
            available.Document.Preparation);
    }

    [Fact]
    public async Task Query_ContentRoundTripsEveryEntryAndStateArm()
    {
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        WorkspaceScopeSnapshot original = fixture.Lease.Scope;
        var scope = new WorkspaceScopeSnapshot(
            original.Revision,
            original.PhysicalComposition,
            [
                WithStatus(
                    original.Packages[0],
                    original.Packages[0].Realization.Status),
                WithStatus(
                    original.Packages[1],
                    new ArtifactRootRealizationStatus.Pending()),
                WithStatus(
                    original.Packages[2],
                    new ArtifactRootRealizationStatus.Failed(
                        ArtifactRootFailure.PreparationFailed)),
            ],
            original.Closure,
            preparing: null);
        WorkspaceTopLevelInventoryOutcome outcome =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                scope,
                WorkspaceTopLevelInventoryRequest.All).Outcome;

        string json = JsonSerializer.Serialize(outcome);
        WorkspaceTopLevelInventoryOutcome? restored =
            JsonSerializer.Deserialize<WorkspaceTopLevelInventoryOutcome>(
                json);

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                restored);
        Assert.Equal(6, available.Document.Entries.Length);
        Assert.Contains("\"kind\":\"ready\"", json);
        Assert.Contains("\"kind\":\"pending\"", json);
        Assert.Contains("\"kind\":\"failed\"", json);
        Assert.Contains("\"kind\":\"exactLibrary\"", json);
        Assert.Contains("\"kind\":\"packagePrefix\"", json);
        Assert.Contains("\"kind\":\"ecosystem\"", json);
        Assert.DoesNotContain("\"Occurrence\"", json);
        Assert.DoesNotContain("\"Correspondence\"", json);
        Assert.DoesNotContain("\"Generation\"", json);
        Assert.DoesNotContain("\"Cancellation\"", json);
    }

    [Fact]
    public async Task Query_ProjectsEveryExactLibraryCoordinateArm()
    {
        ExactLibrarySourceCoordinate package =
            WorkspaceRegistrationTestData.RealPackageSystemTextJson();
        var population = new PlatformLibraryPopulationDeclaration(
            PlatformFamily.DotNetRuntime);
        ImmutableArray<WorkspaceRegistration> registrations =
        [
            new WorkspaceRegistration.ExactLibrary(package),
            new WorkspaceRegistration.ExactLibrary(
                new ExactLibrarySourceCoordinate.Platform(
                    population,
                    package.LibraryIdentity)),
            new WorkspaceRegistration.ExactLibrary(
                new ExactLibrarySourceCoordinate.Project(
                    package.LibraryIdentity)),
            new WorkspaceRegistration.ExactLibrary(
                new ExactLibrarySourceCoordinate.Local(
                    package.LibraryIdentity)),
        ];
        await using WorkspaceInventoryFixture fixture =
            await WorkspaceInventoryFixture.CreateAsync(registrations, []);

        WorkspaceTopLevelInventoryOutcome outcome =
            WorkspaceTopLevelInventoryQuery.Execute(
                fixture.Lease.Definition,
                fixture.Lease.Scope,
                WorkspaceTopLevelInventoryRequest.All).Outcome;

        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                outcome);
        Assert.Collection(
            available.Document.Entries,
            entry => Assert.IsType<
                WorkspaceTopLevelExactLibraryCoordinate.Package>(
                    Assert.IsType<WorkspaceTopLevelExactLibraryEntry>(entry)
                        .Coordinate),
            entry => Assert.IsType<
                WorkspaceTopLevelExactLibraryCoordinate.Platform>(
                    Assert.IsType<WorkspaceTopLevelExactLibraryEntry>(entry)
                        .Coordinate),
            entry => Assert.IsType<
                WorkspaceTopLevelExactLibraryCoordinate.Project>(
                    Assert.IsType<WorkspaceTopLevelExactLibraryEntry>(entry)
                        .Coordinate),
            entry => Assert.IsType<
                WorkspaceTopLevelExactLibraryCoordinate.Local>(
                    Assert.IsType<WorkspaceTopLevelExactLibraryEntry>(entry)
                        .Coordinate));
        string json = JsonSerializer.Serialize(outcome);
        Assert.Contains("\"kind\":\"package\"", json);
        Assert.Contains("\"kind\":\"platform\"", json);
        Assert.Contains("\"kind\":\"project\"", json);
        Assert.Contains("\"kind\":\"local\"", json);
        Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
            JsonSerializer.Deserialize<WorkspaceTopLevelInventoryOutcome>(
                json));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TerminalFailureOutcomesRoundTrip(bool rejected)
    {
        WorkspaceTopLevelInventoryOutcome outcome = rejected
            ? new WorkspaceTopLevelInventoryOutcome.Rejected(
                WorkspaceTopLevelInventoryRejection.InvalidFilter)
            : new WorkspaceTopLevelInventoryOutcome.Unavailable(
                WorkspaceTopLevelInventoryUnavailableReason.InvalidAuthority);

        string json = JsonSerializer.Serialize(outcome);
        WorkspaceTopLevelInventoryOutcome? restored =
            JsonSerializer.Deserialize<WorkspaceTopLevelInventoryOutcome>(
                json);

        if (rejected)
        {
            Assert.Equal(
                WorkspaceTopLevelInventoryRejection.InvalidFilter,
                Assert.IsType<WorkspaceTopLevelInventoryOutcome.Rejected>(
                    restored).Reason);
        }
        else
        {
            Assert.Equal(
                WorkspaceTopLevelInventoryUnavailableReason.InvalidAuthority,
                Assert.IsType<WorkspaceTopLevelInventoryOutcome.Unavailable>(
                    restored).Reason);
        }
    }

    [Fact]
    public async Task SelectionReceipt_RejectsChangedDefinitionAsStale()
    {
        await using WorkspaceInventoryFixture first =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        await using WorkspaceInventoryFixture second =
            await WorkspaceInventoryFixture.CreateMixedAsync();
        WorkspaceTopLevelInventoryQueryExecution execution =
            WorkspaceTopLevelInventoryQuery.Execute(
                first.Lease.Definition,
                first.Lease.Scope,
                WorkspaceTopLevelInventoryRequest.All);
        var available =
            Assert.IsType<WorkspaceTopLevelInventoryOutcome.Available>(
                execution.Outcome);

        Assert.IsType<
            WorkspaceTopLevelInventorySelectionResolution.Stale>(
                execution.Selection.Resolve(
                    second.Lease,
                    available.Document.Entries[0].Key));
    }

    [Fact]
    public void PublicResultsRetainNoLiveWorkspaceOrInvokableContent()
    {
        var visited = new HashSet<Type>();
        Type[] roots =
        [
            typeof(WorkspaceTopLevelInventoryExecution),
            typeof(WorkspaceTopLevelPackageEntry),
            typeof(WorkspaceTopLevelExactLibraryEntry),
            typeof(WorkspaceTopLevelPackagePrefixEntry),
            typeof(WorkspaceTopLevelEcosystemEntry),
            typeof(WorkspaceTopLevelEcosystemPopulation.ExactLibrary),
            typeof(WorkspaceTopLevelEcosystemPopulation.Platform),
            typeof(WorkspaceTopLevelEcosystemPopulation.PackagePrefix),
            typeof(WorkspaceTopLevelInventorySelection.Package),
            typeof(WorkspaceTopLevelInventorySelection.Registration),
        ];
        foreach (Type root in roots)
            Visit(root);

        void Visit(Type type)
        {
            if (!visited.Add(type)
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(Guid)
                || type == typeof(Version)
                || type.Namespace?.StartsWith(
                    "System.Collections.Immutable",
                    StringComparison.Ordinal) == true)
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
                typeof(WorkspacePlan).IsAssignableFrom(type));
            Assert.False(
                typeof(WorkspaceRegistration).IsAssignableFrom(type));
            Assert.False(
                typeof(WorkspaceEcosystemRegistrationDeclaration)
                    .IsAssignableFrom(type));
            Assert.False(
                typeof(EcosystemIntegrationScannerBinding)
                    .IsAssignableFrom(type));
            Assert.False(type.IsByRefLike);
            foreach (Type argument in type.GetGenericArguments())
                Visit(argument);
            foreach (PropertyInfo property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Visit(property.PropertyType);
            }
        }
    }

    static WorkspacePackageOccurrenceDescriptor WithStatus(
        WorkspacePackageOccurrenceDescriptor package,
        ArtifactRootRealizationStatus status) =>
        new(
            package.Occurrence,
            package.Realization with
            {
                Status = status,
            });

    static WorkspaceRegistration ExactLibraryRegistration()
    {
        ExactLibrarySourceCoordinate real =
            WorkspaceRegistrationTestData.RealPackageSystemTextJson();
        return new WorkspaceRegistration.ExactLibrary(
            new ExactLibrarySourceCoordinate.Package(
                PackageSourceCoordinate.Create(
                    "System.Text.Json",
                    "1.0.0"),
                real.LibraryIdentity));
    }

    static ImmutableArray<WorkspaceRegistration> MixedRegistrations()
    {
        ExactLibrarySourceCoordinate exact =
            Assert.IsType<WorkspaceRegistration.ExactLibrary>(
                ExactLibraryRegistration()).Coordinate;
        var prefix = new PackagePrefixDeclaration("Microsoft.Extensions.");
        var ecosystem = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.runtime"),
            ["System", "Microsoft"],
            [
                new PackageCoordinate("System.Text.Json"),
                new PackageCoordinate("Microsoft.Extensions.Logging"),
            ],
            [
                new WorkspaceEcosystemPopulationDeclaration.ExactLibrary(
                    exact),
                new WorkspaceEcosystemPopulationDeclaration.Platform(
                    new(PlatformFamily.DotNetRuntime)),
                new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(
                    prefix),
            ],
            EcosystemIntegrationScanner.AspireBinding);
        return
        [
            new WorkspaceRegistration.ExactLibrary(exact),
            new WorkspaceRegistration.PackagePrefix(prefix),
            new WorkspaceRegistration.Ecosystem(ecosystem),
        ];
    }

    sealed class WorkspaceInventoryFixture : IAsyncDisposable
    {
        WorkspaceInventoryFixture(
            WorkspaceReplacementCoordinator coordinator,
            WorkspaceRealizationOperationLease lease)
        {
            Coordinator = coordinator;
            Lease = lease;
        }

        WorkspaceReplacementCoordinator Coordinator { get; }

        internal WorkspaceRealizationOperationLease Lease { get; }

        internal static Task<WorkspaceInventoryFixture> CreateMixedAsync() =>
            CreateAsync(
                MixedRegistrations(),
                [
                    "System.Text.Json",
                    "Markout",
                    "Microsoft.Extensions.Logging",
                ]);

        internal static async Task<WorkspaceInventoryFixture> CreateAsync(
            ImmutableArray<WorkspaceRegistration> registrations,
            ImmutableArray<string> packageIds)
        {
            var coordinator = new WorkspaceReplacementCoordinator();
            try
            {
                WorkspaceRealizationCandidate candidate =
                    await WorkspaceRealizationConsumer.BeginAsync(
                        coordinator,
                        new WorkspacePlan(registrations));
                using (WorkspaceRealizationConstructionLease construction =
                    WorkspaceRealizationConsumer.EnterConstruction(candidate))
                {
                    if (!packageIds.IsEmpty)
                    {
                        WorkspaceScopeSnapshot current =
                            Assert.IsType<WorkspaceScopeReadResult.Available>(
                                await construction.Workspace
                                    .GetScopeSnapshotAsync()).Snapshot;
                        _ = Assert.IsType<
                            WorkspaceScopeOperationResult.Committed>(
                                await construction.Workspace.ReplaceScopeAsync(
                                    current.Revision,
                                    packageIds.Select(
                                        PackageAssemblyContextCompletionTests
                                            .SharedBinding)
                                        .ToImmutableArray(),
                                    DateTimeOffset.UtcNow.AddMinutes(1),
                                    TestContext.Current.CancellationToken));
                    }
                }

                _ = await WorkspaceRealizationConsumer.ActivateAsync(
                    coordinator,
                    candidate);
                WorkspaceRealizationOperationLease lease =
                    await WorkspaceRealizationConsumer.EnterAsync(coordinator);
                return new(coordinator, lease);
            }
            catch
            {
                await coordinator.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Lease.Dispose();
            await Coordinator.DisposeAsync();
        }
    }
}
