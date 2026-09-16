using ILInspector.Metadata;
using NuGetFetch;
using DotnetInspector.Fixtures;
using DotnetInspector.Presentation;

namespace DotnetInspector.Queries.Tests;

public sealed class ApiCoordinateSourceSelectionQueryTests
{
    [Fact]
    public async Task OverloadOrdinalMoves_MatchingUsesSourceAnchorNotDestinationOrdinal()
    {
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding before = CoordinateLibraryPairingQueryTests.Binding("1.0.0",
            ("lib/net11.0/Fixture.dll", FixtureCatalog.MetadataApiCorrespondencePair.OldAssemblyPath()));
        PackageRootBinding after = CoordinateLibraryPairingQueryTests.Binding("2.0.0",
            ("lib/net11.0/Fixture.dll", FixtureCatalog.MetadataApiCorrespondencePair.NewAssemblyPath()));
        WorkspaceScopeSnapshot initial = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope = Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(initial.Revision, [before, after],
                DateTimeOffset.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken)).Snapshot;
        CoordinatePackageObservation source = await Observe(before);
        CoordinatePackageObservation destination = await Observe(after);
        const string type = "MetadataCorrespondenceFixture.OrdinalSelection";
        ApiCoordinateSourceSelectionResult first = await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
            workspace, source, new("coordinate.sample", "1.0.0", "2.0.0", type, "Pick:1"),
            cancellationToken: TestContext.Current.CancellationToken);
        ApiCoordinateSourceSelectionResult repeatedOrdinal = await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
            workspace, destination, new("coordinate.sample", "2.0.0", "1.0.0", type, "Pick:1"),
            cancellationToken: TestContext.Current.CancellationToken);
        var selected = Assert.IsType<StructuralSubjectIdentity.MemberSubject>(first.Subject);
        var wrongDestination = Assert.IsType<StructuralSubjectIdentity.MemberSubject>(repeatedOrdinal.Subject);
        Assert.Contains("int", selected.Identity.Member.CanonicalSignature);
        Assert.Contains("bool", wrongDestination.Identity.Member.CanonicalSignature);

        var envelope = await ApiCoordinateMatchInspection.ExecuteAsync(
            workspace, selected, ApiDeclarationKind.Method, source, destination,
            TestContext.Current.CancellationToken);

        Assert.Equal(ApiCoordinateMatchStatus.Exact, envelope.Content.Status);
        Assert.Equal(selected.Identity.Member.StableSelector,
            envelope.Content.Destination!.Member!.Value.ToString());
        Assert.NotEqual(wrongDestination.Identity.Member.StableSelector,
            envelope.Content.Destination.Member.Value.ToString());

        async Task<CoordinatePackageObservation> Observe(PackageRootBinding binding) =>
            Assert.IsType<CoordinatePackageObservationResult.Available>(
                await CoordinateLibraryPairingQuery.ObserveAsync(
                    workspace, binding, scope.FindPackageOccurrence(binding)!,
                    TestContext.Current.CancellationToken)).Observation;
    }

    [Theory]
    [InlineData("System.Text.Json", "9.0.0", "10.0.0", "System.Text.Json.JsonSerializer", "Deserialize:1", null)]
    [InlineData("Avalonia", "11.3.14", "12.1.2", "Avalonia.Data.MultiBinding", null, "net8.0")]
    public async Task PinnedSource_SelectsOneExactDeclarationWithoutDestinationLookup(
        string packageId, string before, string after, string type, string? member, string? framework)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        await using var workspace = new InspectionWorkspace();
        PackageRootBinding binding = await packages.BindingAsync(packageId, before, framework);
        WorkspaceScopeSnapshot snapshot = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot committed = Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(snapshot.Revision, [binding],
                DateTimeOffset.UtcNow.AddMinutes(1), TestContext.Current.CancellationToken)).Snapshot;
        CoordinatePackageObservation observation =
            Assert.IsType<CoordinatePackageObservationResult.Available>(
                await CoordinateLibraryPairingQuery.ObserveAsync(
                    workspace, binding, Assert.Single(committed.Packages),
                    TestContext.Current.CancellationToken)).Observation;

        ApiCoordinateSourceSelectionResult result = await ApiCoordinateSourceSelectionQuery.ExecuteAsync(
            workspace, observation, new(packageId, before, after, type, member, framework),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Status == ApiCoordinateSourceSelectionStatus.Selected,
            $"{result.Status}: {result.Failure}\n{string.Join("\n", result.InspectionFailures)}");
        StructuralSubjectIdentity.TypeSubject selectedType = result.Subject switch
        {
            StructuralSubjectIdentity.TypeSubject selected => selected,
            StructuralSubjectIdentity.MemberSubject selected => selected.DeclaringType,
            _ => throw new InvalidOperationException("Selection did not produce an API subject."),
        };
        Assert.Equal(type, selectedType.Identity.Type.ToEscapedFullName());
        Assert.Equal(before, selectedType.Library.Package.Descriptor.PackageVersion);
        if (packageId == "Avalonia")
            Assert.Equal("Avalonia.Markup", selectedType.Library.Identity.Assembly.Name);
        if (member is not null)
            Assert.NotEmpty(Assert.IsType<StructuralSubjectIdentity.MemberSubject>(result.Subject)
                .Identity.Member.StableSelector);
        Assert.Equal(PackageSourceCoordinate.Create(packageId, before), Assert.Single(packages.Requests));
    }
}
