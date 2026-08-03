using System.Reflection.Metadata;
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
}
