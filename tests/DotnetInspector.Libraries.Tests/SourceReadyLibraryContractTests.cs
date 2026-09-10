using System.Reflection;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using ILInspector.Metadata;

namespace DotnetInspector.Libraries.Tests;

public sealed class SourceReadyLibraryContractTests
{
    [Fact]
    public async Task SourceReadyLibrary_BindsPathlessAssemblyAndPdbContent()
    {
        await using PublishedArtifacts artifacts =
            await PublishArtifactsAsync();

        var assembly = new SourceReadyAssembly(
            artifacts.AssemblyContent,
            artifacts.AssemblyProjection);
        ArtifactContentReference[] retainedSource =
            [artifacts.SourceContent];
        var library = new SourceReadyLibrary(
            assembly,
            artifacts.PortablePdbContent,
            retainedSource);
        retainedSource[0] = artifacts.OtherAssemblyContent;

        Assert.Same(artifacts.AssemblyContent, library.Assembly.Content);
        Assert.Same(
            artifacts.AssemblyProjection,
            library.Assembly.Projection);
        Assert.Same(
            artifacts.AssemblyProvenance,
            library.Assembly.Provenance);

        SourceReadyPortablePdbCandidate candidate =
            Assert.IsType<SourceReadyPortablePdbCandidate>(
                library.PortablePdbCandidate);
        Assert.Same(
            artifacts.AssemblyProjection.Registration,
            candidate.Assembly);
        Assert.Same(artifacts.PortablePdbContent, candidate.Content);
        Assert.Same(artifacts.PdbProvenance, candidate.Provenance);
        SourceReadySourceContent source =
            Assert.Single(library.RetainedSourceContent);
        Assert.Same(artifacts.SourceContent, source.Content);
        Assert.Same(artifacts.SourceProvenance, source.Provenance);

        AssertPathlessPublicSurface(typeof(SourceReadyAssembly));
        AssertPathlessPublicSurface(typeof(SourceReadyPortablePdbCandidate));
        AssertPathlessPublicSurface(typeof(SourceReadySourceContent));
        AssertPathlessPublicSurface(typeof(SourceReadyLibrary));
    }

    [Fact]
    public async Task SourceReadyAssembly_RejectsProjectionForAnotherArtifact()
    {
        await using PublishedArtifacts artifacts =
            await PublishArtifactsAsync();

        Assert.Throws<ArgumentException>(
            () => new SourceReadyAssembly(
                artifacts.OtherAssemblyContent,
                artifacts.AssemblyProjection));
    }

    [Fact]
    public async Task SourceReadyAssembly_RejectsProjectionForAnotherGeneration()
    {
        await using PublishedArtifacts artifacts =
            await PublishArtifactsAsync();
        ArtifactAssemblyProjection projection =
            artifacts.AssemblyProjection with
            {
                Registration = artifacts.AssemblyProjection.Registration with
                {
                    Generation =
                        new ArtifactGenerationAuthority().Generation,
                },
            };

        Assert.Throws<ArgumentException>(
            () => new SourceReadyAssembly(
                artifacts.AssemblyContent,
                projection));
    }

    [Fact]
    public async Task SourceReadyLibrary_RejectsAssemblyAsPortablePdb()
    {
        await using PublishedArtifacts artifacts =
            await PublishArtifactsAsync();
        var assembly = new SourceReadyAssembly(
            artifacts.AssemblyContent,
            artifacts.AssemblyProjection);

        Assert.Throws<ArgumentException>(
            () => new SourceReadyLibrary(
                assembly,
                artifacts.AssemblyContent));
    }

    [Fact]
    public async Task SourceReadyLibrary_AllowsMissingPortablePdb()
    {
        await using PublishedArtifacts artifacts =
            await PublishArtifactsAsync();
        var assembly = new SourceReadyAssembly(
            artifacts.AssemblyContent,
            artifacts.AssemblyProjection);

        var library = new SourceReadyLibrary(assembly);

        Assert.Same(assembly, library.Assembly);
        Assert.Null(library.PortablePdbCandidate);
    }

    [Fact]
    public async Task SourceReadyLibrary_RetainsArtifactLeaseInvalidation()
    {
        PublishedArtifacts artifacts = await PublishArtifactsAsync();
        var library = new SourceReadyLibrary(
            new SourceReadyAssembly(
                artifacts.AssemblyContent,
                artifacts.AssemblyProjection),
            artifacts.PortablePdbContent,
            [artifacts.SourceContent]);

        artifacts.Lease.Dispose();

        Assert.Same(
            artifacts.AssemblyProvenance,
            library.Assembly.Provenance);
        Assert.Same(
            artifacts.PdbProvenance,
            library.PortablePdbCandidate!.Provenance);
        Assert.Same(
            artifacts.SourceProvenance,
            Assert.Single(library.RetainedSourceContent).Provenance);
        Assert.Throws<ObjectDisposedException>(
            () => library.Assembly.Content.OpenRead());
        Assert.Throws<ObjectDisposedException>(
            () => library.PortablePdbCandidate!.Content.OpenRead());
        Assert.Throws<ObjectDisposedException>(
            () => Assert.Single(
                library.RetainedSourceContent).Content.OpenRead());

        await artifacts.DisposeAsync();
    }

    private static async Task<PublishedArtifacts> PublishArtifactsAsync()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        byte[] assemblyBytes = await File.ReadAllBytesAsync(
            Assembly.GetExecutingAssembly().Location,
            cancellationToken);
        var assemblyProvenance = new Provenance("package-assembly");
        var otherAssemblyProvenance = new Provenance("platform-assembly");
        var pdbProvenance = new Provenance("symbol-server-pdb");
        var sourceProvenance = new Provenance("source-document");
        var session = new ArtifactSetSession();

        ArtifactContribution? assemblyContribution = null;
        ArtifactContribution? otherAssemblyContribution = null;
        ArtifactContribution? pdbContribution = null;
        ArtifactContribution? sourceContribution = null;
        await session.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                assemblyContribution = Register(
                    scope,
                    assemblyProvenance,
                    assemblyBytes,
                    "assembly");
                otherAssemblyContribution = Register(
                    scope,
                    otherAssemblyProvenance,
                    assemblyBytes,
                    "assembly");
                pdbContribution = Register(
                    scope,
                    pdbProvenance,
                    [0x42, 0x53, 0x4a, 0x42],
                    "portable-pdb");
                sourceContribution = Register(
                    scope,
                    sourceProvenance,
                    "class C {}"u8.ToArray(),
                    "source");
                return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                    new ArtifactAcquisitionOutcome.Acquired(
                        [
                            assemblyContribution!,
                            otherAssemblyContribution!,
                            pdbContribution!,
                            sourceContribution!,
                        ],
                        ArtifactAcquisitionLeases.None));
            },
            cancellationToken: cancellationToken);

        var projections =
            new Dictionary<ArtifactIdentity, ArtifactAssemblyProjection>();
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await session.SealWithProjectionAsync(
                (view, token) =>
                {
                    if (ReferenceEquals(
                            view.Artifact,
                            pdbContribution!.Descriptor.Identity)
                        || ReferenceEquals(
                            view.Artifact,
                            sourceContribution!.Descriptor.Identity))
                    {
                        return null;
                    }

                    ArtifactAssemblyProjection projection =
                        Assert.IsType<
                            ArtifactAssemblyProjectionOutcome.Projected>(
                                ArtifactAssemblyInspection.Project(
                                    view,
                                    token))
                            .Value;
                    projections.Add(view.Artifact, projection);
                    return null;
                },
                cancellationToken));

        ArtifactQueryLease lease = session.IssueLease(
            session.CreateQueryAuthorization());
        return new PublishedArtifacts(
            session,
            lease,
            session.GetContentReference(
                assemblyContribution!.Descriptor.Identity,
                lease),
            projections[assemblyContribution.Descriptor.Identity],
            session.GetContentReference(
                otherAssemblyContribution!.Descriptor.Identity,
                lease),
            session.GetContentReference(
                pdbContribution!.Descriptor.Identity,
                lease),
            session.GetContentReference(
                sourceContribution!.Descriptor.Identity,
                lease),
            assemblyProvenance,
            pdbProvenance,
            sourceProvenance);
    }

    private static ArtifactContribution Register(
        ArtifactContributionScope scope,
        IArtifactProvenance provenance,
        byte[] content,
        string kind) =>
        scope.Register(
            provenance,
            _ => new MemoryStream(content, writable: false),
            kind: kind);

    private static void AssertPathlessPublicSurface(Type type)
    {
        PropertyInfo[] properties =
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.DoesNotContain(
            properties,
            property =>
                property.Name.Contains("Path", StringComparison.Ordinal)
                || typeof(Stream).IsAssignableFrom(property.PropertyType)
                || typeof(Delegate).IsAssignableFrom(property.PropertyType));
    }

    private sealed record Provenance(string Source) :
        IArtifactProvenance;

    private sealed class PublishedArtifacts : IAsyncDisposable
    {
        public PublishedArtifacts(
            ArtifactSetSession session,
            ArtifactQueryLease lease,
            ArtifactContentReference assemblyContent,
            ArtifactAssemblyProjection assemblyProjection,
            ArtifactContentReference otherAssemblyContent,
            ArtifactContentReference portablePdbContent,
            ArtifactContentReference sourceContent,
            IArtifactProvenance assemblyProvenance,
            IArtifactProvenance pdbProvenance,
            IArtifactProvenance sourceProvenance)
        {
            Session = session;
            Lease = lease;
            AssemblyContent = assemblyContent;
            AssemblyProjection = assemblyProjection;
            OtherAssemblyContent = otherAssemblyContent;
            PortablePdbContent = portablePdbContent;
            SourceContent = sourceContent;
            AssemblyProvenance = assemblyProvenance;
            PdbProvenance = pdbProvenance;
            SourceProvenance = sourceProvenance;
        }

        public ArtifactSetSession Session { get; }
        public ArtifactQueryLease Lease { get; }
        public ArtifactContentReference AssemblyContent { get; }
        public ArtifactAssemblyProjection AssemblyProjection { get; }
        public ArtifactContentReference OtherAssemblyContent { get; }
        public ArtifactContentReference PortablePdbContent { get; }
        public ArtifactContentReference SourceContent { get; }
        public IArtifactProvenance AssemblyProvenance { get; }
        public IArtifactProvenance PdbProvenance { get; }
        public IArtifactProvenance SourceProvenance { get; }

        public async ValueTask DisposeAsync()
        {
            Lease.Dispose();
            await Session.DisposeAsync();
        }
    }
}
