using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// GuardedDecode routes every Decompiler signature decode of untrusted metadata through
// SignatureBlobGuard, so a malformed deeply-nested signature degrades to an unresolved shape
// instead of overflowing the native stack inside SRM. These tests craft over-deep signatures and
// assert the graceful fallback.
public class GuardedDecodeTests
{
    [Fact]
    public void DeepMethodSignature_DegradesToUnsupported()
    {
        // void M(int[][]...[]) with a 600-deep array parameter (over the 512 guard limit).
        var sig = new BlobBuilder();
        sig.WriteByte(0x00); // default calling convention
        sig.WriteByte(0x01); // 1 parameter
        sig.WriteByte(0x01); // return type VOID
        for (int i = 0; i < 600; i++)
            sig.WriteByte(0x1d); // SZARRAY
        sig.WriteByte(0x08);     // I4

        var (reader, handle) = BuildStandaloneSig(sig);
        var decoded = GuardedDecode.MethodSignature(reader, reader.GetStandaloneSignature(handle), GenericScope.Empty);

        Assert.Equal(TypeRefKind.Unsupported, decoded.ReturnType.Kind);
        Assert.Empty(decoded.ParameterTypes);
    }

    [Fact]
    public void DeepLocalSignature_DegradesToEmpty()
    {
        // LOCAL_SIG with one local that is a 600-deep array.
        var sig = new BlobBuilder();
        sig.WriteByte(0x07); // LOCAL_SIG
        sig.WriteByte(0x01); // 1 local
        for (int i = 0; i < 600; i++)
            sig.WriteByte(0x1d); // SZARRAY
        sig.WriteByte(0x08);     // I4

        var (reader, handle) = BuildStandaloneSig(sig);
        var locals = GuardedDecode.LocalTypes(reader, reader.GetStandaloneSignature(handle), GenericScope.Empty);

        Assert.Empty(locals);
    }

    static (MetadataReader Reader, StandaloneSignatureHandle Handle) BuildStandaloneSig(BlobBuilder sig)
    {
        var md = new MetadataBuilder();
        md.AddModule(0, md.GetOrAddString("m.dll"), md.GetOrAddGuid(Guid.NewGuid()), default, default);
        md.AddAssembly(md.GetOrAddString("m"), new Version(1, 0, 0, 0), default, default, default, default);
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var handle = md.AddStandaloneSignature(md.GetOrAddBlob(sig));
        var root = new MetadataRootBuilder(md, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return (MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray())).GetMetadataReader(), handle);
    }
}
