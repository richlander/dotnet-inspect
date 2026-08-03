using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

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

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            peReader.GetMetadataReader());
    }
}
