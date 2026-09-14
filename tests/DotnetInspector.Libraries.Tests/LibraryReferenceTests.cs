using System.Reflection;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Libraries.Tests;

public sealed class LibraryReferenceTests
{
    [Fact]
    public async Task
        EqualPackageAndPlatformAssemblies_RemainSourceDistinct()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(2);
        ManagedMetadataIdentity.Assembly identity =
            Identity("System.Text.Json", new Version(11, 0, 0, 0));
        var packageCoordinate = new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create(
                "System.Text.Json",
                "11.0.0"),
            identity);
        var platformCoordinate = new ExactLibrarySourceCoordinate.Platform(
            new(PlatformFamily.DotNetRuntime),
            identity);

        LibraryReference package = LibraryReference.CreateFromSource(
            packageCoordinate,
            new(artifacts[0], identity));
        LibraryReference platform = LibraryReference.CreateFromSource(
            platformCoordinate,
            new(artifacts[1], identity));
        LibraryReference secondPackage =
            LibraryReference.CreateFromSource(
                packageCoordinate,
                new(artifacts[0], identity));

        Assert.NotSame(package, platform);
        Assert.NotEqual(
            package.SourceCoordinate,
            platform.SourceCoordinate);
        Assert.Same(artifacts[0], package.ApiAssembly.ArtifactReference);
        Assert.Same(artifacts[1], platform.ApiAssembly.ArtifactReference);
        Assert.NotSame(package, secondPackage);
        Assert.NotSame(package.ApiAssembly, secondPackage.ApiAssembly);
        Assert.Same(
            package.SourceCoordinate,
            secondPackage.SourceCoordinate);
    }

    [Fact]
    public async Task
        ContentReferences_RetainExactRolesAndCorrespondence()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(4);
        ManagedMetadataIdentity.Assembly identity =
            Identity("System.Text.Json", new Version(11, 0, 0, 0));
        var assembly = new LibraryAssemblyCorrespondence(
            artifacts[0],
            identity,
            artifacts[1],
            identity);
        var xml = new LibraryCompanionCorrespondence(
            artifacts[2],
            LibraryContentRole.CompiledXmlDocumentation,
            artifacts[0]);
        var pdb = new LibraryCompanionCorrespondence(
            artifacts[3],
            LibraryContentRole.PortablePdb,
            artifacts[1]);
        LibraryCompanionCorrespondence[] companions = [xml, pdb];

        LibraryReference library = LibraryReference.CreateFromSource(
            new ExactLibrarySourceCoordinate.Platform(
                new(PlatformFamily.DotNetRuntime),
                identity),
            assembly,
            companions);
        companions[0] = pdb;

        Assert.False(library.IsDirectArtifact);
        Assert.Same(assembly, library.AssemblyCorrespondence);
        Assert.Equal(4, library.Contents.Count);
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.False(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.Same(
            identity,
            library.ApiAssembly.AssemblyIdentity);
        LibraryContentReference implementation =
            Assert.IsType<LibraryContentReference>(
                library.ImplementationAssembly);
        Assert.True(
            implementation.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.Same(identity, implementation.AssemblyIdentity);

        LibraryContentReference xmlReference =
            Assert.Single(
                library.Contents,
                content => content.HasRole(
                    LibraryContentRole.CompiledXmlDocumentation));
        LibraryContentReference pdbReference =
            Assert.Single(
                library.Contents,
                content => content.HasRole(
                    LibraryContentRole.PortablePdb));
        Assert.Same(library.ApiAssembly, xmlReference.AssociatedAssembly);
        Assert.Same(
            implementation,
            pdbReference.AssociatedAssembly);
        Assert.Same(artifacts[2].Registration, xmlReference.Registration);
        Assert.Same(artifacts[3].Provenance, pdbReference.Provenance);
        Assert.Same(xml, library.CompanionCorrespondences[0]);
        Assert.All(
            library.Contents,
            content => Assert.Same(library, content.Library));
    }

    [Fact]
    public async Task DirectAssembly_MayServeBothAssemblyRoles()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(1);
        ManagedMetadataIdentity.Assembly identity =
            Identity("Contoso.Direct", new Version(1, 0, 0, 0));
        LibraryReference library = LibraryReference.CreateDirect(
            new LibraryAssemblyCorrespondence(
                artifacts[0],
                identity,
                artifacts[0],
                identity));

        Assert.True(library.IsDirectArtifact);
        Assert.Null(library.SourceCoordinate);
        Assert.Same(
            library.ApiAssembly,
            library.ImplementationAssembly);
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ApiAssembly));
        Assert.True(
            library.ApiAssembly.HasRole(
                LibraryContentRole.ImplementationAssembly));
        Assert.Single(library.Contents);
    }

    [Fact]
    public async Task Construction_RejectsUncorrelatedContent()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync(4);
        ManagedMetadataIdentity.Assembly identity =
            Identity("Contoso.Library", new Version(1, 0, 0, 0));
        var assembly = new LibraryAssemblyCorrespondence(
            artifacts[0],
            identity,
            artifacts[1],
            identity);

        Assert.Throws<ArgumentException>(
            () => LibraryReference.CreateDirect(
                assembly,
                [
                    new(
                        artifacts[2],
                        LibraryContentRole.CompiledXmlDocumentation,
                        artifacts[3]),
                ]));
        Assert.Throws<ArgumentException>(
            () => LibraryReference.CreateDirect(
                assembly,
                [
                    new(
                        artifacts[2],
                        LibraryContentRole.PortablePdb,
                        artifacts[0]),
                ]));
        Assert.Throws<ArgumentException>(
            () => LibraryReference.CreateFromSource(
                new ExactLibrarySourceCoordinate.Package(
                    PackageSourceCoordinate.Create(
                        "Contoso.Other",
                        "1.0.0"),
                    Identity(
                        "Contoso.Other",
                        new Version(1, 0, 0, 0))),
                assembly));
    }

    [Fact]
    public async Task Construction_RejectsCrossGenerationContent()
    {
        await using ArtifactFixture first =
            await ArtifactFixture.CreateAsync(1);
        await using ArtifactFixture second =
            await ArtifactFixture.CreateAsync(1);
        ManagedMetadataIdentity.Assembly identity =
            Identity("Contoso.Library", new Version(1, 0, 0, 0));

        Assert.Throws<ArgumentException>(
            () => new LibraryAssemblyCorrespondence(
                first[0],
                identity,
                second[0],
                identity));
        Assert.Throws<ArgumentException>(
            () => new LibraryCompanionCorrespondence(
                first[0],
                LibraryContentRole.CompiledXmlDocumentation,
                second[0]));
    }

    [Fact]
    public void ReferenceContracts_AreResourceFree()
    {
        Type[] referenceTypes =
        [
            typeof(LibraryReference),
            typeof(LibraryContentReference),
            typeof(LibraryAssemblyCorrespondence),
            typeof(LibraryCompanionCorrespondence),
        ];

        Assert.All(
            referenceTypes,
            type =>
            {
                Assert.False(typeof(IDisposable).IsAssignableFrom(type));
                Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
                Assert.Null(
                    type.GetCustomAttribute<ResourceOwnershipAttribute>(
                        inherit: false));
                Assert.DoesNotContain(
                    type.GetFields(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic),
                    field =>
                        typeof(IDisposable).IsAssignableFrom(field.FieldType)
                        || typeof(IAsyncDisposable).IsAssignableFrom(
                            field.FieldType)
                        || typeof(Delegate).IsAssignableFrom(field.FieldType)
                        || typeof(Stream).IsAssignableFrom(field.FieldType));
            });
    }

    private static ManagedMetadataIdentity.Assembly Identity(
        string name,
        Version version) =>
        new(
            new AssemblyReferenceIdentity(
                name,
                version,
                Culture: null,
                PublicKeyToken: null));

    private sealed class ArtifactFixture : IAsyncDisposable
    {
        private readonly ArtifactSetSession _session;
        private readonly ArtifactQueryLease _queryLease;
        private readonly IReadOnlyList<ArtifactContentReference> _references;

        private ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            IReadOnlyList<ArtifactContentReference> references)
        {
            _session = session;
            _queryLease = queryLease;
            _references = references;
        }

        public ArtifactContentReference this[int index] =>
            _references[index];

        public static async Task<ArtifactFixture> CreateAsync(int count)
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                for (int index = 0; index < count; index++)
                {
                    int value = index;
                    await session.AddRequiredAcquisitionAsync(
                        (scope, _) =>
                        {
                            ArtifactContribution contribution =
                                scope.Register(
                                    new Provenance($"artifact-{value}"),
                                    _ => new MemoryStream(
                                        [(byte)value],
                                        writable: false));
                            return ValueTask.FromResult<
                                ArtifactAcquisitionOutcome>(
                                    new ArtifactAcquisitionOutcome.Acquired(
                                        [contribution],
                                        ArtifactAcquisitionLeases.None));
                        },
                        cancellationToken: cancellationToken);
                }

                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryLease queryLease = session.IssueLease(
                    session.CreateQueryAuthorization());
                IReadOnlyList<ArtifactContentReference> references =
                    session.GetCatalog(queryLease)
                        .Select(
                            descriptor => session.GetContentReference(
                                descriptor.Identity,
                                queryLease))
                        .ToArray();
                return new ArtifactFixture(
                    session,
                    queryLease,
                    references);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            _queryLease.Dispose();
            await _session.DisposeAsync();
        }
    }

    private sealed record Provenance(string Name) : IArtifactProvenance;
}
