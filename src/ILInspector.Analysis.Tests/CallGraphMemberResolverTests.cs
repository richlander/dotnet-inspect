using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class CallGraphMemberResolverTests
{
    [Fact]
    public void Selector_PreservesGenericArgumentsAndByRefShape()
    {
        var span = TypeRef.Definition("corelib", "System", "ReadOnlySpan`1");
        var objectSpan = TypeRef.GenericInstance(
            span,
            [TypeRef.CoreLib("System", "Object")]);
        var stringSpan = TypeRef.GenericInstance(
            span,
            [TypeRef.CoreLib("System", "String")]);

        var objectSelector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.CoreLib("System", "String"),
            "Concat",
            [objectSpan],
            TypeRef.CoreLib("System", "String"),
            MemberKind.Method));
        var stringSelector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.CoreLib("System", "String"),
            "Concat",
            [stringSpan],
            TypeRef.CoreLib("System", "String"),
            MemberKind.Method));
        var byRefSelector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.CoreLib("System", "Int32"),
            "TryParse",
            [TypeRef.CoreLib("System", "String"), TypeRef.ByRef(TypeRef.CoreLib("System", "Int32"))],
            TypeRef.CoreLib("System", "Boolean"),
            MemberKind.Method));

        Assert.NotEqual(objectSelector.Key, stringSelector.Key);
        Assert.Equal(
            "[corelib]System.ReadOnlySpan{[corelib]System.Object}",
            objectSelector.ParameterTypes[0]);
        Assert.Equal("[corelib]System.Int32@", byRefSelector.ParameterTypes[1]);
    }

    [Fact]
    public void Resolve_UsesStructuredIndexerAccessorIdentity()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Indexed",
            Members =
            [
                Indexer("int", 0x06000001),
                Indexer("string", 0x06000002),
            ],
        };
        var selector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Indexed"),
            "get_Item",
            [TypeRef.CoreLib("System", "String")],
            TypeRef.CoreLib("System", "Int32"),
            MemberKind.Method)
        {
            HasThis = true,
        });

        var resolved = CallGraphMemberResolver.Resolve(
            type,
            selector.Name,
            selector.Key);

        Assert.NotNull(resolved);
        Assert.Equal(0x06000002, resolved.BodyToken);
        Assert.Equal("string", resolved.Member.SignatureModel!.Parameters[0].Type);
    }

    [Fact]
    public void Resolve_RequiresTokenAndSelectorToAgree()
    {
        var first = Indexer("int", 0x06000001);
        var second = Indexer("string", 0x06000002);
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Indexed",
            Members = [first, second],
        };
        var deliberatelyWrongShape = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Indexed"),
            "get_Item",
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Int32"),
            MemberKind.Method)
        {
            HasThis = true,
        });

        var resolved = CallGraphMemberResolver.Resolve(
            type,
            deliberatelyWrongShape.Name,
            deliberatelyWrongShape.Key,
            metadataToken: 0x06000002);

        Assert.Same(first, resolved!.Member);
        Assert.Equal(0x06000001, resolved.BodyToken);
    }

    [Fact]
    public void BodySelectors_PreserveExactAccessorIdentity()
    {
        var member = Indexer("string", 0x06000002);
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Indexed",
            Members = [member],
        };

        var body = Assert.Single(CallGraphMemberResolver.CreateBodySelectors(type, member));

        Assert.Equal(0x06000002, body.BodyToken);
        Assert.Equal("get_Item", body.MemberName);
        Assert.NotEqual(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            body.SelectorKey);
    }

    [Fact]
    public void Selector_ErasesCustomModifiersLikeApiSurface()
    {
        var modified = TypeRef.UnsupportedModified(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "IsExternalInit"),
            TypeRef.CoreLib("System", "Int32"),
            isRequired: true);
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [modified],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method));
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var member = Method("int");

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
    }

    [Fact]
    public void Selector_PreservesFunctionPointerPayload()
    {
        var signature = new MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.CDecl,
                SignatureAttributes.None),
            TypeRef.CoreLib("System", "Void"),
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [TypeRef.CoreLib("System", "Int32")]);
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [TypeRef.UnsupportedFunctionPointer(signature)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method));
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var member = Method("delegate* unmanaged[Cdecl]<int, void>");

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
    }

    [Fact]
    public void Selector_PreservesNestedGenericSegments()
    {
        var definition = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer`1+Inner`1");
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [
                TypeRef.GenericInstance(
                    definition,
                    [
                        TypeRef.CoreLib("System", "Int32"),
                        TypeRef.CoreLib("System", "String"),
                    ]),
            ],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method));
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var member = Method(
            "Samples.Outer<int>.Inner<string>",
            "[Samples]Samples.Outer{[corelib]System.Int32}.Inner{[corelib]System.String}");

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
    }

    [Fact]
    public void Resolve_DistinguishesStaticAndInstanceCallingShape()
    {
        var staticMember = WithToken(Method("int"), 0x06000001, isStatic: true);
        var instanceMember = WithToken(Method("int"), 0x06000002, isStatic: false);
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [staticMember, instanceMember],
        };
        var selector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        });

        var resolved = CallGraphMemberResolver.Resolve(type, selector.Name, selector.Key);

        Assert.Same(instanceMember, resolved!.Member);
    }

    [Fact]
    public void Resolve_DistinguishesSameNamedParameterTypesFromDifferentAssemblies()
    {
        var first = WithToken(
            Method("Samples.Payload", "[Contracts.A]Samples.Payload"),
            0x06000001,
            isStatic: true);
        var second = WithToken(
            Method("Samples.Payload", "[Contracts.B]Samples.Payload"),
            0x06000002,
            isStatic: true);
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [first, second],
        };
        var selector = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [TypeRef.Definition("Contracts.B", "Samples", "Payload")],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method));

        var resolved = CallGraphMemberResolver.Resolve(type, selector.Name, selector.Key);

        Assert.Same(second, resolved!.Member);
    }

    [Fact]
    public void ApiSurface_RetainsDefiningAssemblyInSignatureIdentity()
    {
        using var stream = File.OpenRead(typeof(CallGraphMemberResolverTests).Assembly.Location);
        using var reader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(reader, includeAll: true);
        ApiType type = Assert.Single(surface.Types, candidate =>
            candidate.Members.Any(member => member.Name == nameof(IdentityFixture)));
        ApiMember member = Assert.Single(type.Members, candidate =>
            candidate.Name == nameof(IdentityFixture));

        Assert.Equal(
            "[ILInspector.Analysis.Tests]ILInspector.Analysis.Tests.CallGraphMemberResolverTests.IdentityPayload",
            member.SignatureModel!.Parameters[0].TypeIdentity);
        ApiMember byRef = Assert.Single(type.Members, candidate =>
            candidate.Name == nameof(ByRefIdentityFixture));
        Assert.Equal(
            "[ILInspector.Analysis.Tests]ILInspector.Analysis.Tests.CallGraphMemberResolverTests.IdentityPayload@",
            byRef.SignatureModel!.Parameters[0].TypeIdentity);
    }

    static ApiMember Method(string parameterType, string? parameterIdentity = null) => new()
    {
        Name = "M",
        Kind = "method",
        ReturnType = "void",
        IsStatic = true,
        SignatureModel = new ApiSignature
        {
            ReturnType = "void",
            ReturnTypeIdentity = "[corelib]System.Void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "value",
                    Type = parameterType,
                    TypeIdentity = parameterIdentity ?? parameterType switch
                    {
                        "int" => "[corelib]System.Int32",
                        "string" => "[corelib]System.String",
                        "delegate* unmanaged[Cdecl]<int, void>" =>
                            "delegate* unmanaged[Cdecl]{[corelib]System.Int32,[corelib]System.Void}",
                        _ => parameterType,
                    },
                },
            ],
        },
    };

    static ApiMember WithToken(ApiMember member, int token, bool isStatic)
    {
        member.MetadataToken = token;
        member.IsStatic = isStatic;
        return member;
    }

    static void IdentityFixture(IdentityPayload value) { }
    static void ByRefIdentityFixture(ref IdentityPayload value) { }

    sealed class IdentityPayload;

    static ApiMember Indexer(string parameterType, int getterToken) => new()
    {
        Name = "Item",
        Kind = "property",
        ReturnType = "int",
        GetterToken = getterToken,
        SignatureModel = new ApiSignature
        {
            ReturnType = "int",
            ReturnTypeIdentity = "[corelib]System.Int32",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "index",
                    Type = parameterType,
                    TypeIdentity = parameterType switch
                    {
                        "int" => "[corelib]System.Int32",
                        "string" => "[corelib]System.String",
                        _ => parameterType,
                    },
                },
            ],
        },
    };
}
