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
        Assert.Equal(
            "ILInspector.CSharp.Tests.TypeShellProducerTests.BaseFixture",
            request.Type.BaseType);

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

    [Fact]
    public void MemberShellProducer_ComposesInitPropertyPolicy()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Value",
            Kind: CSharpShellMemberKind.PropertyGet,
            IsStatic: false,
            Parameters: [],
            ReturnType: "int",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.TargetGetterWithInitSetter,
            Body: "return _value;",
            ReturnAttributes: ["return: System.Diagnostics.CodeAnalysis.NotNull"],
            GetterToken: 0x06000001,
            SetterToken: 0x06000002));

        Assert.Equal(CSharpBodyPolicy.Full, policy.BodyPolicy);
        Assert.Equal("property", policy.Member.Kind);
        Assert.Equal(0x06000001, policy.Member.GetterToken);
        Assert.Equal(0x06000002, policy.Member.SetterToken);
        Assert.Collection(
            policy.Member.SignatureModel!.Accessors,
            getter =>
            {
                Assert.Equal("get", getter.Kind);
                Assert.Equal(
                    ["return: System.Diagnostics.CodeAnalysis.NotNull"],
                    getter.ReturnAttributes);
            },
            setter => Assert.Equal("init", setter.Kind));

        var body = Assert.IsType<CSharpPropertyBody>(policy.Body);
        Assert.Equal(
            CSharpAccessorBody.Block("return _value;") with { IsReplacementTarget = true },
            body.Getter);
        Assert.Equal(CSharpAccessorBody.Throw, body.Setter);
    }

    [Fact]
    public void MemberShellProducer_ComposesExplicitInterfaceEventWithSiblingBody()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Changed",
            Kind: CSharpShellMemberKind.EventAdd,
            IsStatic: false,
            Parameters: [],
            ReturnType: "System.Action",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.TargetEventAccessorWithSibling,
            Body: "_changed += value;",
            ExplicitInterfaceMemberName: "IEvents.Changed",
            SiblingBody: "_changed -= value;",
            AdderToken: 0x06000003,
            RemoverToken: 0x06000004));

        Assert.Equal("explicit-interface-implementation", policy.Member.Kind);
        Assert.Equal("IEvents.Changed", policy.Member.Name);
        Assert.Collection(
            policy.Member.SignatureModel!.Accessors,
            adder => Assert.Equal("add", adder.Kind),
            remover => Assert.Equal("remove", remover.Kind));

        var body = Assert.IsType<CSharpEventBody>(policy.Body);
        Assert.Equal(
            CSharpAccessorBody.Block("_changed += value;") with { IsReplacementTarget = true },
            body.Adder);
        Assert.Equal(CSharpAccessorBody.Block("_changed -= value;"), body.Remover);
    }

    [Fact]
    public void MemberShellProducer_MarksTargetMethodBodyForReplacement()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Run",
            Kind: CSharpShellMemberKind.Method,
            IsStatic: false,
            Parameters: [],
            ReturnType: "void",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.TargetBody,
            Body: "return;"));

        var body = Assert.IsType<CSharpBlockBody>(policy.Body);
        Assert.True(body.IsReplacementTarget);
    }

    [Fact]
    public void MemberShellProducer_ComposesPrimaryConstructorStubInitializer()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(
            new CSharpMemberShellSpec(
                Name: ".ctor",
                Kind: CSharpShellMemberKind.Constructor,
                IsStatic: false,
                Parameters: [],
                ReturnType: null,
                TypeParameters: [],
                BodyKind: CSharpShellBodyKind.Throw,
                Body: null),
            primaryConstructorParameterCount: 2);

        Assert.Equal(CSharpBodyPolicy.Stub, policy.BodyPolicy);
        var body = Assert.IsType<CSharpBlockBody>(policy.Body);
        Assert.Equal("throw null;", body.Source);
        Assert.NotNull(body.ConstructorInitializer);
        Assert.Equal(CSharpConstructorInitializerKind.This, body.ConstructorInitializer.Kind);
        Assert.Equal(["default", "default"], body.ConstructorInitializer.Arguments);
    }

    [Theory]
    [InlineData("ref int", null, "int", "ref")]
    [InlineData("ref int", "out", "int", "out")]
    [InlineData("string", null, "string", null)]
    public void MemberShellProducer_NormalizesParameterModifier(
        string type,
        string? modifier,
        string expectedType,
        string? expectedModifier)
    {
        var parameter = CSharpMemberShellProducer.BuildParameter(
            new CSharpShellParameter("value", type, modifier));

        Assert.Equal(expectedType, parameter.Type);
        Assert.Equal(expectedModifier, parameter.Modifier);
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
