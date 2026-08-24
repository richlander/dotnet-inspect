using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public sealed class DescriptorContentIdentityTests
{
    [Fact]
    public void DescriptorBackedOpen_RejectsDifferentAssemblyIdentity()
    {
        ResolvedAssemblyReference descriptor = MismatchedAssemblyDescriptor();

        BadImageFormatException exception =
            Assert.Throws<BadImageFormatException>(
                () => MetadataSource.Open(
                    descriptor,
                    externalPdbPath: null,
                    TestAssemblyReferenceResolvers.None));

        Assert.Contains("identity", exception.Message);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("OpenMetadataOnly")]
    [InlineData("OpenEmbeddedPdbOnly")]
    [InlineData("OpenPrefetched")]
    public void PdbContextDescriptorRoutes_RejectDifferentAssemblyIdentity(
        string route)
    {
        ResolvedAssemblyReference descriptor = MismatchedAssemblyDescriptor();

        BadImageFormatException exception =
            Assert.Throws<BadImageFormatException>(() =>
            {
                using PdbContext context = route switch
                {
                    "Open" => PdbContext.Open(descriptor),
                    "OpenMetadataOnly" =>
                        PdbContext.OpenMetadataOnly(descriptor),
                    "OpenEmbeddedPdbOnly" =>
                        PdbContext.OpenEmbeddedPdbOnly(
                            descriptor,
                            maxEmbeddedPdbBytes: 1024),
                    "OpenPrefetched" =>
                        PdbContext.OpenPrefetched(descriptor),
                    _ => throw new InvalidOperationException(route),
                };
            });

        Assert.Contains("identity", exception.Message);
    }

    [Fact]
    public void MetadataContextDescriptorOpen_RejectsBeforeGrantingTrust()
    {
        ResolvedAssemblyReference descriptor = MismatchedAssemblyDescriptor();
        using var context =
            new MetadataContext(TestAssemblyReferenceResolvers.None);

        BadImageFormatException exception =
            Assert.Throws<BadImageFormatException>(
                () => context.Open(descriptor));

        Assert.Contains("identity", exception.Message);
    }

    [Fact]
    public void AssemblyImageDescriptorOpen_RejectsDifferentAssemblyIdentity()
    {
        ResolvedAssemblyReference descriptor = MismatchedAssemblyDescriptor();

        BadImageFormatException exception =
            Assert.Throws<BadImageFormatException>(
                () => AssemblyImage.Open(descriptor));

        Assert.Contains("identity", exception.Message);
    }

    [Fact]
    public void DescriptorBackedOpen_RejectsChangedNetmoduleMvid()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"descriptor-content-{Guid.NewGuid():N}.netmodule");
        File.WriteAllBytes(path, BuildModuleImage(Guid.NewGuid()));
        try
        {
            ResolvedAssemblyReference descriptor =
                Assert.IsType<ResolvedAssemblyReference>(
                    ResolvedAssemblyReference.CreateFromModulePathIfManaged(
                        path,
                        AssemblyResolutionProvenance.Local("test")));
            File.WriteAllBytes(path, BuildModuleImage(Guid.NewGuid()));

            BadImageFormatException exception =
                Assert.Throws<BadImageFormatException>(
                    () => MetadataSource.Open(
                        descriptor,
                        externalPdbPath: null,
                        TestAssemblyReferenceResolvers.None));

            Assert.Contains("MVID", exception.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static byte[] BuildModuleImage(Guid mvid)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString("input.netmodule"),
            mvid: metadata.GetOrAddGuid(mvid),
            encId: default,
            encBaseId: default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static ResolvedAssemblyReference MismatchedAssemblyDescriptor()
    {
        ResolvedAssemblyReference acquired =
            ResolvedAssemblyReference.CreateFromPath(
                typeof(DescriptorContentIdentityTests).Assembly.Location,
                AssemblyResolutionProvenance.Local("test"));
        return ResolvedAssemblyReference.Create(
            acquired.Identity with { Name = "Different" },
            path: null,
            acquired.OpenRead,
            acquired.Provenance);
    }
}
