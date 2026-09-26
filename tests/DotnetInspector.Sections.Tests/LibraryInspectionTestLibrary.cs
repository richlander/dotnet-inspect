using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

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

    public static Task<LibraryInspectionTestLibrary> CreateAsync(
        byte[] content,
        ManagedMetadataIdentity.Assembly identity) =>
        CreateAsync(content, identity, implementation: content);

    /// <summary>
    /// Creates a Library whose API and implementation roles are separate
    /// contents, or whose API content has no implementation when
    /// <paramref name="implementation"/> is null.
    /// </summary>
    public static async Task<LibraryInspectionTestLibrary> CreateAsync(
        byte[] api,
        ManagedMetadataIdentity.Assembly apiIdentity,
        byte[]? implementation)
    {
        var session = new ArtifactSetSession();
        try
        {
            bool shared = ReferenceEquals(api, implementation);
            ArtifactContribution? apiContribution = null;
            ArtifactContribution? implementationContribution = null;
            await session.AddRequiredAcquisitionAsync(
                (scope, cancellationToken) =>
                {
                    apiContribution = Register(scope, api, "api");
                    implementationContribution =
                        implementation is null || shared
                            ? null
                            : Register(scope, implementation, "implementation");
                    return ValueTask.FromResult<
                        ArtifactAcquisitionOutcome>(
                            new ArtifactAcquisitionOutcome.Acquired(
                                implementationContribution is null
                                    ? [apiContribution]
                                    : [apiContribution, implementationContribution],
                                ArtifactAcquisitionLeases.None));
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                await session.SealAsync(
                    TestContext.Current.CancellationToken));

            ArtifactQueryLease queryLease = session.IssueLease(
                session.CreateQueryAuthorization());
            var contentLeases = new List<ArtifactContentLease>();
            try
            {
                ArtifactContentReference apiReference =
                    session.GetContentReference(
                        apiContribution!.Descriptor.Identity,
                        queryLease);
                contentLeases.Add(
                    session.IssueContentLease(apiReference, queryLease));
                ArtifactContentReference? implementationReference = null;
                ManagedMetadataIdentity.Assembly? implementationIdentity = null;
                if (shared)
                {
                    implementationReference = apiReference;
                    implementationIdentity = apiIdentity;
                }
                else if (implementationContribution is not null)
                {
                    implementationReference =
                        session.GetContentReference(
                            implementationContribution.Descriptor.Identity,
                            queryLease);
                    implementationIdentity = Identity(implementation!);
                    contentLeases.Add(
                        session.IssueContentLease(
                            implementationReference,
                            queryLease));
                }

                LibraryReference library =
                    LibraryReference.CreateDirect(
                        new LibraryAssemblyCorrespondence(
                            apiReference,
                            apiIdentity,
                            implementationReference,
                            implementationIdentity));
                var owner = new LibraryContentOwner(
                    library,
                    [.. contentLeases]);
                contentLeases.Clear();
                return new LibraryInspectionTestLibrary(session, owner);
            }
            finally
            {
                foreach (ArtifactContentLease lease in contentLeases)
                    lease.Dispose();
                queryLease.Dispose();
            }
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static ArtifactContribution Register(
        ArtifactContributionScope scope,
        byte[] content,
        string role) =>
        scope.Register(
            new Provenance($"library-inspection-test/{role}"),
            token =>
            {
                token.ThrowIfCancellationRequested();
                return new MemoryStream(
                    content,
                    writable: false);
            });

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

    public static async Task<byte[]> PinnedNet11Async(
        string pack,
        string fileName) =>
        await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Net11",
                pack,
                fileName),
            TestContext.Current.CancellationToken);

    public static async Task<byte[]> RealNetstandardAsync() =>
        await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "LibraryOverview",
                "netstandard.dll"),
            TestContext.Current.CancellationToken);

    public static async Task<byte[]> RealSystemXmlAsync() =>
        await File.ReadAllBytesAsync(
            Path.Combine(
                RuntimeEnvironment.GetRuntimeDirectory(),
                "System.Xml.dll"),
            TestContext.Current.CancellationToken);

    public static async Task<byte[]> NamespaceSuffixFixtureAsync() =>
        await File.ReadAllBytesAsync(
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
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
        bool includeModuleExport = false,
        bool includeGlobalType = false,
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
        if (includeGlobalType)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract,
                default,
                metadata.GetOrAddString("GlobalProbe"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
        }
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
        if (includeModuleExport)
        {
            AssemblyFileHandle file = metadata.AddAssemblyFile(
                metadata.GetOrAddString("Probe.netmodule"),
                default,
                containsMetadata: true);
            metadata.AddExportedType(
                TypeAttributes.Public,
                metadata.GetOrAddString("Probe"),
                metadata.GetOrAddString("Exported"),
                file,
                typeDefinitionId: 1);
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
