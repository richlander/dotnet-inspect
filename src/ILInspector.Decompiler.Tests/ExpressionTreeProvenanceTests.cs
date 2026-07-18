using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Decompiler.Pipeline;
using Xunit;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Metadata-level guards for the platform-provenance check that anchors
/// expression-tree lambda recovery (issue #2864). These author exact metadata
/// handles no C# compiler emits: duplicate same-simple-name assembly references
/// reached through a <c>TypeSpecification</c> (modopt/array wrappers), and
/// corelib-like simple names on unsigned references. The check must rest on the
/// exact declaring-type handle's public-key token, never on a simple-name lookup
/// or a corelib name canonicalization.
/// </summary>
public sealed class ExpressionTreeProvenanceTests
{
    // ECMA / .NET Framework public key token (System.*), a trusted platform key.
    static readonly byte[] PlatformToken = [0xb7, 0x7a, 0x5c, 0x56, 0x19, 0x34, 0xe0, 0x89];

    enum Wrapper { SZArray, Pointer, Modopt }

    [Fact]
    public void GenuinePlatformTypeSpecification_IsTrusted()
    {
        var image = BuildImage(
            out var spec,
            Wrapper.SZArray,
            typeNamespace: "System.Linq.Expressions",
            typeName: "Expression",
            scopeName: "System.Linq.Expressions",
            scopeToken: PlatformToken,
            decoyName: null,
            decoyToken: null);

        Assert.True(IsTrusted(image, spec));
    }

    [Fact]
    public void GenuineCoreLibraryTypeSpecification_IsTrusted()
    {
        var image = BuildImage(
            out var spec,
            Wrapper.Pointer,
            typeNamespace: "System",
            typeName: "DateTime",
            scopeName: "mscorlib",
            scopeToken: PlatformToken,
            decoyName: null,
            decoyToken: null);

        Assert.True(IsTrusted(image, spec));
    }

    [Fact]
    public void GenuinePlatform_ViaModoptTypeSpecification_IsTrusted()
    {
        // Proves the modopt wrapper strips to the exact base handle (so the modopt
        // decline below rests on the unsigned scope, not on a decode that lost the type).
        var image = BuildImage(
            out var spec,
            Wrapper.Modopt,
            typeNamespace: "System.Linq.Expressions",
            typeName: "Expression",
            scopeName: "System.Linq.Expressions",
            scopeToken: PlatformToken,
            decoyName: null,
            decoyToken: null);

        Assert.True(IsTrusted(image, spec));
    }

    [Fact]
    public void DuplicateSimpleName_SpoofScopeViaTypeSpecification_Declines()
    {
        // A signed platform reference and an unsigned lookalike share the simple name
        // "System.Linq.Expressions". The type binds to the UNSIGNED reference through a
        // modopt-wrapped TypeSpecification. A simple-name token lookup would find the
        // signed decoy (added first) and wrongly bless it; the exact-handle check must
        // read the unsigned scope's (missing) token and decline.
        var image = BuildImage(
            out var spec,
            Wrapper.Modopt,
            typeNamespace: "System.Linq.Expressions",
            typeName: "Expression",
            scopeName: "System.Linq.Expressions",
            scopeToken: null,                    // unsigned real scope of the type
            decoyName: "System.Linq.Expressions", // signed decoy, same simple name, added first
            decoyToken: PlatformToken);

        Assert.False(IsTrusted(image, spec));
    }

    [Fact]
    public void CoreLibrarySimpleNameSpoof_ViaTypeSpecification_Declines()
    {
        // An unsigned reference literally named "mscorlib". A canonicalization that maps
        // the simple name to the core library would bypass the token check; the exact
        // handle carries no platform token, so the check must decline.
        var image = BuildImage(
            out var spec,
            Wrapper.SZArray,
            typeNamespace: "System",
            typeName: "DateTime",
            scopeName: "mscorlib",
            scopeToken: null,
            decoyName: null,
            decoyToken: null);

        Assert.False(IsTrusted(image, spec));
    }

    [Fact]
    public void UnsignedLookalike_ViaArrayTypeSpecification_Declines()
    {
        var image = BuildImage(
            out var spec,
            Wrapper.SZArray,
            typeNamespace: "System.Linq.Expressions",
            typeName: "Expression",
            scopeName: "System.Linq.Expressions",
            scopeToken: null,
            decoyName: null,
            decoyToken: null);

        Assert.False(IsTrusted(image, spec));
    }

    static bool IsTrusted(ImmutableArray<byte> image, TypeSpecificationHandle spec)
    {
        using var provider = MetadataReaderProvider.FromMetadataImage(image);
        var reader = provider.GetMetadataReader();
        return IrImporter.IsTrustedPlatformMemberReference(reader, spec);
    }

    static ImmutableArray<byte> BuildImage(
        out TypeSpecificationHandle spec,
        Wrapper wrapper,
        string typeNamespace,
        string typeName,
        string scopeName,
        byte[]? scopeToken,
        string? decoyName,
        byte[]? decoyToken)
    {
        var mb = new MetadataBuilder();
        mb.AddModule(
            generation: 0,
            mb.GetOrAddString("provenance.dll"),
            mb.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);

        // The decoy is added FIRST so a first-match simple-name lookup would pick it.
        if (decoyName is not null)
            AddAssemblyReference(mb, decoyName, decoyToken);

        var scope = AddAssemblyReference(mb, scopeName, scopeToken);

        var typeRef = mb.AddTypeReference(
            scope,
            mb.GetOrAddString(typeNamespace),
            mb.GetOrAddString(typeName));

        var sig = new BlobBuilder();
        var encoder = new BlobEncoder(sig).TypeSpecificationSignature();
        switch (wrapper)
        {
            case Wrapper.SZArray:
                encoder.SZArray().Type(typeRef, isValueType: false);
                break;
            case Wrapper.Pointer:
                encoder.Pointer().Type(typeRef, isValueType: false);
                break;
            case Wrapper.Modopt:
                encoder.CustomModifiers().AddModifier(typeRef, isOptional: true);
                encoder.Type(typeRef, isValueType: false);
                break;
        }

        spec = mb.AddTypeSpecification(mb.GetOrAddBlob(sig));

        var root = new MetadataRootBuilder(mb);
        var image = new BlobBuilder();
        root.Serialize(image, methodBodyStreamRva: 0, mappedFieldDataStreamRva: 0);
        return image.ToImmutableArray();
    }

    static AssemblyReferenceHandle AddAssemblyReference(MetadataBuilder mb, string name, byte[]? token)
        => mb.AddAssemblyReference(
            mb.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            culture: default,
            token is null ? default : mb.GetOrAddBlob(token),
            flags: 0,
            hashValue: default);
}
