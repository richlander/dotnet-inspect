using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.CSharp.Tests;

public sealed class TypeShellProducerTests
{
    [Fact]
    public void RequiresAsyncBodyModifier_UsesDefiningMethodMetadata()
    {
        using var pe = new PEReader(File.OpenRead(typeof(TypeShellProducerTests).Assembly.Location));
        var reader = pe.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions
            .Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == nameof(TypeShellProducerTests));
        var type = reader.GetTypeDefinition(typeHandle);
        var methods = type.GetMethods()
            .ToDictionary(
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name),
                StringComparer.Ordinal);

        Assert.True(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(RuntimeAsyncFixture)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(AsyncIteratorFixture)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(IsUnsupportedSurfaceSignature_AllowsOrdinarySignatures)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(IteratorFixture)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            MetadataTokens.GetToken(typeHandle)));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            0x0600FFFF));
    }

    [Theory]
    [InlineData("delegate*<int, void>")]
    [InlineData("@delegate*<int, void>")]
    [InlineData("<>c__DisplayClass0_0")]
    [InlineData("(int, string){")]
    public void IsUnsupportedSurfaceSignature_FlagsUnrepresentableShapes(string signature)
    {
        Assert.True(TypeShellProducer.IsUnsupportedSurfaceSignature(signature));
    }

    [Theory]
    [InlineData("System.Int32")]
    [InlineData("Samples.Widget")]
    [InlineData("System.Collections.Generic.List<int>")]
    [InlineData("int[]")]
    public void IsUnsupportedSurfaceSignature_AllowsOrdinarySignatures(string signature)
    {
        Assert.False(TypeShellProducer.IsUnsupportedSurfaceSignature(signature));
    }

    [Fact]
    public void IsStaticType_DistinguishesStaticClassesFromOtherKinds()
    {
        using var pe = new PEReader(File.OpenRead(typeof(TypeShellProducerTests).Assembly.Location));
        var reader = pe.GetMetadataReader();

        TypeDefinition Type(string name) => reader.GetTypeDefinition(reader.TypeDefinitions
            .Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == name));

        Assert.True(TypeShellProducer.IsStaticType(Type(nameof(StaticFixture))));
        Assert.False(TypeShellProducer.IsStaticType(Type(nameof(InstanceFixture))));
        Assert.False(TypeShellProducer.IsStaticType(Type(nameof(IInterfaceFixture))));
    }

    [Fact]
    public void BuildPrintRequest_ComposesApiTypeFromSpecAndMetadata()
    {
        using var pe = new PEReader(File.OpenRead(typeof(TypeShellProducerTests).Assembly.Location));
        var reader = pe.GetMetadataReader();

        TypeDefinitionHandle Handle(string name) => reader.TypeDefinitions
            .Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == name);

        var member = new CSharpMemberPolicy(
            new ApiMember { Name = "Value", Kind = "field", ReturnType = "System.Int32" },
            CSharpBodyPolicy.Skeleton);
        // Nested spec deliberately claims Kind=Struct for a type whose metadata is a
        // static class, proving modifiers are read from metadata rather than the spec
        // kind, and that an object-family base is left implicit (null).
        var nested = new CSharpTypeShellSpec(
            Handle(nameof(StaticFixture)),
            Namespace: "Samples",
            MetadataName: "StaticFixture",
            Kind: CSharpTypeShellKind.Struct,
            InterfaceDisplayNames: [],
            MemberPolicies: [],
            PrimaryConstructorParameters: [],
            NestedTypes: []);
        var spec = new CSharpTypeShellSpec(
            Handle(nameof(DerivedFixture)),
            Namespace: "Samples",
            MetadataName: "DerivedFixture",
            Kind: CSharpTypeShellKind.Class,
            InterfaceDisplayNames: ["System.IDisposable"],
            MemberPolicies: [member],
            PrimaryConstructorParameters: [],
            NestedTypes: [nested]);

        var request = TypeShellProducer.BuildPrintRequest(reader, spec);

        // Spec-supplied facts flow straight through.
        Assert.Equal("Samples", request.Type.Namespace);
        Assert.Equal("DerivedFixture", request.Type.Name);
        Assert.Equal("DerivedFixture", request.Type.MetadataName);
        Assert.Equal("class", request.Type.Kind);
        Assert.Equal(["System.IDisposable"], request.Type.Interfaces);
        Assert.Same(member, Assert.Single(request.MemberPolicyOverrides));
        Assert.Equal("Value", Assert.Single(request.Type.Members).Name);
        Assert.Same(member.Member, request.Type.Members[0]);

        // The base type is reconstructed by the seam from the type's own metadata
        // (same-assembly non-generic class base), not carried on the spec.
        Assert.NotNull(request.Type.BaseType);
        Assert.EndsWith("BaseFixture", request.Type.BaseType);

        // Modifiers are read from the type's own metadata, not the spec kind.
        var nestedRequest = Assert.Single(request.NestedTypes);
        Assert.Equal("StaticFixture", nestedRequest.Type.Name);
        Assert.Equal("struct", nestedRequest.Type.Kind);
        Assert.True(nestedRequest.Type.IsStatic);
        Assert.True(nestedRequest.Type.IsAbstract);
        Assert.True(nestedRequest.Type.IsSealed);
        // A static class's object-family base is left implicit.
        Assert.Null(nestedRequest.Type.BaseType);
    }

    static async Task RuntimeAsyncFixture()
        => await Task.Yield();

    static async IAsyncEnumerable<int> AsyncIteratorFixture()
    {
        await Task.Yield();
        yield return 1;
    }

    static IEnumerable<int> IteratorFixture()
    {
        yield return 1;
    }

    static class StaticFixture
    {
    }

    class BaseFixture
    {
    }

    sealed class DerivedFixture : BaseFixture
    {
    }

    sealed class InstanceFixture
    {
    }

    interface IInterfaceFixture
    {
    }
}
