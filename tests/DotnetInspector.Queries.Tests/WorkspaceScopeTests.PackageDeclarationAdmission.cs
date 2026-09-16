using System.IO.Compression;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceScopeTests
{
    const string DeclarationName =
        "ILInspector.Metadata.AssemblyReferenceIdentity";

    [Fact]
    public async Task PackageScopeDeclarationAdmission_PreservesExactOccurrenceAndSurfaceAssets()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspacePackageOccurrenceDescriptor occurrence =
            Assert.Single((await Replace(
                workspace,
                MultiAssemblyBinding("First.Package"))).Packages);
        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator();

        WorkspacePackageDeclarationAdmission admitted =
            Admission(await workspace.AdmitPackageScopeDeclarationAsync(
                occurrence,
                TestContext.Current.CancellationToken));

        Assert.Same(occurrence, admitted.Occurrence);
        Assert.Same(Ready(occurrence), admitted.Generation);
        WorkspaceDeclarationContextReceipt context =
            admitted.Context.Receipt;
        var request =
            Assert.IsType<WorkspaceDeclarationRequest.PackageScope>(
                context.Request);
        Assert.Same(occurrence, request.Occurrence);
        Assert.Equal(2, context.Members.Length);
        Assert.Equal(
            [
                "ref/net11.0/DotnetInspector.Queries.dll",
                "ref/net11.0/ILInspector.Metadata.dll",
            ],
            context.Members.Select(member =>
                Assert.IsType<
                    WorkspaceDeclarationOrigin.PackageScope>(
                        member.Origin).Asset.Path));
        Assert.All(context.Members, member =>
        {
            var origin =
                Assert.IsType<
                    WorkspaceDeclarationOrigin.PackageScope>(
                        member.Origin);
            Assert.Same(occurrence, origin.Occurrence);
            Assert.Equal(
                origin.Asset.Path,
                Assert.IsType<
                    AssemblyResolutionProvenance.PackageAsset>(
                        member.Selection).AssetPath);
        });
        var captured =
            Assert.IsType<
                WorkspaceDeclarationPopulationCapture.Captured>(
                    workspace.CaptureDeclarationPopulation(
                        [admitted.Context]));
        Assert.Single(captured.Population.Receipt.Contexts);
        Assert.False(locator.IsActive);
        Assert.Equal(0, locator.InventoryReadCount);

        TypeDeclarationLocatorResult.Evaluated result =
            await FindPackageDeclaration(locator);

        TypeDeclarationLocatorCandidate candidate =
            Assert.Single(Assert.Single(result.Answers).Candidates);
        Assert.Same(
            context.Members.Single(member =>
                member.AssemblyIdentity.Name
                    == "ILInspector.Metadata").Occurrence,
            candidate.Observation.Occurrence);
        Assert.Equal(2, locator.InventoryReadCount);
        var projected =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    result,
                    TypeDeclarationLocatorSectionPlan.All));
        TypeDeclarationLocatorSectionCandidate projectedCandidate =
            Assert.Single(
                Assert.Single(projected.Answers).Candidates);
        Assert.Equal(
            "ref/net11.0/ILInspector.Metadata.dll",
            Assert.IsType<
                TypeDeclarationLocatorSelection.PackageSelection>(
                    projectedCandidate.Observation.Selection).AssetPath);
        var realization =
            Assert.IsType<
                TypeDeclarationLocatorRealization.PackageRealization>(
                    projectedCandidate.Observation.Realization);
        Assert.Equal(
            occurrence.Occurrence.Package.Coordinate.Producer,
            realization.Producer);
    }

    [Fact]
    public async Task PackageScopeDeclarationAdmission_IsIdempotentAndMaintainsAppend()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot firstScope =
            await Replace(workspace, Binding("First.Package"));
        WorkspacePackageOccurrenceDescriptor first =
            Assert.Single(firstScope.Packages);
        WorkspacePackageDeclarationAdmission firstAdmission =
            Admission(await workspace.AdmitPackageScopeDeclarationAsync(
                first,
                TestContext.Current.CancellationToken));
        WorkspacePackageDeclarationAdmission repeated =
            Admission(await workspace.AdmitPackageScopeDeclarationAsync(
                first,
                TestContext.Current.CancellationToken));
        Assert.Same(firstAdmission, repeated);

        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator();
        TypeDeclarationLocatorResult.Evaluated before =
            await FindPackageDeclaration(locator);
        Assert.Single(Assert.Single(before.Answers).Candidates);
        Assert.Equal(1, locator.InventoryReadCount);

        WorkspaceScopeSnapshot removed =
            Committed(
                await workspace.RemovePackageOccurrenceAsync(
                    firstScope.Revision,
                    first.Occurrence.Identity,
                    Deadline,
                    TestContext.Current.CancellationToken))
                .Snapshot;
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind
                .OccurrenceNotCurrent,
            Rejection(await workspace.AdmitPackageScopeDeclarationAsync(
                first,
                TestContext.Current.CancellationToken)).Kind);
        WorkspaceScopeSnapshot appended =
            Committed(await workspace.AddPackagesAsync(
                removed.Revision,
                [Binding("First.Package")],
                Deadline,
                TestContext.Current.CancellationToken)).Snapshot;
        WorkspacePackageOccurrenceDescriptor second =
            Assert.Single(appended.Packages);
        _ = Admission(await workspace.AdmitPackageScopeDeclarationAsync(
            second,
            TestContext.Current.CancellationToken));
        await locator.Maintenance.WaitAsync(
            TestContext.Current.CancellationToken);

        TypeDeclarationLocatorResult.Evaluated after =
            await FindPackageDeclaration(locator);
        Assert.Equal(2, Assert.Single(after.Answers).Candidates.Length);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Single(Assert.Single(before.Answers).Candidates);
        Assert.NotSame(
            first.Occurrence.Identity,
            second.Occurrence.Identity);
        Assert.Equal(
            Assert.IsType<
                DotnetInspector.SourceSelection
                    .ExactLibrarySourceCoordinate.Package>(
                        Assert.Single(before.Answers)
                            .Candidates[0].Coordinate),
            Assert.IsType<
                DotnetInspector.SourceSelection
                    .ExactLibrarySourceCoordinate.Package>(
                        Assert.Single(after.Answers)
                            .Candidates[1].Coordinate));
    }

    [Fact]
    public async Task PackageScopeDeclarationAdmission_RejectsInputsWithoutCurrentAuthority()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspacePackageOccurrenceDescriptor current =
            Assert.Single((await Replace(
                workspace,
                Binding("Current.Package"))).Packages);
        await using var foreignWorkspace =
            new InspectionWorkspace();
        WorkspacePackageOccurrenceDescriptor foreign =
            Assert.Single((await Replace(
                foreignWorkspace,
                Binding("Foreign.Package"))).Packages);
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind
                .ForeignWorkspace,
            Rejection(await workspace.AdmitPackageScopeDeclarationAsync(
                foreign,
                TestContext.Current.CancellationToken)).Kind);

        var rootOnlyOccurrence = new WorkspacePackageOccurrence(
            workspace.Identity,
            current.Occurrence.Package,
            current.Occurrence.Correspondence);
        var rootOnly = new WorkspacePackageOccurrenceDescriptor(
            rootOnlyOccurrence,
            current.Realization);
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind
                .OccurrenceNotCurrent,
            Rejection(await workspace.AdmitPackageScopeDeclarationAsync(
                rootOnly,
                TestContext.Current.CancellationToken)).Kind);

        _ = Admission(
            await workspace.AdmitPackageScopeDeclarationAsync(
                current,
                TestContext.Current.CancellationToken));
        ArtifactRootCompositionGenerationIdentity pendingEpoch =
            ArtifactAvailable(await workspace.RetireArtifactRootAsync(
                current.Occurrence.Correspondence,
                Ready(current)));
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind.RootPending,
            Rejection(await workspace.AdmitPackageScopeDeclarationAsync(
                current,
                TestContext.Current.CancellationToken)).Kind);

        ArtifactAvailable(
            await workspace.FailArtifactRootReplacementAsync(
                current.Occurrence.Correspondence,
                pendingEpoch,
                ArtifactRootFailure.PreparationFailed));
        WorkspacePackageOccurrenceDescriptor failed =
            Assert.Single((await Current(workspace)).Packages);
        WorkspacePackageDeclarationAdmissionFailure failure =
            Rejection(await workspace.AdmitPackageScopeDeclarationAsync(
                failed,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind.RootFailed,
            failure.Kind);
        Assert.Equal(
            ArtifactRootFailure.PreparationFailed,
            failure.ArtifactFailure);

        var authority = new ArtifactRootPreparationAuthority(
            workspace.Identity,
            new(),
            Deadline,
            TestContext.Current.CancellationToken);
        ArtifactRootPreparationReceipt receipt =
            ArtifactAvailable(
                await workspace.PreparePackageArtifactRootsAsync(
                    authority,
                    [Binding("Current.Package")]));
        ArtifactRootReplacementSettlement settlement =
            ArtifactAvailable(
                await workspace.SettleArtifactRootReplacementAsync(
                    authority,
                    receipt,
                    ArtifactAvailable(
                        await workspace
                            .GetCurrentArtifactRootCompositionGenerationAsync(
                                workspace.Identity))));
        Assert.NotNull(settlement);
        _ = await Current(workspace);
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind
                .ArtifactGenerationMismatch,
            Rejection(await workspace.AdmitPackageScopeDeclarationAsync(
                current,
                TestContext.Current.CancellationToken)).Kind);
    }

    [Fact]
    public async Task PackageScopeDeclarationAdmission_PreservesTypedRealizationGap()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspacePackageOccurrenceDescriptor occurrence =
            Assert.Single((await Replace(
                workspace,
                Binding(
                    "Empty.Package",
                    entry: "ref/net11.0/_._"))).Packages);
        WorkspacePackageDeclarationAdmission admission =
            Admission(await workspace.AdmitPackageScopeDeclarationAsync(
                occurrence,
                TestContext.Current.CancellationToken));

        Assert.False(admission.Context.Receipt.IsRealized);
        WorkspaceDeclarationFailure.PackageScopeSelection gap =
            Assert.IsType<
                WorkspaceDeclarationFailure.PackageScopeSelection>(
                    Assert.Single(
                        admission.Context.Receipt.Failures));
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            gap.Status);

        TypeDeclarationLocatorResult.Evaluated result =
            await FindPackageDeclaration(
                workspace.GetDeclarationLocator());
        Assert.Empty(Assert.Single(result.Answers).Candidates);
        Assert.False(Assert.Single(result.Answers).IsComplete);
        var projected =
            Assert.IsType<TypeDeclarationLocatorSectionResult.Evaluated>(
                TypeDeclarationLocatorSection.Project(
                    result,
                    TypeDeclarationLocatorSectionPlan.All));
        var projectedGap =
            Assert.IsType<
                TypeDeclarationLocatorContextFailure
                    .PackageScopeSelection>(
                    Assert.Single(
                        Assert.Single(projected.Contexts)
                            .Failures));
        Assert.Equal(
            PackageCompileAssetSelectionStatus.EmptyCompileGroup,
            projectedGap.Status);
        Assert.Equal(
            occurrence.Occurrence.Package.Coordinate.Producer,
            projectedGap.Producer);
        using JsonDocument json = JsonDocument.Parse(
            TypeDeclarationLocatorSectionJson.Serialize(projected));
        Assert.Equal(
            "package-scope-selection",
            json.RootElement
                .GetProperty("contexts")[0]
                .GetProperty("failures")[0]
                .GetProperty("kind")
                .GetString());
    }

    [Fact]
    public async Task PackageScopeDeclarationAdmission_CloseReturnsLeaseAfterMaintenance()
    {
        var workspace = new InspectionWorkspace();
        WorkspacePackageOccurrenceDescriptor occurrence =
            Assert.Single((await Replace(
                workspace,
                Binding("Close.Package"))).Packages);
        InspectionWorkspace.RootLifetime lifetime =
            Assert.Single(Lifetimes(workspace));
        _ = Admission(await workspace.AdmitPackageScopeDeclarationAsync(
            occurrence,
            TestContext.Current.CancellationToken));
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        async ValueTask Pause()
        {
            entered.TrySetResult();
            await resume.Task.ConfigureAwait(false);
        }
        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator(null, Pause);
        Task<TypeDeclarationLocatorResult> query =
            locator.ExecuteAsync(
                [
                    new TypeDeclarationLocatorRequest.Pattern(
                        DeclarationName),
                ],
                cancellationToken:
                    TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        try
        {
            Assert.False(close.IsCompleted);
            Assert.False(lifetime.Released.Task.IsCompleted);
        }
        finally
        {
            resume.TrySetResult();
        }

        TypeDeclarationLocatorResult? queryResult = null;
        try
        {
            queryResult = await query;
        }
        catch (OperationCanceledException)
        {
        }
        if (queryResult is not null)
        {
            switch (queryResult)
            {
                case TypeDeclarationLocatorResult.Evaluated evaluated:
                    Assert.IsType<
                        TypeDeclarationLocatorMemberOutcome.Unavailable>(
                            Assert.Single(evaluated.Members));
                    break;
                case TypeDeclarationLocatorResult.Rejected rejected:
                    Assert.Equal(
                        TypeDeclarationLocatorRejectionKind
                            .PopulationUnavailable,
                        rejected.Kind);
                    Assert.Equal(
                        WorkspaceDeclarationPopulationFailure
                            .WorkspaceClosing,
                        rejected.PopulationFailure);
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown locator result.");
            }
        }
        Assert.True((await close).Succeeded);
        Assert.True(lifetime.Released.Task.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task PackageScopeDeclarationAdmission_RealJsonPackagesRemainDistinctChoices()
    {
        using var client = new HttpClient();
        var options = new WorkspaceContextLoadOptions
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization(
                    [PackageSource.NuGetOrg]),
            PackageStore = new InMemoryPackageStore(),
        };
        var acquired =
            Assert.IsType<PackageRootAcquisitionOutcome.Acquired>(
                await PackageRootAcquisition.AcquireAsync(
                    PackageRootAcquisitionRequest.Create(
                        "System.Text.Json",
                        "10.0.0",
                        "net10.0"),
                    options,
                    TestContext.Current.CancellationToken));
        Assert.True(
            acquired.Payload.Content.TryOpenArchive(
                out Stream? packageStream));
        using (packageStream)
        using (var copy = new MemoryStream())
        {
            await packageStream.CopyToAsync(
                copy,
                TestContext.Current.CancellationToken);
            var mirrorContent = new InMemoryPackageContent(
                copy.ToArray(),
                fromCache: false,
                producerKey: "tests-mirror");
            PackageRootBinding mirror =
                PackageRootBinding.CreateFromSource(
                    new AcquiredPackageSourcePayload(
                        PackageSourceCoordinate.Create(
                            "System.Text.Json",
                            "10.0.0"),
                        mirrorContent,
                        "tests-mirror",
                        PackagePayloadOrigin.Download),
                    "net10.0");

            await using var workspace =
                new InspectionWorkspace();
            WorkspaceScopeSnapshot firstScope =
                await Replace(workspace, acquired.Binding);
            _ = Admission(
                await workspace
                    .AdmitPackageScopeDeclarationAsync(
                        Assert.Single(firstScope.Packages),
                        TestContext.Current.CancellationToken));
            WorkspaceDeclarationLocator locator =
                workspace.GetDeclarationLocator();
            TypeDeclarationLocatorResult.Evaluated first =
                await AssertJsonSerializer(locator);
            Assert.Single(
                Assert.Single(first.Answers).Candidates);

            WorkspaceScopeSnapshot secondScope =
                Committed(await workspace.AddPackagesAsync(
                    firstScope.Revision,
                    [mirror],
                    Deadline,
                    TestContext.Current.CancellationToken)).Snapshot;
            _ = Admission(
                await workspace
                    .AdmitPackageScopeDeclarationAsync(
                        secondScope.Packages[1],
                        TestContext.Current.CancellationToken));
            await locator.Maintenance.WaitAsync(
                TestContext.Current.CancellationToken);
            TypeDeclarationLocatorResult.Evaluated second =
                await AssertJsonSerializer(locator);
            TypeDeclarationLocatorCandidate[] choices =
                [.. Assert.Single(second.Answers).Candidates];

            Assert.Equal(2, choices.Length);
            Assert.Equal(
                2,
                choices.Select(candidate =>
                        Assert.IsType<
                            WorkspaceDeclarationOrigin.PackageScope>(
                                candidate.Observation.Origin)
                            .Occurrence.Occurrence.Package.Coordinate
                            .Producer)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.Single(
                Assert.Single(first.Answers).Candidates);
        }
    }

    static async Task<TypeDeclarationLocatorResult.Evaluated>
        FindPackageDeclaration(WorkspaceDeclarationLocator locator) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync(
                [
                    new TypeDeclarationLocatorRequest.Pattern(
                        DeclarationName),
                ],
                includeAll: true,
                TestContext.Current.CancellationToken));

    static WorkspacePackageDeclarationAdmission Admission(
        WorkspacePackageDeclarationAdmissionResult result) =>
        Assert.IsType<
            WorkspacePackageDeclarationAdmissionResult.Admitted>(
                result).Admission;

    static WorkspacePackageDeclarationAdmissionFailure Rejection(
        WorkspacePackageDeclarationAdmissionResult result) =>
        Assert.IsType<
            WorkspacePackageDeclarationAdmissionResult.Rejected>(
                result).Failure;

    static async Task<TypeDeclarationLocatorResult.Evaluated>
        AssertJsonSerializer(
            WorkspaceDeclarationLocator locator) =>
        Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync(
                [
                    new TypeDeclarationLocatorRequest.Pattern(
                        "System.Text.Json.JsonSerializer"),
                ],
                includeAll: true,
                TestContext.Current.CancellationToken));

    static PackageRootBinding MultiAssemblyBinding(
        string packageId)
    {
        using var bytes = new MemoryStream();
        using (var archive =
            new ZipArchive(
                bytes,
                ZipArchiveMode.Create,
                leaveOpen: true))
        {
            Add(
                "ref/net11.0/DotnetInspector.Queries.dll",
                typeof(InspectionWorkspace).Assembly.Location);
            Add(
                "ref/net11.0/ILInspector.Metadata.dll",
                typeof(AssemblyReferenceIdentity).Assembly.Location);

            void Add(string path, string source)
            {
                using Stream destination =
                    archive.CreateEntry(path).Open();
                destination.Write(File.ReadAllBytes(source));
            }
        }

        var content = new InMemoryPackageContent(
            bytes.ToArray(),
            fromCache: false,
            producerKey: "tests");
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    packageId,
                    "1.0.0"),
                content,
                "tests",
                PackagePayloadOrigin.Download),
            "net11.0",
            displayPackageId: packageId);
    }
}
