using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

// GuardedSignatureText routes the compile-back / type-source composers' string-producing signature
// decodes through SignatureBlobGuard, so a malformed deeply-nested signature degrades to a
// placeholder type instead of overflowing the native stack inside SRM.
public class GuardedSignatureTextTests
{
    [Fact]
    public void DeepFieldSignature_DegradesToPlaceholder()
    {
        // FIELD header then a 600-deep array (over the 512 guard limit).
        var sig = new BlobBuilder();
        sig.WriteByte(0x06); // FIELD
        for (int i = 0; i < 600; i++)
            sig.WriteByte(0x1d); // SZARRAY
        sig.WriteByte(0x08);     // I4

        var (reader, handle) = BuildField(sig);
        var text = GuardedSignatureText.FieldText(reader, reader.GetFieldDefinition(handle), context: null);

        Assert.Equal("object", text);
    }

    static (MetadataReader Reader, FieldDefinitionHandle Handle) BuildField(BlobBuilder sig)
    {
        var md = new MetadataBuilder();
        md.AddModule(0, md.GetOrAddString("m.dll"), md.GetOrAddGuid(Guid.NewGuid()), default, default);
        md.AddAssembly(md.GetOrAddString("m"), new Version(1, 0, 0, 0), default, default, default, default);
        var field = md.AddFieldDefinition(System.Reflection.FieldAttributes.Public, md.GetOrAddString("f"), md.GetOrAddBlob(sig));
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default,
            MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        var root = new MetadataRootBuilder(md, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        var reader = MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(image.ToArray())).GetMetadataReader();
        return (reader, field);
    }
}
