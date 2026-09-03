extern alias legacyunsafe;

using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

using NewAccessorFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.AccessorContractFixtures;
using LegacyFixtures = legacyunsafe::ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
using NewFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The API surface <c>unsafe</c> modifier under the updated memory-safety rules.
/// A member declared <c>unsafe</c>/<c>extern</c> is stamped with
/// <c>RequiresUnsafeAttribute</c> even when no pointer appears in its signature,
/// so the extractor must treat that attribute — not just a textual <c>*</c> in
/// the signature — as evidence of an unsafe member. The fixtures live in the
/// new-rules assembly (compiled with <c>updated-memory-safety-rules</c>).
/// </summary>
public sealed class ApiSurfaceUnsafeTests
{
    private static ApiType Type(Type type)
    {
        using var stream = File.OpenRead(type.Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        return surface.Types.First(t => t.FullName == type.FullName);
    }

    private static ApiMember Method(string name)
    {
        // ApiSurfaceExtractor materializes the surface into lists, so the reader
        // can be disposed once Extract returns. typeof(...).Name is the simple
        // type name ("UnsafeFixtures"); nameof on the using-alias would yield the
        // alias identifier instead.
        return Type(typeof(NewFixtures)).Members.First(m => m.Name == name && m.Kind == "method");
    }

    [Fact]
    public void RequiresUnsafeMember_WithoutPointerInSignature_IsUnsafe()
    {
        // `public static unsafe int Risky() => 42;` — declared unsafe, no pointer
        // in the signature, so the only evidence is RequiresUnsafeAttribute.
        ApiMember method = Method(nameof(NewFixtures.Risky));

        Assert.True(method.IsUnsafe);
        Assert.DoesNotContain(
            method.Attributes,
            attribute => attribute.Contains("RequiresUnsafeAttribute", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdatedPointerSignatureMember_WithoutContract_IsNotUnsafe()
    {
        Assert.False(Method(nameof(NewFixtures.FreePointer)).IsUnsafe);
    }

    [Fact]
    public void UpdatedAccessorSpecificContract_MakesPropertyUnsafe()
    {
        using var stream = File.OpenRead(
            typeof(NewAccessorFixtures).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(
            handle => reader.GetString(
                reader.GetTypeDefinition(handle).Name)
                == typeof(NewAccessorFixtures).Name);
        PropertyDefinitionHandle propertyHandle =
            reader.GetTypeDefinition(typeHandle).GetProperties().Single();
        PropertyAccessors accessors =
            reader.GetPropertyDefinition(propertyHandle).GetAccessors();
        var index = MemorySafetyMetadataIndex.Create(reader);

        Assert.IsType<MemorySafetyMemberContractResult.None>(
            index.GetMemberContract(propertyHandle));
        Assert.IsType<MemorySafetyMemberContractResult.Explicit>(
            index.GetMemberContract(accessors.Getter));

        ApiMember property = Type(typeof(NewAccessorFixtures)).Members.Single(
            member => member.Name == nameof(NewAccessorFixtures.Property));
        Assert.True(property.IsUnsafe);
    }

    [Fact]
    public void UnsupportedRules_KeepCompatibilityWithoutPromotingDirectAttribute()
    {
        byte[] image = File.ReadAllBytes(
            typeof(NewFixtures).Assembly.Location);
        ChangeMemorySafetyRulesVersion(
            image,
            originalVersion: 2,
            replacementVersion: 99);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));

        ApiSurface surface =
            ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType type = surface.Types.Single(
            candidate => candidate.FullName == typeof(NewFixtures).FullName);

        Assert.False(type.Members.Single(
            member => member.Name == nameof(NewFixtures.Risky)
                && member.Kind == "method").IsUnsafe);
        Assert.True(type.Members.Single(
            member => member.Name == nameof(NewFixtures.FreePointer)
                && member.Kind == "method").IsUnsafe);
        Assert.Contains(
            surface.InspectionFailures,
            failure => failure.Operation
                    == ApiSurfaceInspectionFailure.MemorySafetyContractOperation
                && failure.Kind == nameof(MemorySafetyRulesState.Unsupported));
    }

    [Fact]
    public void LegacyPointerSignatureMember_HasImplicitUnsafeContract()
    {
        ApiMember method = Type(typeof(LegacyFixtures)).Members.First(
            member => member.Name == nameof(LegacyFixtures.FreePointer)
                && member.Kind == "method");

        Assert.True(method.IsUnsafe);
    }

    [Fact]
    public void FunctionPointerSignature_PreservesDelegatePointerShape()
    {
        Assert.Equal(
            "int InvokeFunctionPointer(delegate*<int, int> callback, int x)",
            Method(nameof(NewFixtures.InvokeFunctionPointer)).Signature);
    }

    [Fact]
    public void FunctionPointerSignature_PreservesDistinctFieldShapes()
    {
        var members = Type(typeof(FunctionPointerShapeFixture)).Members;

        Assert.Equal(
            "delegate*<int, int>",
            members.Single(m => m.Name == nameof(FunctionPointerShapeFixture.Field)).ReturnType);
        Assert.Equal(
            "delegate*<string, bool>",
            members.Single(m => m.Name == nameof(FunctionPointerShapeFixture.Other)).ReturnType);
        Assert.Equal(
            "delegate* unmanaged[Cdecl]<int, void>",
            members.Single(m => m.Name == nameof(FunctionPointerShapeFixture.Unmanaged)).ReturnType);
        Assert.Equal(
            "delegate*<int, int>[]",
            members.Single(m => m.Name == nameof(FunctionPointerShapeFixture.ArrayOf)).ReturnType);
        Assert.Equal(
            "delegate*<int, int> Ret(delegate*<int, int> f)",
            members.Single(m => m.Name == nameof(FunctionPointerShapeFixture.Ret)).Signature);
    }

    [Fact]
    public void FunctionPointerSignature_AppliesNullabilityInMetadataOrder()
    {
        var members = Type(typeof(FunctionPointerNullabilityFixture)).Members;

        Assert.Equal(
            "delegate*<string, string?>",
            members.Single(m => m.Name == nameof(FunctionPointerNullabilityFixture.ReturnsNullable)).ReturnType);
        Assert.Equal(
            "delegate*<string?, string>",
            members.Single(m => m.Name == nameof(FunctionPointerNullabilityFixture.ParameterNullable)).ReturnType);
    }

    [Fact]
    public void SafeMember_IsNotUnsafe()
    {
        // No pointer and not declared unsafe at the member level.
        Assert.False(Method(nameof(NewFixtures.StackAllocDefault)).IsUnsafe);
    }

    static void ChangeMemorySafetyRulesVersion(
        byte[] image,
        int originalVersion,
        int replacementVersion)
    {
        byte[] original =
            [1, 0, (byte)originalVersion, 0, 0, 0, 0, 0];
        int offset = image.AsSpan().IndexOf(original);
        Assert.True(offset >= 0, "MemorySafetyRulesAttribute blob not found.");
        Assert.Equal(
            -1,
            image.AsSpan(offset + original.Length).IndexOf(original));
        image[offset + 2] = (byte)replacementVersion;
    }
}

public unsafe class FunctionPointerNullabilityFixture
{
    public delegate*<string, string?> ReturnsNullable;
    public delegate*<string?, string> ParameterNullable;
}

public unsafe class FunctionPointerShapeFixture
{
    public delegate*<int, int> Field;
    public delegate*<string, bool> Other;
    public delegate* unmanaged[Cdecl]<int, void> Unmanaged;
    public delegate*<int, int>[] ArrayOf = [];

    public delegate*<int, int> Ret(delegate*<int, int> f) => f;
}
