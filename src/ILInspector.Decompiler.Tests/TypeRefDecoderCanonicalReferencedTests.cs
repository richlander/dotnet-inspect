using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
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

/// <summary>
/// An <see cref="AssemblyDefinition"/>'s own simple name is equally forgeable
/// when the reader is not the originally-opened target: a cross-assembly
/// resolver can open an untrusted sibling file (e.g. a same-directory
/// <c>System.Runtime.dll</c> resolved for an unsigned reference,
/// <see cref="CrossAssemblyTypeResolver"/>) and decode types declared inside
/// it. <see cref="TypeRefDecoder.GetTypeFromDefinition"/> must grant
/// <see cref="TypeRef.CoreLibrary"/> identity for a type declared in that
/// reader only when the reader's own public key hashes to a trusted platform
/// token, never from its self-claimed <see cref="AssemblyDefinition"/> name
/// alone (issue #3045).
/// </summary>
public sealed class TypeRefDecoderCanonicalSelfTests
{
    // The well-known ECMA/"neutral" public key. Hashes (SHA-1, last 8 bytes,
    // reversed) to the trusted platform token b77a5c561934e089.
    static readonly byte[] EcmaPublicKey = [0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0];
    static readonly byte[] UntrustedPublicKey = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("System.Private.CoreLib")]
    [InlineData("mscorlib")]
    [InlineData("netstandard")]
    [InlineData("System.Runtime.Extensions")]
    public void ForgedSelfNamedAssembly_WithNoPublicKey_DoesNotGrantCoreLibraryIdentity(string facadeName)
    {
        var type = DecodeSelfDefinedType(facadeName, publicKey: null);
        Assert.Equal(facadeName, type.Assembly);
    }

    [Fact]
    public void ForgedSelfNamedAssembly_WithUntrustedPublicKey_DoesNotGrantCoreLibraryIdentity()
    {
        var type = DecodeSelfDefinedType("System.Runtime", UntrustedPublicKey);
        Assert.Equal("System.Runtime", type.Assembly);
    }

    [Theory]
    [InlineData("System.Runtime")]
    [InlineData("mscorlib")]
    public void GenuineSelfNamedAssembly_WithPlatformPublicKey_GrantsCoreLibraryIdentity(string facadeName)
    {
        var type = DecodeSelfDefinedType(facadeName, EcmaPublicKey);
        Assert.Equal(TypeRef.CoreLibrary, type.Assembly);
    }

    static TypeRef DecodeSelfDefinedType(string assemblyName, byte[]? publicKey)
    {
        var mb = new MetadataBuilder();
        mb.AddAssembly(
            mb.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey is null ? default : mb.GetOrAddBlob(publicKey),
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.None);
        mb.AddModule(
            generation: 0,
            mb.GetOrAddString(assemblyName + ".dll"),
            mb.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        mb.AddTypeDefinition(
            System.Reflection.TypeAttributes.Public,
            mb.GetOrAddString("System"),
            mb.GetOrAddString("Decimal"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var root = new MetadataRootBuilder(mb);
        var image = new BlobBuilder();
        root.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);

        using var provider = MetadataReaderProvider.FromMetadataImage(image.ToImmutableArray());
        var reader = provider.GetMetadataReader();
        var handle = reader.TypeDefinitions.Single(h => reader.GetString(reader.GetTypeDefinition(h).Name) == "Decimal");
        return TypeRefDecoder.Instance.GetTypeFromDefinition(reader, handle, rawTypeKind: 0);
    }
}
