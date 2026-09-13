using System.Collections.Immutable;
using System.IO.Compression;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

internal static class NavigationSnapshotTestData
{
    internal static PackageRootBinding Binding(
        string packageId,
        string assemblyName = "Navigation.Library",
        string framework = "net11.0")
        => BindingWithAssemblies(
            packageId,
            framework,
            assemblyName);

    internal static PackageRootBinding BindingWithAssemblies(
        string packageId,
        string framework,
        params string[] assemblyNames)
    {
        byte[] assembly = File.ReadAllBytes(
            typeof(NavigationSnapshotTestData).Assembly.Location);
        return BindingWithAssemblyImages(
            packageId,
            framework,
            [
                .. assemblyNames.Select(
                    name => (name, assembly)),
            ]);
    }

    internal static PackageRootBinding BindingWithAssemblyImages(
        string packageId,
        string framework,
        params (string Name, byte[] Content)[] assemblies)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string name, byte[] content) in assemblies)
            {
                using Stream entry = archive.CreateEntry(
                    $"lib/{framework}/{name}.dll").Open();
                entry.Write(content);
            }
        }

        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(packageId, "1.0.0"),
                new InMemoryPackageContent(
                    bytes.ToArray(),
                    fromCache: false,
                    producerKey: "fixture"),
                "fixture",
                PackagePayloadOrigin.Download),
            framework);
    }

    internal static async Task<WorkspaceScopeSnapshot> ReplaceAsync(
        InspectionWorkspace workspace,
        params PackageRootBinding[] bindings)
    {
        WorkspaceScopeReadResult initial =
            await workspace.GetScopeSnapshotAsync();
        WorkspaceScopeSnapshot snapshot =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                initial).Snapshot;
        WorkspaceScopeOperationResult result =
            await workspace.ReplaceScopeAsync(
                snapshot.Revision,
                [.. bindings],
                DateTimeOffset.UtcNow.AddMinutes(5),
                TestContext.Current.CancellationToken);
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            result).Snapshot;
    }

    internal static NavigationPackageEvaluation PackageEvaluation(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        params LibrarySurface[] libraries)
    {
        var evaluations =
            ImmutableArray.CreateBuilder<NavigationLibraryEvaluation>(
                libraries.Length);
        var entries =
            ImmutableArray.CreateBuilder<
                AssemblyContextEntry<AssemblyApiSurface>>(
                    libraries.Length);
        PackageCompileAsset[] assets = [.. binding.Root.AssetSelection.Assets];
        Assert.Equal(libraries.Length, assets.Length);
        for (int index = 0; index < libraries.Length; index++)
        {
            LibrarySurface item = libraries[index];
            WorkspaceContextMember library =
                Library(binding.Coordinate, item.Name);
            var association = new PackageAssemblyRoleParticipant(
                binding.Root.Identity,
                assets[index],
                library.Participant);
            evaluations.Add(
                new NavigationLibraryEvaluation(
                    binding.Coordinate,
                    association));
            entries.Add(
                item.Error is null
                    ? Available(library, item.Types, item.Failures)
                    : new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                        new AssemblyContextSubject(
                            library.Participant.Assembly),
                        item.Error));
        }

        return new NavigationPackageEvaluation(
            occurrence,
            binding,
            evaluations.MoveToImmutable(),
            new AssemblyContextApiSurfaceResult(
                new AssemblyContextResult<AssemblyApiSurface>(
                    entries.MoveToImmutable()),
                [],
                Truncation: null));
    }

    internal static LibrarySurface Surface(
        string name,
        params ApiType[] types) =>
        new(name, [.. types], [], Error: null);

    internal static ApiType Type(
        string name,
        params ApiMember[] members) =>
        new()
        {
            Namespace = "Sample",
            Name = name,
            DefinitionName = TypeName("Sample", name),
            Accessibility = "public",
            Members = [.. members],
        };

    internal static ApiMember Member(string name) =>
        new()
        {
            Name = name,
            Kind = "method",
            Signature = $"void {name}()",
        };

    internal static ViewFacetAvailabilitySnapshot AllAvailable(
        ViewFacetRegistry registry) =>
        new(
            registry.Descriptors.Select(
                descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        ViewFacetAvailability.Available.Instance)));

    static AssemblyContextEntry<AssemblyApiSurface> Available(
        WorkspaceContextMember library,
        ImmutableArray<ApiType> types,
        ImmutableArray<ApiSurfaceInspectionFailure> failures)
    {
        var surface = new ApiSurface
        {
            Types = [.. types],
            InspectionFailures = [.. failures],
        };
        return new AssemblyContextEntry<AssemblyApiSurface>.Available(
            new AssemblyContextSubject(library.Participant.Assembly),
            new AssemblyApiSurface(surface, failures));
    }

    static WorkspaceContextMember Library(
        RealizedMemberCoordinate.Package coordinate,
        string name)
    {
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.Create(
                new AssemblyReferenceIdentity(
                    name,
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null),
                path: null,
                () => new MemoryStream([0], writable: false),
                AssemblyResolutionProvenance.Package(
                    coordinate.PackageId,
                    coordinate.Version,
                    coordinate.Framework,
                    coordinate.RuntimeIdentifier));
        return new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance));
    }

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    internal sealed record LibrarySurface(
        string Name,
        ImmutableArray<ApiType> Types,
        ImmutableArray<ApiSurfaceInspectionFailure> Failures,
        Exception? Error);
}
