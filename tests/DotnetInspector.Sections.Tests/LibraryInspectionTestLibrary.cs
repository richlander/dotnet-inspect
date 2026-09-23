using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Sections.Tests;

internal sealed class LibraryInspectionTestLibrary : IAsyncDisposable
{
    private readonly ArtifactSetSession _session;
    private readonly LibraryContentOwner _owner;

    private LibraryInspectionTestLibrary(
        ArtifactSetSession session,
        LibraryContentOwner owner)
    {
        _session = session;
        _owner = owner;
    }

    public LibraryReference Reference => _owner.Reference;

    public LibraryContentOwnerState State => _owner.State;

    public static async Task<LibraryInspectionTestLibrary> CreateAsync(
        byte[] content,
        ManagedMetadataIdentity.Assembly identity)
    {
        var session = new ArtifactSetSession();
        try
        {
            ArtifactContribution? contribution = null;
            await session.AddRequiredAcquisitionAsync(
                (scope, cancellationToken) =>
                {
                    contribution = scope.Register(
                        new Provenance("library-inspection-test"),
                        token =>
                        {
                            token.ThrowIfCancellationRequested();
                            return new MemoryStream(
                                content,
                                writable: false);
                        });
                    return ValueTask.FromResult<
                        ArtifactAcquisitionOutcome>(
                            new ArtifactAcquisitionOutcome.Acquired(
                                [contribution],
                                ArtifactAcquisitionLeases.None));
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                await session.SealAsync(
                    TestContext.Current.CancellationToken));

            ArtifactQueryLease queryLease = session.IssueLease(
                session.CreateQueryAuthorization());
            ArtifactContentLease? contentLease = null;
            try
            {
                ArtifactContentReference reference =
                    session.GetContentReference(
                        contribution!.Descriptor.Identity,
                        queryLease);
                contentLease =
                    session.IssueContentLease(reference, queryLease);
                LibraryReference library =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            reference,
                            identity,
                            reference,
                            identity));
                var owner = new LibraryContentOwner(
                    library,
                    [contentLease]);
                contentLease = null;
                return new LibraryInspectionTestLibrary(session, owner);
            }
            finally
            {
                contentLease?.Dispose();
                queryLease.Dispose();
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    public LibraryOperationLease IssueOperation() =>
        Assert.IsType<LibraryOperationLeaseIssueOutcome.Issued>(
                _owner.IssueOperationLease(Reference))
            .Lease;

    public async Task RetireAsync()
    {
        await _owner.DisposeAsync()
            .AsTask()
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(LibraryContentOwnerState.Released, State);
    }

    public async ValueTask DisposeAsync()
    {
        await _owner.DisposeAsync();
        await _session.DisposeAsync();
    }

    public static async Task<byte[]> RealSystemTextJsonAsync() =>
        await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "LibraryOverview",
                "System.Text.Json.dll"),
            TestContext.Current.CancellationToken);

    public static ManagedMetadataIdentity.Assembly Identity(
        byte[] content)
    {
        using var peReader = new PEReader(
            new MemoryStream(content, writable: false));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);
        return new ManagedMetadataIdentity.Assembly(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    public static ManagedMetadataIdentity.Assembly ProbeIdentity() =>
        new(
            new AssemblyReferenceIdentity(
                "Probe",
                new Version(1, 0, 0, 0),
                Culture: null,
                PublicKeyToken: null));

    public static byte[] BuildMetadataImage(
        bool includeAssembly = true,
        bool emptyModuleVersionId = false,
        bool malformedPublicType = false,
        string metadataVersion = "v4.0.30319")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            emptyModuleVersionId
                ? default
                : metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        if (includeAssembly)
        {
            metadata.AddAssembly(
                metadata.GetOrAddString("Probe"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        }

        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (malformedPublicType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("Broken"),
                metadata.GetOrAddString("Declaration"),
                MetadataTokens.TypeReferenceHandle(1),
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                metadataVersion,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    private sealed record Provenance(string Name) : IArtifactProvenance;
}
