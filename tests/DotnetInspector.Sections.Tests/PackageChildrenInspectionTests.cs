using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class PackageChildrenInspectionTests
{
    [Fact]
    public async Task
        RealSystemTextJson_PreservesAssetIdentityAndCountsForwarders()
    {
        byte[] content =
            await LibraryInspectionTestLibrary.RealSystemTextJsonAsync();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = Group(workspace, content);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var target = new PackageLibraryInspectionTarget(
            "ref/net10.0/System.Text.Json.dll",
            "ref/net10.0/System.Text.Json.dll",
            PackageLibraryChildRole.Compile,
            group,
            participant);

        InspectionEnvelope<PackageChildrenDocument> envelope =
            await PackageChildrenInspection.ExecuteLibrariesAsync(
                new(
                    "System.Text.Json",
                    "10.0.0",
                    "net10.0"),
                [target],
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Empty(envelope.Diagnostics);
        Assert.True(envelope.Content.IsComplete);
        Assert.Equal(
            PackageChildrenKind.Libraries,
            envelope.Content.Kind);
        Assert.Equal(
            PackageChildrenStatus.Available,
            envelope.Content.Status);
        PackageLibraryChild library =
            Assert.Single(envelope.Content.Libraries);
        Assert.Equal(
            "ref/net10.0/System.Text.Json.dll",
            library.AssetId.ToString());
        Assert.Equal(
            "ref/net10.0/System.Text.Json.dll",
            library.AssetPath.ToString());
        Assert.Equal(
            "System.Text.Json",
            library.AssemblyName.ToString());
        Assert.Equal(PackageLibraryChildRole.Compile, library.Role);
        LibraryTypePopulationCountOutcome.Counted count =
            Assert.IsType<
                LibraryTypePopulationCountOutcome.Counted>(
                    library.PublicTypeDeclarations);
        Assert.True(count.Total > 0);
        Assert.True(count.Forwarders > 0);
        Assert.Null(library.Unavailable);
        Assert.True(library.IsAvailable);
    }

    [Fact]
    public void RuntimeIdentifierPackages_AreTypedAndDeterministic()
    {
        PackageChildrenDocument document =
            PackageChildrenDocument.FromRuntimeIdentifierPackages(
                new("dotnet-inspect", "0.26.0"),
                [
                    new("win-x64", "dotnet-inspect.win-x64"),
                    new("any", "dotnet-inspect.any"),
                ]);

        Assert.Equal(
            PackageChildrenKind.RuntimeIdentifierPackages,
            document.Kind);
        Assert.Equal(
            PackageChildrenStatus.Available,
            document.Status);
        Assert.Equal(
            ["any", "win-x64"],
            document.RuntimeIdentifierPackages
                .Select(static row =>
                    row.RuntimeIdentifier.ToString()));
        Assert.Empty(document.Libraries);
        Assert.True(document.IsComplete);
    }

    [Fact]
    public void NativeToolPackage_StatesThatItHasNoManagedLibraries()
    {
        PackageChildrenDocument document =
            PackageChildrenDocument.NoManagedLibraries(
                new("dotnet-inspect.osx-arm64", "0.26.0"),
                "Native executable");

        Assert.Equal(
            PackageChildrenKind.NoManagedLibraries,
            document.Kind);
        Assert.Equal(
            "Native executable",
            document.Detail?.ToString());
        Assert.Empty(document.Libraries);
        Assert.Empty(document.RuntimeIdentifierPackages);
        Assert.True(document.IsComplete);
    }

    private static AssemblyContextGroup Group(
        InspectionWorkspace workspace,
        byte[] content)
    {
        ImmutableArray<byte> image =
            ImmutableCollectionsMarshal.AsImmutableArray(content);
        using var reader = new PEReader(image);
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        var participant = new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(content, writable: false),
                AssemblyResolutionProvenance.Local(
                    "package-children-real-asset")),
            new MissingBindingPolicy());
        return workspace.CreateAssemblyContextGroup([participant]);
    }

    private sealed class MissingBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } =
            new();

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request) =>
            new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind
                            .CandidateUnavailable)));
    }
}
