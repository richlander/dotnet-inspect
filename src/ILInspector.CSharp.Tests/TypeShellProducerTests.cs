using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using CSharpText;
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
        Assert.Equal(
            [
                nameof(TypeShellProducerTests),
                nameof(DerivedFixture),
            ],
            request.Type.DefinitionName?.Segments);
        Assert.Equal([0, 0], request.Type.IntroducedTypeParameterCounts);
        Assert.Equal(
            nameof(DerivedFixture),
            CSharpFormatter.FormatTypeName(request.Type));
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
        Assert.Equal(
            [
                nameof(TypeShellProducerTests),
                nameof(StaticFixture),
            ],
            nestedRequest.Type.DefinitionName?.Segments);
        Assert.Equal([0, 0], nestedRequest.Type.IntroducedTypeParameterCounts);
        Assert.Equal(
            nameof(StaticFixture),
            CSharpFormatter.FormatTypeName(nestedRequest.Type));
        Assert.Equal("struct", nestedRequest.Type.Kind);
        Assert.True(nestedRequest.Type.IsStatic);
        Assert.True(nestedRequest.Type.IsAbstract);
        Assert.True(nestedRequest.Type.IsSealed);
        // A static class's object-family base is left implicit.
        Assert.Null(nestedRequest.Type.BaseType);
    }

    [Theory]
    [InlineData("A+B")]
    [InlineData("A<B")]
    [InlineData(" ")]
    public void HostileMetadataSelfNameIsNotRendered(string metadataName)
    {
        byte[] image = BuildHostileMetadataFixture(metadataName);
        using var stream = new MemoryStream(image);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle hostile = MetadataTokens.TypeDefinitionHandle(2);
        var spec = new CSharpTypeShellSpec(
            hostile,
            Namespace: "N",
            MetadataName: reader.GetString(reader.GetTypeDefinition(hostile).Name),
            Kind: CSharpTypeShellKind.Class,
            InterfaceDisplayNames: [],
            MemberPolicies: [],
            PrimaryConstructorParameters: [],
            NestedTypes: []);

        CSharpTypePrintRequest request = TypeShellProducer.BuildPrintRequest(reader, spec);
        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(request));
        CSharpDeclaredTypeSelfNameFailure failure = Assert.Single(outcome.SelfNameFailures);
        var refusal = Assert.IsType<
            CSharpDeclaredTypeSelfNameFailureReason.IdentifierNotAdmitted>(
                failure.Reason);

        Assert.Equal([metadataName], failure.Identity.Segments);
        Assert.Equal(
            CSharpTypeDeclarationIdentifierRefusalReason.InvalidIdentifier,
            refusal.Reason);
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
            SiblingBody: "int* p = stackalloc int[1]; _changed -= value;",
            AdderToken: 0x06000003,
            RemoverToken: 0x06000004));

        Assert.True(policy.Member.IsUnsafe);
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
        Assert.Equal(
            CSharpAccessorBody.Block("int* p = stackalloc int[1]; _changed -= value;"),
            body.Remover);
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
    public void MemberShellProducer_DoesNotTreatNonSzArrayMarkerAsUnsafe()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(
            new CSharpMemberShellSpec(
                Name: "Run",
                Kind: CSharpShellMemberKind.Method,
                IsStatic: false,
                Parameters: [new CSharpShellParameter("value", "int[*]")],
                ReturnType: "void",
                TypeParameters: [],
                BodyKind: CSharpShellBodyKind.TargetBody,
                Body: "return;"));

        Assert.False(policy.Member.IsUnsafe);
    }

    [Fact]
    public void MemberShellProducer_ComposesExplicitInterfaceMethodDeclaration()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Run",
            Kind: CSharpShellMemberKind.Method,
            IsStatic: false,
            Parameters: [new CSharpShellParameter("value", "ref int")],
            ReturnType: "T?",
            TypeParameters:
            [
                new CSharpShellTypeParameter(
                    "T",
                    ["class"],
                    [new TypeParameterConstraint("class", IsTypeName: false)],
                    TypeParameterTypeKind.ReferenceType)
            ],
            BodyKind: CSharpShellBodyKind.TargetBody,
            Body: "return default;",
            ExplicitInterfaceMemberName: "Samples.IRunner.Run",
            DeclarationSignature: "this harness text must not be used"));

        Assert.Equal(
            "T? Samples.IRunner.Run<T>(ref int value)",
            policy.Member.Signature);

        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Runner",
            Kind = "class",
            Interfaces = ["Samples.IRunner"],
            Members = [policy.Member],
        };
        var result = Assert.IsType<CSharpTypePrintOutcome.Printed>(
            new CSharpTypePrinter().Print(new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides: [policy]))).Result;

        Assert.Contains(
            "T? Samples.IRunner.Run<T>(ref int value)",
            Assert.Single(result.Units).Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "where T : class",
            Assert.Single(result.Units).Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MemberShellProducer_PrintsExplicitInterfaceInitProperty()
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Value",
            Kind: CSharpShellMemberKind.PropertyGet,
            IsStatic: false,
            Parameters: [],
            ReturnType: "int",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.TargetGetterWithInitSetter,
            Body: "return 1;",
            ExplicitInterfaceMemberName: "IValue.Value"));

        var type = new ApiType
        {
            Name = "Holder",
            Kind = "class",
            Interfaces = ["IValue"],
            Members = [policy.Member],
        };
        var result = Assert.IsType<CSharpTypePrintOutcome.Printed>(
            new CSharpTypePrinter().Print(new CSharpTypePrintRequest(
                type,
                memberPolicyOverrides: [policy]))).Result;

        Assert.Contains(
            "int IValue.Value",
            Assert.Single(result.Units).Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "init",
            Assert.Single(result.Units).Source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MemberShellProducer_PreservesInitOnlyPropertyShapes()
    {
        var skeleton = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Value",
            Kind: CSharpShellMemberKind.PropertySet,
            IsStatic: false,
            Parameters: [],
            ReturnType: "int",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.InitOnlyProperty,
            Body: null));
        var stub = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Value",
            Kind: CSharpShellMemberKind.PropertySet,
            IsStatic: false,
            Parameters: [],
            ReturnType: "int",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.ThrowInit,
            Body: null));

        Assert.Equal(CSharpBodyPolicy.Skeleton, skeleton.BodyPolicy);
        Assert.Equal(CSharpBodyPolicy.Stub, stub.BodyPolicy);
        Assert.All(
            [skeleton, stub],
            policy => Assert.Collection(
                policy.Member.SignatureModel!.Accessors,
                accessor => Assert.Equal("init", accessor.Kind)));
        var body = Assert.IsType<CSharpPropertyBody>(stub.Body);
        Assert.Null(body.Getter);
        Assert.Equal(CSharpAccessorBody.Throw, body.Setter);
    }

    [Theory]
    [InlineData(CSharpShellMemberKind.Method, "Run")]
    [InlineData(CSharpShellMemberKind.Method, " IRunner.Run")]
    [InlineData(CSharpShellMemberKind.Method, "IRunner..Run")]
    [InlineData(CSharpShellMemberKind.Method, "I Runner.Run")]
    [InlineData(CSharpShellMemberKind.Method, "IMap<I Runner, int>.Run")]
    [InlineData(CSharpShellMemberKind.Method, "IMap<Ns..Value, int>.Run")]
    [InlineData(CSharpShellMemberKind.Method, "IMap<string,, int>.Run")]
    public void MemberShellProducer_RejectsUnqualifiedExplicitInterfaceName(
        CSharpShellMemberKind memberKind,
        string explicitInterfaceMemberName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
                Name: "Run",
                Kind: memberKind,
                IsStatic: false,
                Parameters: [],
                ReturnType: "void",
                TypeParameters: [],
                BodyKind: CSharpShellBodyKind.TargetBody,
                Body: "return;",
                ExplicitInterfaceMemberName: explicitInterfaceMemberName)));

        Assert.Contains(
            "must be qualified as 'Interface.Member'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IMap<string, int>.Run")]
    [InlineData("IMap<System.Collections.Generic.IReadOnlyList<string>, int[,]>.Run")]
    [InlineData("IMap<(int left, string right), int>.Run")]
    public void MemberShellProducer_AcceptsGenericExplicitInterfaceName(string name)
    {
        var policy = CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
            Name: "Run",
            Kind: CSharpShellMemberKind.Method,
            IsStatic: false,
            Parameters: [],
            ReturnType: "void",
            TypeParameters: [],
            BodyKind: CSharpShellBodyKind.TargetBody,
            Body: "return;",
            ExplicitInterfaceMemberName: name));

        Assert.Equal($"void {name}()", policy.Member.Signature);
    }

    [Fact]
    public void MemberShellProducer_RejectsBodylessExplicitInterfaceMethod()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
                Name: "Run",
                Kind: CSharpShellMemberKind.Method,
                IsStatic: false,
                Parameters: [],
                ReturnType: "void",
                TypeParameters: [],
                BodyKind: CSharpShellBodyKind.None,
                Body: null,
                ExplicitInterfaceMemberName: "IRunner.Run")));

        Assert.Contains(
            "body shape 'None' is not valid for member kind 'Method'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpShellBodyKind.None)]
    [InlineData(CSharpShellBodyKind.InitOnlyProperty)]
    public void MemberShellProducer_RejectsExplicitSetterOnlySkeleton(
        CSharpShellBodyKind bodyKind)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
                Name: "Value",
                Kind: CSharpShellMemberKind.PropertySet,
                IsStatic: false,
                Parameters: [],
                ReturnType: "int",
                TypeParameters: [],
                BodyKind: bodyKind,
                Body: null,
                ExplicitInterfaceMemberName: "IValue.Value")));

        Assert.Contains(
            $"body shape '{bodyKind}' is not valid for member kind 'PropertySet'",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpShellMemberKind.Constructor)]
    [InlineData(CSharpShellMemberKind.Field)]
    public void MemberShellProducer_RejectsExplicitInterfaceNameForUnsupportedMember(
        CSharpShellMemberKind memberKind)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
                Name: "Member",
                Kind: memberKind,
                IsStatic: false,
                Parameters: [],
                ReturnType: "int",
                TypeParameters: [],
                BodyKind: CSharpShellBodyKind.None,
                Body: null,
                ExplicitInterfaceMemberName: "IContract.Member")));

        Assert.Contains(
            $"Member kind '{memberKind}' does not support an explicit-interface name",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CSharpShellMemberKind.PropertyGet, CSharpShellBodyKind.TargetSetterWithGetter)]
    [InlineData(CSharpShellMemberKind.PropertySet, CSharpShellBodyKind.TargetGetterWithSetter)]
    [InlineData(CSharpShellMemberKind.Method, CSharpShellBodyKind.TargetInitBody)]
    [InlineData(CSharpShellMemberKind.Method, CSharpShellBodyKind.TargetEventAccessorWithSibling)]
    [InlineData(CSharpShellMemberKind.EventAdd, CSharpShellBodyKind.AutoProperty)]
    [InlineData(CSharpShellMemberKind.Method, CSharpShellBodyKind.FieldInitializer)]
    [InlineData(CSharpShellMemberKind.Field, CSharpShellBodyKind.Throw)]
    [InlineData(CSharpShellMemberKind.PropertySet, CSharpShellBodyKind.AutoProperty)]
    public void MemberShellProducer_RejectsBodyKindForDifferentMemberShape(
        CSharpShellMemberKind memberKind,
        CSharpShellBodyKind bodyKind)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
                Name: "Member",
                Kind: memberKind,
                IsStatic: false,
                Parameters: [],
                ReturnType: "int",
                TypeParameters: [],
                BodyKind: bodyKind,
                Body: "return 0;",
                SiblingBody: "return;")));

        Assert.Contains(
            $"body shape '{bodyKind}' is not valid for member kind '{memberKind}'",
            exception.Message,
            StringComparison.Ordinal);
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

    [Fact]
    public void MemberShellProducer_PreservesStubConstructorInitializer()
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
                Body: null,
                ConstructorInitializer: "base((int*)0)"));

        Assert.True(policy.Member.IsUnsafe);
        var body = Assert.IsType<CSharpBlockBody>(policy.Body);
        Assert.Equal("throw null;", body.Source);
        Assert.NotNull(body.ConstructorInitializer);
        Assert.Equal(CSharpConstructorInitializerKind.Base, body.ConstructorInitializer.Kind);
        Assert.Equal(["(int*)0"], body.ConstructorInitializer.Arguments);
    }

    [Theory]
    [InlineData(CSharpShellMemberKind.Method, "base(1)")]
    [InlineData(CSharpShellMemberKind.Constructor, "other(1)")]
    public void MemberShellProducer_RejectsInvalidConstructorInitializer(
        CSharpShellMemberKind kind,
        string initializer)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CSharpMemberShellProducer.BuildPolicy(new CSharpMemberShellSpec(
                Name: kind == CSharpShellMemberKind.Constructor ? ".ctor" : "Run",
                Kind: kind,
                IsStatic: false,
                Parameters: [],
                ReturnType: kind == CSharpShellMemberKind.Constructor ? null : "void",
                TypeParameters: [],
                BodyKind: CSharpShellBodyKind.Throw,
                Body: null,
                ConstructorInitializer: initializer)));

        Assert.Contains("Constructor initializers require a constructor shell", exception.Message);
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

    static byte[] BuildHostileMetadataFixture(string metadataName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("HostileSelfNameFixture.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("HostileSelfNameFixture"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString(metadataName),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var peBuilder = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
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
