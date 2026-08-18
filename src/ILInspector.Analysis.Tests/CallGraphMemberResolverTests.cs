using System.Collections.Immutable;
using System.Reflection.Metadata;
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
        Assert.Equal("System.ReadOnlySpan{System.Object}", objectSelector.ParameterTypes[0]);
        Assert.Equal("System.Int32@", byRefSelector.ParameterTypes[1]);
    }

    [Fact]
    public void Resolve_DistinguishesInstanceAndStaticMethodsWithTheSameSignature()
    {
        var instanceMember = Method("int");
        instanceMember.MetadataToken = 0x06000001;
        var staticMember = Method("int");
        staticMember.MetadataToken = 0x06000002;
        staticMember.IsStatic = true;
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [instanceMember, staticMember],
        };
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        var instanceReference = new MemberRef(
            declaringType,
            "M",
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        };
        var staticReference = new MemberRef(
            declaringType,
            "M",
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method);

        CallGraphMemberSelector instanceSelector =
            CallGraphMemberResolver.CreateSelector(instanceReference);
        CallGraphMemberSelector staticSelector =
            CallGraphMemberResolver.CreateSelector(staticReference);

        Assert.NotEqual(instanceSelector.Key, staticSelector.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, instanceMember).Key,
            instanceSelector.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, staticMember).Key,
            staticSelector.Key);
        Assert.Same(
            instanceMember,
            CallGraphMemberResolver.Resolve(
                type,
                instanceSelector.Name,
                instanceSelector.Key)!
                .Member);
        Assert.Same(
            staticMember,
            CallGraphMemberResolver.Resolve(
                type,
                staticSelector.Name,
                staticSelector.Key)!
                .Member);
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
    public void Resolve_SurfaceUsesMetadataTypeIdentityAndStructuralFallback()
    {
        var member = Method("int");
        member.MetadataToken = 0x06000002;
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Outer<T>.Inner",
            MetadataName = "Outer`1+Inner",
            Members = [member],
        };
        var surface = new ApiSurface { Types = [type] };
        string selector = CallGraphMemberResolver.CreateSelector(type, member).Key;

        CallGraphMemberResolution? resolved = CallGraphMemberResolver.Resolve(
            surface,
            "Samples.Outer`1+Inner",
            member.Name,
            selector,
            metadataToken: 0x06000001);

        Assert.NotNull(resolved);
        Assert.Same(member, resolved.Member);
        Assert.Equal(0x06000002, resolved.BodyToken);
    }

    [Fact]
    public void ResolveDefinitionIdentity_DistinguishesNestingFromLiteralPlus()
    {
        var nestedMember = Method("int");
        nestedMember.MetadataToken = 0x06000001;
        var literalMember = Method("string");
        literalMember.MetadataToken = 0x06000002;
        var nested = new ApiType
        {
            Namespace = "Samples",
            Name = "Outer.Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("Samples", ["Outer", "Inner"]))
                .Name,
            Members = [nestedMember],
        };
        var literal = new ApiType
        {
            Namespace = "Samples",
            Name = "Outer+Inner",
            MetadataName = "Outer+Inner",
            DefinitionName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("Samples", ["Outer+Inner"]))
                .Name,
            Members = [literalMember],
        };
        var surface = new ApiSurface { Types = [nested, literal] };

        CallGraphMemberResolution? nestedResolution =
            CallGraphMemberResolver.ResolveDefinitionIdentity(
                surface,
                "Samples.Outer+Inner",
                nestedMember.Name,
                CallGraphMemberResolver.CreateSelector(nested, nestedMember).Key);
        CallGraphMemberResolution? literalResolution =
            CallGraphMemberResolver.ResolveDefinitionIdentity(
                surface,
                @"Samples.Outer\+Inner",
                literalMember.Name,
                CallGraphMemberResolver.CreateSelector(literal, literalMember).Key);

        Assert.Same(nestedMember, nestedResolution!.Member);
        Assert.Same(literalMember, literalResolution!.Member);
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
            MemberKind.Method)
        {
            HasThis = true,
        });
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
            MemberKind.Method)
        {
            HasThis = true,
        });
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
            MemberKind.Method)
        {
            HasThis = true,
        });
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var member = Method("Samples.Outer<int>.Inner<string>");

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
    }

    [Fact]
    public void Selector_DistinguishesNestedTypeFromLiteralPlusName()
    {
        MetadataTypeDefinitionName literalName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("Samples", ["Outer+Inner"]))
            .Name;
        TypeRef literal = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer+Inner",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                literalName));
        TypeRef nested = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer+Inner",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Samples",
                        ["Outer", "Inner"]))
                .Name));

        CallGraphMemberSelector literalSelector =
            CallGraphMemberResolver.CreateSelector(new MemberRef(
                TypeRef.Definition("Samples", "Samples", "Owner"),
                "M",
                [literal],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method));
        CallGraphMemberSelector nestedSelector =
            CallGraphMemberResolver.CreateSelector(new MemberRef(
                TypeRef.Definition("Samples", "Samples", "Owner"),
                "M",
                [nested],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method));

        Assert.Equal("Samples.Outer+Inner", literalSelector.ParameterTypes[0]);
        Assert.Equal("Samples.Outer.Inner", nestedSelector.ParameterTypes[0]);
        Assert.NotEqual(literalSelector.Key, nestedSelector.Key);
    }

    [Fact]
    public void SelectorAndAnalysisKey_DistinguishNestedTypeFromLiteralDotName()
    {
        TypeRef literal = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer.Inner",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Samples",
                        ["Outer.Inner"]))
                .Name));
        TypeRef nested = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer+Inner",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Samples",
                        ["Outer", "Inner"]))
                .Name));

        CallGraphMemberSelector Selector(TypeRef parameter) =>
            CallGraphMemberResolver.CreateSelector(new MemberRef(
                TypeRef.Definition("Samples", "Samples", "Owner"),
                "M",
                [parameter],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method));

        Assert.NotEqual(Selector(literal).Key, Selector(nested).Key);
        Assert.NotEqual(
            GenericMemberIdentity.KeyFragment(literal),
            GenericMemberIdentity.KeyFragment(nested));
    }

    [Fact]
    public void Selector_ExactOrdinaryTypeStillMatchesApiSignature()
    {
        TypeRef parameter = TypeRef.Definition(
            "Samples",
            "Samples",
            "Token",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                Name("Samples", ["Token"])));
        var member = Method("Samples.Token");
        var owner = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [member],
        };
        var reference = new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [parameter],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        };

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(owner, member).Key,
            CallGraphMemberResolver.CreateSelector(reference).Key);
    }

    [Fact]
    public void Resolve_UsesTokenWhenApiSignatureCannotProjectExactDelimiter()
    {
        TypeRef parameter = TypeRef.Definition(
            "Samples",
            "Samples",
            "A+B",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                Name("Samples", ["A+B"])));
        var member = Method("Samples.A+B");
        member.MetadataToken = 0x06000001;
        var owner = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [member],
        };
        CallGraphMemberSelector referenceSelector =
            CallGraphMemberResolver.CreateSelector(new MemberRef(
                TypeRef.Definition("Samples", "Samples", "Owner"),
                "M",
                [parameter],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method)
            {
                HasThis = true,
            });

        Assert.NotEqual(
            CallGraphMemberResolver.CreateSelector(owner, member).Key,
            referenceSelector.Key);
        Assert.Null(CallGraphMemberResolver.Resolve(
            owner,
            referenceSelector.Name,
            referenceSelector.Key));
        Assert.Same(
            member,
            CallGraphMemberResolver.Resolve(
                owner,
                referenceSelector.Name,
                referenceSelector.Key,
                metadataToken: 0x06000001)!
                .Member);
    }

    // End-to-end: the identity the product projects for a call-graph target's declaring type is
    // the identity its resolver matches, and it distinguishes nesting from a literal delimiter.
    // The flattened metadata spelling — which cannot — is published only where it names one type.
    [Fact]
    public void DeclaringTypeIdentity_IsCarriedAndMatchedWithoutFlattening()
    {
        MetadataTypeDefinitionName nestedName = Name("Samples", ["Outer", "Inner"]);
        MetadataTypeDefinitionName literalName = Name("Samples", ["Outer+Inner"]);
        TypeRef nestedType = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer+Inner",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                nestedName));
        TypeRef literalType = TypeRef.Definition(
            "Samples",
            "Samples",
            "Outer+Inner",
            new ResolvableTypeReference(
                new TypeReferenceOrigin.CurrentAssembly(),
                literalName));

        Assert.Equal(
            "Samples.Outer+Inner",
            CallGraphMemberResolver.DefinitionIdentity(nestedType));
        Assert.Equal(
            @"Samples.Outer\+Inner",
            CallGraphMemberResolver.DefinitionIdentity(literalType));
        Assert.NotEqual(
            CallGraphMemberResolver.DefinitionIdentity(nestedType),
            CallGraphMemberResolver.DefinitionIdentity(literalType));

        Assert.Equal(
            "Samples.Outer+Inner",
            CallGraphMemberResolver.UnambiguousMetadataIdentity(nestedType));
        Assert.Null(CallGraphMemberResolver.UnambiguousMetadataIdentity(literalType));

        // A type whose namespace or segment carries the other delimiter is withheld too.
        Assert.Null(CallGraphMemberResolver.UnambiguousMetadataIdentity(
            TypeRef.Definition(
                "Samples",
                "Samples",
                "Outer.Inner",
                new ResolvableTypeReference(
                    new TypeReferenceOrigin.CurrentAssembly(),
                    Name("Samples", ["Outer.Inner"])))));

        // A decoder-free TypeRef keeps the legacy spelling: there is no structured name that
        // could contradict it.
        Assert.Equal(
            "Samples.Widget",
            CallGraphMemberResolver.UnambiguousMetadataIdentity(
                TypeRef.Definition("Samples", "Samples", "Widget")));
        Assert.Null(CallGraphMemberResolver.DefinitionIdentity(
            TypeRef.Definition("Samples", "Samples", "Widget")));

        // The carried identity is exactly what the resolver matches on the other side.
        var nestedMember = Method("int");
        nestedMember.MetadataToken = 0x06000001;
        var literalMember = Method("string");
        literalMember.MetadataToken = 0x06000002;
        var surface = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "Samples",
                    Name = "Outer.Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = nestedName,
                    Members = [nestedMember],
                },
                new ApiType
                {
                    Namespace = "Samples",
                    Name = "Outer+Inner",
                    MetadataName = "Outer+Inner",
                    DefinitionName = literalName,
                    Members = [literalMember],
                },
            ],
        };

        Assert.Same(
            nestedMember,
            CallGraphMemberResolver.ResolveDefinitionIdentity(
                surface,
                CallGraphMemberResolver.DefinitionIdentity(nestedType)!,
                nestedMember.Name,
                CallGraphMemberResolver.CreateSelector(surface.Types[0], nestedMember).Key)!
                .Member);
        Assert.Same(
            literalMember,
            CallGraphMemberResolver.ResolveDefinitionIdentity(
                surface,
                CallGraphMemberResolver.DefinitionIdentity(literalType)!,
                literalMember.Name,
                CallGraphMemberResolver.CreateSelector(surface.Types[1], literalMember).Key)!
                .Member);
    }

    static MetadataTypeDefinitionName Name(string @namespace, string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(@namespace, [.. segments]))
            .Name;

    static ApiMember Method(string parameterType) => new()
    {
        Name = "M",
        Kind = "method",
        ReturnType = "void",
        SignatureModel = new ApiSignature
        {
            ReturnType = "void",
            Parameters = [new ApiParameter { Name = "value", Type = parameterType }],
        },
    };

    static ApiMember Indexer(string parameterType, int getterToken) => new()
    {
        Name = "Item",
        Kind = "property",
        ReturnType = "int",
        GetterToken = getterToken,
        SignatureModel = new ApiSignature
        {
            ReturnType = "int",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "index",
                    Type = parameterType,
                },
            ],
        },
    };
}
