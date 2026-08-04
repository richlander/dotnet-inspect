using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Findings;

namespace ILInspector.Metadata.Tests;

public class PdbContextDescriptorTests
{
    [Fact]
    public void OpenDescriptor_UsesAuthoritativeStreamInsteadOfPath()
    {
        string authoritativePath = typeof(PdbContextDescriptorTests).Assembly.Location;
        string informationalPath = typeof(PdbContext).Assembly.Location;
        byte[] authoritativeImage = File.ReadAllBytes(authoritativePath);
        AssemblyReferenceIdentity identity = ReadIdentity(authoritativeImage);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            informationalPath,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        using var context = PdbContext.Open(descriptor);

        Assert.Equal(identity.Name, context.ExtractAssemblyInfo().AssemblyName);
        Assert.Equal(informationalPath, context.AssemblyPathOrNull);
    }

    [Fact]
    public void OpenDescriptor_StreamOnlyImageRemainsUsable()
    {
        byte[] image = File.ReadAllBytes(
            typeof(PdbContextDescriptorTests).Assembly.Location);
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        using var context = PdbContext.Open(descriptor);

        Assert.Equal(identity.Name, context.ExtractAssemblyInfo().AssemblyName);
        Assert.Null(context.AssemblyPathOrNull);
        Assert.Throws<InvalidOperationException>(() => context.AssemblyPath);
    }

    [Fact]
    public void DeclarationInventory_UsesAuthoritativeDescriptorStream()
    {
        string authoritativePath =
            typeof(PdbContextDescriptorTests).Assembly.Location;
        string informationalPath = typeof(PdbContext).Assembly.Location;
        byte[] authoritativeImage = File.ReadAllBytes(authoritativePath);
        AssemblyReferenceIdentity identity = ReadIdentity(authoritativeImage);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            informationalPath,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        var read = Assert.IsType<
            AssemblyTypeDeclarationInventoryOutcome.Read>(
                AssemblyTypeDeclarationInventoryReader.Read(descriptor));

        Assert.Equal(identity, read.Inventory.Identity);
        Assert.Contains(
            read.Inventory.Definitions,
            name => name.ToMetadataFullName()
                == typeof(PdbContextDescriptorTests).FullName);
    }

    [Fact]
    public void DeclarationInventory_RejectsDescriptorIdentityMismatch()
    {
        byte[] authoritativeImage = File.ReadAllBytes(
            typeof(PdbContextDescriptorTests).Assembly.Location);
        AssemblyReferenceIdentity wrongIdentity =
            ReadIdentity(File.ReadAllBytes(typeof(PdbContext).Assembly.Location));
        var descriptor = ResolvedAssemblyReference.Create(
            wrongIdentity,
            path: null,
            () => new MemoryStream(authoritativeImage, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        var rejected = Assert.IsType<
            AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                AssemblyTypeDeclarationInventoryReader.Read(descriptor));

        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    [Fact]
    public void DeclarationInventory_IncludesNestedForwardedTypes()
    {
        byte[] image = BuildNestedForwarderAssembly();
        AssemblyReferenceIdentity identity = ReadIdentity(image);
        var descriptor = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(image, writable: false),
            AssemblyResolutionProvenance.Local("test"));

        var read = Assert.IsType<
            AssemblyTypeDeclarationInventoryOutcome.Read>(
                AssemblyTypeDeclarationInventoryReader.Read(descriptor));

        Assert.Contains(
            read.Inventory.Forwarders,
            name => name.Namespace == "N"
                && name.Segments.SequenceEqual(["Outer", "Inner"]));
    }

    [Fact]
    public void SurfaceClassification_ProjectsSuccessAndFailureToFindings()
    {
        string path = typeof(PdbContextDescriptorTests).Assembly.Location;
        AssemblySurfaceClassificationOutcome classified =
            AssemblySurfaceClassifier.Classify(
                path,
                AssemblyResolutionProvenance.Local("test"));
        var subject = new FindingSubject(path, Path.GetFileName(path));

        var complete = Assert.IsType<
            FindingInspection<AssemblySurfaceClassification>.Complete>(
                MetadataFindings
                    .InspectAssemblySurface(classified, subject).Value);
        Assert.Single(complete.Findings);
        Assert.Equal(
            AssemblySurfaceKind.Implementation,
            complete.Findings[0].Payload.Kind);

        var rejected = new AssemblySurfaceClassificationOutcome.Rejected(
            new CandidateOpenFailure(
                CandidateOpenFailureKind.InvalidImage,
                "Invalid metadata."));
        Assert.IsType<
            FindingInspection<AssemblySurfaceClassification>.Failed>(
                MetadataFindings
                    .InspectAssemblySurface(rejected, subject).Value);
    }

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }

    static byte[] BuildNestedForwarderAssembly()
    {
        const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("NestedForwarder.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("NestedForwarder"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle target = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Target"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        ExportedTypeHandle outer = metadata.AddExportedType(
            TypeAttributes.Public | Forwarder,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Outer"),
            target,
            typeDefinitionId: 0);
        metadata.AddExportedType(
            TypeAttributes.NestedPublic,
            default,
            metadata.GetOrAddString("Inner"),
            outer,
            typeDefinitionId: 0);

        var builder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        builder.Serialize(image);
        return image.ToArray();
    }
}
