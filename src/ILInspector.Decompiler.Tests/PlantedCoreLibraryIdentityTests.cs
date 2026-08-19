using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Core-library identity comes from acquisition, not from what an assembly says
/// about itself. The platform public keys are published data and nothing here
/// verifies a strong-name signature, so a planted file can carry the ECMA key
/// verbatim. Before <c>CoreLibraryIdentityTrust</c>, that was enough to mint
/// <c>corelib</c> for the planted file's own definitions, and a fake
/// <c>System.Collections.IEnumerable</c> then compared equal to the real one —
/// authorizing collection-initializer raising for a type that implements
/// nothing of the sort.
/// </summary>
public class PlantedCoreLibraryIdentityTests
{
    /// <summary>
    /// A resolver-opened file carrying the ECMA public key does not get to name
    /// its own definitions as core-library types. Fails if the trust check in
    /// <c>TypeRefDecoder.CanonicalSelf</c> is removed, or if the classification
    /// at <c>MetadataContext.Open(ResolvedAssemblyReference)</c> stops running:
    /// the fake interface then satisfies the real one.
    /// </summary>
    [Fact]
    public void PlantedPlatformKey_DoesNotMintCoreLibraryIdentity()
    {
        string directory = Directory.CreateTempSubdirectory(
            "planted-corelib-identity-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            using var source = MetadataSource.Open(
                typeof(object).Assembly.Location,
                null,
                TestAssemblyReferenceResolvers.SingleAssembly(path));
            TypeRef fake = TypeRef.Definition("System.Runtime", "N", "Fake");

            Assert.NotEqual(
                MetadataFactState.Yes,
                source.SupportsCollectionInitializer(fake));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The same file opened directly is the caller's designated target, which is
    /// trusted by designation, so it keeps the identity its key claims. This is
    /// the negative case for the rule above: the deny list must be scoped to
    /// resolution, not applied to every reader.
    /// </summary>
    [Fact]
    public void DesignatedTarget_KeepsCoreLibraryIdentity()
    {
        string directory = Directory.CreateTempSubdirectory(
            "designated-corelib-identity-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            using var source = MetadataSource.OpenWithoutSymbols(path);
            TypeRef decoded = TypeRefDecoder.Instance.GetTypeFromDefinition(
                source.Reader,
                MetadataTokens.TypeDefinitionHandle(2),
                rawTypeKind: 0);

            Assert.Equal(TypeRef.CoreLibrary, decoded.Assembly);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// An assembly the caller designated — a corpus path, or a build layout the
    /// user named — keeps core-library identity even though it is reached by
    /// resolution rather than being the target. Designation is the caller's
    /// statement of trust, and it is what separates a dotnet/runtime build
    /// layout from a planted sibling: the two are indistinguishable in
    /// metadata. Fails if <c>DesignatedAsset</c> stops being honoured, which
    /// would silently degrade raising for corpus and build-layout inspection.
    /// </summary>
    [Fact]
    public void DesignatedAcquisition_KeepsCoreLibraryIdentity()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Designated("corpus"),
            CoreLibraryTrustPolicy.DesignatedAndPlatform,
            expectedCorelib: true);
    }

    /// <summary>
    /// A discovered sibling is denied under the default policy but honoured
    /// when the host opts in. Fails if the policy knob stops being consulted,
    /// which would leave a host unable to inspect a build layout it trusts.
    /// </summary>
    [Fact]
    public void DiscoveredSibling_FollowsTheHostPolicy()
    {
        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Local("sibling"),
            CoreLibraryTrustPolicy.DesignatedAndPlatform,
            expectedCorelib: false);

        RunWithResolvedCoreLibrary(
            AssemblyResolutionProvenance.Local("sibling"),
            CoreLibraryTrustPolicy.IncludeDiscovered,
            expectedCorelib: true);
    }

    /// <summary>
    /// Opens the planted assembly through reference resolution with a given
    /// acquisition provenance and host policy, and reports whether its own
    /// definitions decoded as core-library types.
    /// </summary>
    static void RunWithResolvedCoreLibrary(
        AssemblyResolutionProvenance provenance,
        CoreLibraryTrustPolicy policy,
        bool expectedCorelib)
    {
        string directory = Directory.CreateTempSubdirectory(
            "resolved-corelib-identity-").FullName;
        try
        {
            string path = Path.Combine(directory, "System.Runtime.dll");
            File.WriteAllBytes(path, BuildPlantedCoreLibrary());

            var resolver = new ProvenanceResolver(path, provenance);
            using var context = new MetadataContext(resolver)
            {
                CoreLibraryTrust = policy,
            };

            OpenedAssembly opened = Assert.IsType<OpenedAssembly>(
                context.Open(
                    resolver.Resolve(
                        new AssemblyReferenceIdentity(
                            "System.Runtime",
                            Version: null,
                            Culture: null,
                            PublicKeyToken: null),
                        AssemblyResolutionScope.Any)!));

            TypeRef decoded = TypeRefDecoder.Instance.GetTypeFromDefinition(
                opened.Reader,
                MetadataTokens.TypeDefinitionHandle(2),
                rawTypeKind: 0);

            Assert.Equal(
                expectedCorelib,
                decoded.Assembly == TypeRef.CoreLibrary);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    sealed class ProvenanceResolver(
        string path,
        AssemblyResolutionProvenance provenance) : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => string.Equals(
                identity.Name,
                "System.Runtime",
                StringComparison.OrdinalIgnoreCase)
                ? ResolvedAssemblyReference.CreateFromPath(path, provenance)
                : null;
    }

    /// <summary>
    /// An assembly named <c>System.Runtime</c> carrying the real ECMA public key
    /// blob, defining its own <c>System.Collections.IEnumerable</c> (row 2) and a
    /// class implementing it (row 3). The key is copied from the running core
    /// library precisely because it is public: no private key is involved and no
    /// signature is produced.
    /// </summary>
    static byte[] BuildPlantedCoreLibrary()
    {
        byte[] platformPublicKey = typeof(object).Assembly.GetName().GetPublicKey()!;

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("System.Runtime.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(platformPublicKey),
            AssemblyFlags.PublicKey,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var iface = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("System.Collections"),
            metadata.GetOrAddString("IEnumerable"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var fake = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Fake"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddInterfaceImplementation(fake, iface);

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
