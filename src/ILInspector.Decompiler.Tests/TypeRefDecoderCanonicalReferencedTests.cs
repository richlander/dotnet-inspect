using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// A referenced assembly's simple name is forgeable: a planted <c>AssemblyRef</c>
/// row can be named <c>"System.Runtime"</c>/<c>"mscorlib"</c>/etc. with no valid
/// public-key token. <see cref="TypeRefDecoder.GetTypeFromReference"/> must grant
/// <see cref="TypeRef.CoreLibrary"/> identity only when that reference's token is
/// a trusted platform key (<c>PlatformKeys.IsPlatform</c>), never from the name
/// alone (issue #3045).
/// </summary>
public sealed class TypeRefDecoderCanonicalReferencedTests
{
    // ECMA / .NET Framework public key token (System.*), a trusted platform key.
    static readonly byte[] PlatformToken = [0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89];
    static readonly byte[] ForgedToken = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88];

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("System.Runtime.Extensions")]
    public void ForgedCoreLibFacadeName_WithNoToken_DoesNotGrantCoreLibraryIdentity(string facadeName)
    {
        var type = DecodeTypeReference(facadeName, token: null);
        Assert.NotEqual(TypeRef.CoreLibrary, type.Assembly);
        Assert.Equal(facadeName, type.Assembly);
    }

    [Fact]
    public void ForgedCoreLibFacadeName_WithUntrustedToken_DoesNotGrantCoreLibraryIdentity()
    {
        var type = DecodeTypeReference("System.Runtime", ForgedToken);
        Assert.NotEqual(TypeRef.CoreLibrary, type.Assembly);
        Assert.Equal("System.Runtime", type.Assembly);
    }

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("System.Runtime.Extensions")]
    public void GenuineCoreLibFacadeName_WithPlatformToken_GrantsCoreLibraryIdentity(string facadeName)
    {
        var type = DecodeTypeReference(facadeName, PlatformToken);
        Assert.Equal(TypeRef.CoreLibrary, type.Assembly);
    }

    [Fact]
    public void NonFacadeName_IsNeverCanonicalizedRegardlessOfToken()
    {
        var type = DecodeTypeReference("Newtonsoft.Json", PlatformToken);
        Assert.Equal("Newtonsoft.Json", type.Assembly);
    }

    static TypeRef DecodeTypeReference(string assemblyName, byte[]? token)
    {
        var mb = new MetadataBuilder();
        mb.AddModule(
            generation: 0,
            mb.GetOrAddString("forged.dll"),
            mb.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        var scope = mb.AddAssemblyReference(
            mb.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            token is null ? default : mb.GetOrAddBlob(token),
            flags: 0,
            hashValue: default);

        var typeRef = mb.AddTypeReference(
            scope,
            mb.GetOrAddString("System"),
            mb.GetOrAddString("Decimal"));

        var root = new MetadataRootBuilder(mb);
        var image = new BlobBuilder();
        root.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);

        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var reader = provider.GetMetadataReader();
        return TypeRefDecoder.Instance.GetTypeFromReference(reader, typeRef, rawTypeKind: 0);
    }
}
