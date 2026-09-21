using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class ApiCoordinateCorrespondenceQueryTests
{
    static CancellationToken Cancellation =>
        TestContext.Current.CancellationToken;

    [Fact]
    public async Task DirectDefinition_ReturnsExactDestinationType()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        (CoordinatePackageObservation first, CoordinatePackageObservation second) =
            await ObservePair(workspace, before, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "Api"));

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, first, second, Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Exact, result.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            Assert.IsType<ApiDeclarationBindingResult>(result.SourceBinding).Status);
        CoordinateTypeResolutionOutcomeEvidence.Resolved resolution =
            Assert.IsType<CoordinateTypeResolutionOutcomeEvidence.Resolved>(
                Assert.IsType<CoordinateTypeResolutionEvidence.Available>(
                    result.Resolution).Outcome);
        Assert.Empty(resolution.Hops);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            Assert.IsType<ApiDeclarationCorrespondenceResult>(
                result.Correspondence).Status);
        StructuralSubjectIdentity.TypeSubject destination =
            Assert.IsType<StructuralSubjectIdentity.TypeSubject>(
                result.Destination);
        Assert.Same(
            Assert.Single(second.Libraries).Subject.Identity.Registration,
            destination.Library.Identity.Registration);
        Assert.Equal(source.Identity.Type, destination.Identity.Type);
        Assert.Null(result.Failure);

        ApiCoordinateCorrespondenceEvidence evidence = result.Detach();
        await workspace.CloseAsync();

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Exact, evidence.Status);
        Assert.False(evidence.IsCompleteDestinationAbsence);
        Assert.Equal("1.0.0", evidence.Source.Library.Package.PackageVersion);
        Assert.Equal("ref/net11.0/Target.dll", evidence.Source.Library.Asset!.Path);
        Assert.Equal(ApiDeclarationKind.Type, evidence.Source.Kind);
        Assert.Equal(source.Identity.Type, evidence.Source.DeclaringType);
        Assert.Equal("2.0.0", evidence.Destination!.Library.Package.PackageVersion);
        Assert.Equal("ref/net11.0/Target.dll", evidence.Destination.Library.Asset!.Path);
        Assert.Equal(source.Identity.Type, evidence.Destination.DeclaringType);
    }

    [Fact]
    public async Task DistinctWorkspaces_ReturnExactDestinationType()
    {
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        CoordinatePackageObservation first =
            await ObserveSingle(sourceWorkspace, before);
        CoordinatePackageObservation second =
            await ObserveSingle(destinationWorkspace, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "Api"));

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                sourceWorkspace,
                destinationWorkspace,
                source,
                first,
                second,
                Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Exact, result.Status);
        StructuralSubjectIdentity.TypeSubject resultSource =
            Assert.IsType<StructuralSubjectIdentity.TypeSubject>(
                result.Source);
        StructuralSubjectIdentity.TypeSubject destination =
            Assert.IsType<StructuralSubjectIdentity.TypeSubject>(
                result.Destination);
        Assert.Same(
            Assert.Single(first.Libraries).Subject.Identity.Registration,
            resultSource.Library.Identity.Registration);
        Assert.Same(
            Assert.Single(second.Libraries).Subject.Identity.Registration,
            destination.Library.Identity.Registration);
        Assert.Same(destinationWorkspace.Identity, destination.Workspace.Identity);
        Assert.NotSame(
            source.Library.Identity.Registration,
            destination.Library.Identity.Registration);

        ApiCoordinateCorrespondenceEvidence evidence = result.Detach();
        await sourceWorkspace.CloseAsync();
        await destinationWorkspace.CloseAsync();

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Exact, evidence.Status);
        Assert.Equal("1.0.0", evidence.Source.Library.Package.PackageVersion);
        Assert.Equal("2.0.0", evidence.Destination!.Library.Package.PackageVersion);
    }

    [Fact]
    public async Task DistinctWorkspaces_ReturnExactDestinationMember()
    {
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("lib/net11.0/Fixture.dll",
                FixtureCatalog.MetadataApiCorrespondencePair.OldAssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("lib/net11.0/Fixture.dll",
                FixtureCatalog.MetadataApiCorrespondencePair.NewAssemblyPath()));
        CoordinatePackageObservation first =
            await ObserveSingle(sourceWorkspace, before);
        CoordinatePackageObservation second =
            await ObserveSingle(destinationWorkspace, after);
        const string type =
            "MetadataCorrespondenceFixture.OrdinalSelection";
        ApiCoordinateSourceSelectionResult selection =
            await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
                sourceWorkspace,
                first,
                new("coordinate.sample", "1.0.0", "2.0.0", type, "Pick:1"),
                cancellationToken: Cancellation);
        StructuralSubjectIdentity.MemberSubject source =
            Assert.IsType<StructuralSubjectIdentity.MemberSubject>(
                selection.Subject);

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                sourceWorkspace,
                destinationWorkspace,
                source,
                ApiDeclarationKind.Method,
                first,
                second,
                Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Exact, result.Status);
        StructuralSubjectIdentity.MemberSubject destination =
            Assert.IsType<StructuralSubjectIdentity.MemberSubject>(
                result.Destination);
        Assert.Equal(
            source.Identity.Member.StableSelector,
            destination.Identity.Member.StableSelector);
        Assert.Same(destinationWorkspace.Identity, destination.Workspace.Identity);
        Assert.NotSame(
            source.DeclaringType.Library.Identity.Registration,
            destination.DeclaringType.Library.Identity.Registration);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ForeignEndpointWorkspace_IsRefused(bool sourceEndpoint)
    {
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        await using var foreignWorkspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        CoordinatePackageObservation first =
            await ObserveSingle(sourceWorkspace, before);
        CoordinatePackageObservation second =
            await ObserveSingle(destinationWorkspace, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "Api"));

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                sourceEndpoint ? foreignWorkspace : sourceWorkspace,
                sourceEndpoint ? destinationWorkspace : foreignWorkspace,
                source,
                first,
                second,
                Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Refused, result.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceFailureKind.ForeignWorkspace,
            result.Failure!.Kind);
        Assert.Null(result.SourceBinding);
        Assert.Null(result.Resolution);
        Assert.Null(result.Correspondence);
        Assert.Null(result.Destination);
    }

    [Fact]
    public async Task RetiredSourceRoot_ReturnsTypedFailure()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        (CoordinatePackageObservation first, CoordinatePackageObservation second) =
            await ObservePair(workspace, before, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "Api"));
        WorkspaceScopeSnapshot current =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ClearScopeAsync(
                current.Revision,
                DateTimeOffset.UtcNow.AddMinutes(1),
                Cancellation));

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, first, second, Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Failed, result.Status);
        Assert.Equal(
            CoordinateLibraryPairingStatus.Exact,
            result.LibraryPairing.Status);
        Assert.Null(result.SourceBinding);
        Assert.Null(result.Resolution);
        Assert.Equal(
            ApiCoordinateCorrespondenceFailureKind.SourceRootUnavailable,
            result.Failure!.Kind);
        Assert.NotNull(result.Failure.RootFailure);

        ApiCoordinateCorrespondenceEvidence evidence = result.Detach();
        await workspace.CloseAsync();

        Assert.False(evidence.IsCompleteDestinationAbsence);
        Assert.Equal(ApiCoordinateCorrespondenceStatus.Failed, evidence.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceFailureKind.SourceRootUnavailable,
            evidence.Failure!.Kind);
    }

    [Fact]
    public async Task MissingSourceAndAbsentEntryLibrary_IsRefusedNotAbsent()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/_._",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        (CoordinatePackageObservation first, CoordinatePackageObservation second) =
            await ObservePair(workspace, before, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "MissingApi"));

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, first, second, Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Refused, result.Status);
        Assert.Equal(
            CoordinateLibraryPairingStatus.Absent,
            result.LibraryPairing.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Absent,
            Assert.IsType<ApiDeclarationBindingResult>(result.SourceBinding).Status);
        Assert.Null(result.Resolution);
        Assert.Null(result.Correspondence);
        Assert.Null(result.Destination);

        ApiCoordinateCorrespondenceEvidence evidence = result.Detach();
        await workspace.CloseAsync();

        Assert.False(evidence.IsCompleteDestinationAbsence);
        Assert.Equal(ApiDeclarationKind.Type, evidence.Source.Kind);
        Assert.Equal("1.0.0", evidence.Source.Library.Package.PackageVersion);
    }

    [Fact]
    public async Task EstablishedSourceAndAbsentEntryLibrary_IsAbsent()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/_._",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        (CoordinatePackageObservation first, CoordinatePackageObservation second) =
            await ObservePair(workspace, before, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "Api"));

        ApiCoordinateCorrespondenceResult result =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, first, second, Cancellation);

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Absent, result.Status);
        Assert.Equal(
            CoordinateLibraryPairingStatus.Absent,
            result.LibraryPairing.Status);
        Assert.Equal(
            ApiDeclarationCorrespondenceStatus.Exact,
            Assert.IsType<ApiDeclarationBindingResult>(result.SourceBinding).Status);
        Assert.Null(result.Resolution);
        Assert.Null(result.Correspondence);
        Assert.Null(result.Destination);

        ApiCoordinateCorrespondenceEvidence evidence = result.Detach();
        await workspace.CloseAsync();

        Assert.True(evidence.IsCompleteDestinationAbsence);
        Assert.Equal(
            CoordinateLibraryPairingStatus.Absent,
            evidence.LibraryPairing.Status);
        Assert.Equal("1.0.0", evidence.Source.Library.Package.PackageVersion);
        Assert.Equal("2.0.0", evidence.LibraryPairing.After.PackageVersion);
        Assert.Null(evidence.LibraryPairing.Destination);
    }

    [Fact]
    public async Task AmbiguousDestinationLibraries_DetachAsNonAbsence()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = Binding(
            "1.0.0",
            ("ref/net11.0/Target.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()));
        PackageRootBinding after = Binding(
            "2.0.0",
            ("ref/net11.0/First.dll",
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath()),
            ("ref/net11.0/Second.dll",
                FixtureCatalog.AnalysisCallerGraphTargetV2.AssemblyPath()));
        (CoordinatePackageObservation first, CoordinatePackageObservation second) =
            await ObservePair(workspace, before, after);
        StructuralSubjectIdentity.TypeSubject source =
            StructuralSubjectIdentity.ForType(
                Assert.Single(first.Libraries).Subject,
                Type("Target", "Api"));

        ApiCoordinateCorrespondenceEvidence evidence =
            (await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, source, first, second, Cancellation)).Detach();
        await workspace.CloseAsync();

        Assert.Equal(ApiCoordinateCorrespondenceStatus.Ambiguous, evidence.Status);
        Assert.False(evidence.IsCompleteDestinationAbsence);
        Assert.Equal(
            CoordinateLibraryPairingStatus.Ambiguous,
            evidence.LibraryPairing.Status);
        Assert.Equal(2, evidence.LibraryPairing.Candidates.Length);
        Assert.Null(evidence.Destination);
    }

    [Fact]
    public void DetachedEvidence_PublicBoundaryExcludesLiveQueryTypes()
    {
        Type[] boundaries =
        [
            typeof(ApiCoordinateCorrespondenceEvidence),
            typeof(ApiCoordinateDeclarationEvidence),
            typeof(CoordinateLibraryPairingEvidence),
            typeof(CoordinateApiLibraryEvidence),
            typeof(CoordinateResolutionAssemblyEvidence),
        ];
        Type[] liveTypes =
        [
            typeof(InspectionWorkspace),
            typeof(StructuralSubjectIdentity),
            typeof(CoordinatePackageObservation),
            typeof(CoordinateApiLibraryObservation),
            typeof(CoordinateLibraryPairingResult),
            typeof(ApiCoordinateCorrespondenceResult),
        ];

        foreach (PropertyInfo property in boundaries.SelectMany(type =>
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)))
        {
            foreach (Type liveType in liveTypes)
            {
                Assert.False(
                    ContainsType(property.PropertyType, liveType),
                    $"{property.DeclaringType!.Name}.{property.Name} exposes {liveType.Name}.");
            }
        }
    }

    static bool ContainsType(Type candidate, Type prohibited) =>
        prohibited.IsAssignableFrom(candidate)
        || candidate.IsArray && ContainsType(candidate.GetElementType()!, prohibited)
        || candidate.IsGenericType
            && candidate.GetGenericArguments().Any(argument =>
                ContainsType(argument, prohibited));

    static MetadataTypeDefinitionName Type(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                ImmutableArray.Create(segments))).Name;

    static async Task<(
        CoordinatePackageObservation Before,
        CoordinatePackageObservation After)> ObservePair(
        InspectionWorkspace workspace,
        PackageRootBinding before,
        PackageRootBinding after)
    {
        WorkspaceScopeSnapshot current =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.ReplaceScopeAsync(
                    current.Revision,
                    [before, after],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    Cancellation)).Snapshot;
        return (
            await Observe(workspace, before, scope),
            await Observe(workspace, after, scope));
    }

    static async Task<CoordinatePackageObservation> Observe(
        InspectionWorkspace workspace,
        PackageRootBinding binding,
        WorkspaceScopeSnapshot scope) =>
        Assert.IsType<CoordinatePackageObservationResult.Available>(
            await CoordinateLibraryPairingQuery.ObserveAsync(
                workspace,
                binding,
                scope.FindPackageOccurrence(binding)!,
                Cancellation)).Observation;

    static async Task<CoordinatePackageObservation> ObserveSingle(
        InspectionWorkspace workspace,
        PackageRootBinding binding)
    {
        WorkspaceScopeSnapshot current =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.AddPackagesAsync(
                    current.Revision,
                    [binding],
                    DateTimeOffset.UtcNow.AddMinutes(1),
                    Cancellation)).Snapshot;
        return await Observe(workspace, binding, scope);
    }

    internal static PackageRootBinding Binding(
        string version,
        params (string Entry, string Image)[] assets)
    {
        using var bytes = new MemoryStream();
        using (var archive =
            new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string path, string image) in assets)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                if (!path.EndsWith("/_._", StringComparison.Ordinal))
                    entry.Write(File.ReadAllBytes(image));
            }
        }

        var payload = new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create("coordinate.sample", version),
            new InMemoryPackageContent(
                bytes.ToArray(),
                false,
                PackageProducerIdentity.NuGetOrg.Key),
            PackageProducerIdentity.NuGetOrg.Key,
            PackageProducerIdentity.NuGetOrg,
            PackagePayloadOrigin.Download);
        return PackageRootBinding.CreateFromSource(payload, "net11.0");
    }
}
