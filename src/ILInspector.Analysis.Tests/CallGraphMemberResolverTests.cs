using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
        CallGraphMemberResolution instanceResolution =
            CallGraphMemberResolver.Resolve(
                type,
                instanceSelector.Name,
                instanceSelector.Key)!;
        Assert.Same(type, instanceResolution.Type);
        Assert.Same(instanceMember, instanceResolution.Member);
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
    public void Resolve_MatchesInitSetterReturnModifierAcrossProducers()
    {
        var member = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            ReturnType = "int",
            SetterToken = 0x06000002,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "set",
                        StructuralReturnType = StructuralTypeIdentity.Modified(
                            required: true,
                            "System.Runtime.CompilerServices.IsExternalInit",
                            "System.Void"),
                    },
                ],
            },
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [member],
        };
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "set_Value",
            [TypeRef.CoreLib("System", "Int32")],
            TypeRef.UnsupportedModified(
                TypeRef.CoreLib("System.Runtime.CompilerServices", "IsExternalInit"),
                TypeRef.CoreLib("System", "Void"),
                isRequired: true),
            MemberKind.Method)
        {
            HasThis = true,
        });

        var setter = Assert.Single(
            CallGraphMemberResolver.CreateBodySelectors(type, member),
            selector => selector.MemberName == "set_Value");

        Assert.Equal(graph.Key, setter.SelectorKey);
        Assert.Equal(
            0x06000002,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.BodyToken);
    }

    [Fact]
    public void Resolve_MatchesCompiledInitSetterAcrossProducers()
    {
        using var stream = File.OpenRead(typeof(InitAccessorFixtures).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == nameof(InitAccessorFixtures));
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(InitAccessorFixtures.Value));
        CallGraphMemberBodySelector setter = Assert.Single(
            CallGraphMemberResolver.CreateBodySelectors(type, member),
            selector => selector.MemberName == "set_Value");

        MemberRef reference = MemberResolver.ResolveMethod(
            peReader.GetMetadataReader(),
            MetadataTokens.EntityHandle(setter.BodyToken),
            GenericScope.Empty);
        CallGraphMemberSelector graph = CallGraphMemberResolver.CreateSelector(reference);

        Assert.Equal(graph.Key, setter.SelectorKey);
        Assert.Contains("IsExternalInit", setter.SelectorKey, StringComparison.Ordinal);
        Assert.Equal(
            setter.BodyToken,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.BodyToken);
    }

    [Fact]
    public void Resolve_MatchesExplicitInterfaceAccessorNameAcrossProducers()
    {
        var member = new ApiMember
        {
            Name = "INamed.Value",
            Kind = "property",
            ReturnType = "int",
            GetterToken = 0x06000002,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "get",
                        Name = "INamed.get_Value",
                    },
                ],
            },
        };
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [member],
        };
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "INamed.get_Value",
            ImmutableArray<TypeRef>.Empty,
            TypeRef.CoreLib("System", "Int32"),
            MemberKind.Method)
        {
            HasThis = true,
        });

        var getter = Assert.Single(CallGraphMemberResolver.CreateBodySelectors(type, member));
        Assert.Equal("INamed.get_Value", getter.MemberName);
        Assert.NotEqual("get_INamed.Value", getter.MemberName);
        Assert.Equal(graph.Key, getter.SelectorKey);
        Assert.Equal(
            0x06000002,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.BodyToken);
    }

    [Fact]
    public void Resolve_MatchesCompiledExplicitInterfaceAccessorAcrossProducers()
    {
        using var stream = File.OpenRead(typeof(ExplicitAccessorFixtures).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == nameof(ExplicitAccessorFixtures));
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Kind == "property"
                && candidate.Name.EndsWith(
                    $".{nameof(IExplicitAccessor.Value)}",
                    StringComparison.Ordinal));
        CallGraphMemberBodySelector getter = Assert.Single(
            CallGraphMemberResolver.CreateBodySelectors(type, member),
            selector => selector.BodyToken == member.GetterToken);

        MemberRef reference = MemberResolver.ResolveMethod(
            peReader.GetMetadataReader(),
            MetadataTokens.EntityHandle(getter.BodyToken),
            GenericScope.Empty);
        CallGraphMemberSelector graph = CallGraphMemberResolver.CreateSelector(reference);

        Assert.Equal(reference.Name, getter.MemberName);
        Assert.False(getter.MemberName.StartsWith("get_", StringComparison.Ordinal));
        Assert.Contains(".get_", getter.MemberName, StringComparison.Ordinal);
        Assert.Equal(graph.Key, getter.SelectorKey);
        Assert.Equal(
            getter.BodyToken,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.BodyToken);
    }

    [Fact]
    public void Resolve_MatchesCompiledNestedGenericAndByRefAcrossProducers()
    {
        using var stream = File.OpenRead(typeof(NestedGenericKeyFixtures).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader mdReader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == nameof(NestedGenericKeyFixtures));

        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeNested));
        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeRefNested));
        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeRefPlain));
        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeList));
        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeRefInt));
        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeListOfOuter));
        AssertCompiledSelectorAgreement(type, mdReader, nameof(NestedGenericKeyFixtures.TakeListOfNested));
        AssertCompiledSelectorAgreement(type, mdReader, "TakeHidden");
        AssertCompiledSelectorAgreement(type, mdReader, "TakeRefHidden");

        ApiMember[] wrapped = [.. type.Members.Where(candidate => candidate.Name == "TakeWrapped")];
        Assert.Equal(2, wrapped.Length);
        foreach (ApiMember member in wrapped)
            AssertCompiledSelectorAgreement(type, mdReader, member);
    }

    [Fact]
    public void Selector_PlacesByRefMarkerOutsideNestedGenericDisplay()
    {
        var nested = Method("ref Samples.Outer<int>.Inner<string>");
        nested.MetadataToken = 0x06000001;
        var plus = Method("ref Samples.Outer<int>+Inner<string>");
        plus.MetadataToken = 0x06000001;
        var ordinary = Method("ref int");
        ordinary.MetadataToken = 0x06000002;
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [nested, ordinary],
        };
        var plusType = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [plus],
        };
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        CallGraphMemberSelector nestedGraph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            [
                TypeRef.ByRef(
                    TypeRef.GenericInstance(
                        TypeRef.Definition("Samples", "Samples", "Outer`1+Inner`1"),
                        [
                            TypeRef.CoreLib("System", "Int32"),
                            TypeRef.CoreLib("System", "String"),
                        ])),
            ],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        });

        Assert.Null(nested.SignatureModel!.Parameters[0].StructuralType);
        Assert.Equal(
            "Samples.Outer{System.Int32}.Inner{System.String}@",
            CallGraphMemberResolver.CreateSelector(type, nested).ParameterTypes[0]);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, nested).Key,
            nestedGraph.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(plusType, plus).Key,
            nestedGraph.Key);
        Assert.Equal(
            "System.Int32@",
            CallGraphMemberResolver.CreateSelector(type, ordinary).ParameterTypes[0]);
        Assert.Same(
            nested,
            CallGraphMemberResolver.Resolve(type, nestedGraph.Name, nestedGraph.Key)!.Member);
    }

    [Fact]
    public void Selector_DistinguishesNestedGenericInsideAnotherGenericArgument()
    {
        var outer = Method("System.Collections.Generic.List<Samples.Outer<int>>");
        outer.MetadataToken = 0x06000001;
        var inner = Method("System.Collections.Generic.List<Samples.Outer<int>.Inner<string>>");
        inner.MetadataToken = 0x06000002;
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [outer, inner],
        };
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        CallGraphMemberSelector innerGraph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            [
                TypeRef.GenericInstance(
                    TypeRef.Definition("corelib", "System.Collections.Generic", "List`1"),
                    [
                        TypeRef.GenericInstance(
                            TypeRef.Definition("Samples", "Samples", "Outer`1+Inner`1"),
                            [
                                TypeRef.CoreLib("System", "Int32"),
                                TypeRef.CoreLib("System", "String"),
                            ]),
                    ]),
            ],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        });
        CallGraphMemberSelector outerGraph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            [
                TypeRef.GenericInstance(
                    TypeRef.Definition("corelib", "System.Collections.Generic", "List`1"),
                    [
                        TypeRef.GenericInstance(
                            TypeRef.Definition("Samples", "Samples", "Outer`1"),
                            [TypeRef.CoreLib("System", "Int32")]),
                    ]),
            ],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        });

        Assert.NotEqual(outerGraph.Key, innerGraph.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, outer).Key,
            outerGraph.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, inner).Key,
            innerGraph.Key);
        Assert.Same(
            outer,
            CallGraphMemberResolver.Resolve(type, outerGraph.Name, outerGraph.Key)!.Member);
        Assert.Same(
            inner,
            CallGraphMemberResolver.Resolve(type, innerGraph.Name, innerGraph.Key)!.Member);
    }

    [Fact]
    public void Selector_KeepsDisplaySpellingWhenModifiersArePresent()
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

        Assert.Equal("System.Int32", graph.ParameterTypes[0]);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).ParameterTypes[0],
            graph.ParameterTypes[0]);
        Assert.NotEqual(
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
        var member = Method(
            "delegate* unmanaged[Cdecl]<int, void>",
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: false,
                explicitThis: false,
                genericParameterCount: 0,
                requiredParameterCount: 1,
                ["System.Int32"],
                "System.Void"));

        Assert.Equal(
            "delegate* unmanaged[Cdecl]{System.Int32,System.Void}",
            graph.ParameterTypes[0]);
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

    [Fact]
    public void Selector_DistinguishesCustomModifiersPinnedAndFunctionPointerHeaders()
    {
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        TypeRef required = TypeRef.UnsupportedModified(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "IsExternalInit"),
            TypeRef.CoreLib("System", "Int32"),
            isRequired: true);
        TypeRef optional = TypeRef.UnsupportedModified(
            TypeRef.CoreLib("System.Runtime.CompilerServices", "IsExternalInit"),
            TypeRef.CoreLib("System", "Int32"),
            isRequired: false);
        TypeRef pinned = TypeRef.Pinned(TypeRef.CoreLib("System", "Int32"));
        TypeRef plain = TypeRef.CoreLib("System", "Int32");

        CallGraphMemberSelector requiredSelector = Graph(declaringType, required);
        CallGraphMemberSelector optionalSelector = Graph(declaringType, optional);
        CallGraphMemberSelector pinnedSelector = Graph(declaringType, pinned);
        CallGraphMemberSelector plainSelector = Graph(declaringType, plain);

        Assert.Equal("System.Int32", requiredSelector.ParameterTypes[0]);
        Assert.Equal("System.Int32", pinnedSelector.ParameterTypes[0]);
        Assert.NotEqual(requiredSelector.Key, optionalSelector.Key);
        Assert.NotEqual(requiredSelector.Key, plainSelector.Key);
        Assert.NotEqual(pinnedSelector.Key, plainSelector.Key);

        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        ApiMember requiredMember = Method(
            "int",
            StructuralTypeIdentity.Modified(
                required: true,
                "System.Runtime.CompilerServices.IsExternalInit",
                "System.Int32"));
        requiredMember.MetadataToken = 0x06000001;
        ApiMember pinnedMember = Method(
            "int",
            StructuralTypeIdentity.Pinned("System.Int32"));
        pinnedMember.MetadataToken = 0x06000002;
        type.Members = [requiredMember, pinnedMember];

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, requiredMember).Key,
            requiredSelector.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, pinnedMember).Key,
            pinnedSelector.Key);
        Assert.Same(
            requiredMember,
            CallGraphMemberResolver.Resolve(
                type,
                requiredSelector.Name,
                requiredSelector.Key)!
                .Member);
        Assert.Same(
            pinnedMember,
            CallGraphMemberResolver.Resolve(
                type,
                pinnedSelector.Name,
                pinnedSelector.Key)!
                .Member);

        CallGraphMemberSelector cdecl = Graph(
            declaringType,
            FunctionPointer(
                SignatureCallingConvention.CDecl,
                SignatureAttributes.None,
                requiredParameterCount: 1,
                genericParameterCount: 0));
        CallGraphMemberSelector instance = Graph(
            declaringType,
            FunctionPointer(
                SignatureCallingConvention.CDecl,
                SignatureAttributes.Instance,
                requiredParameterCount: 1,
                genericParameterCount: 0));
        CallGraphMemberSelector explicitThis = Graph(
            declaringType,
            FunctionPointer(
                SignatureCallingConvention.CDecl,
                SignatureAttributes.Instance | SignatureAttributes.ExplicitThis,
                requiredParameterCount: 1,
                genericParameterCount: 0));
        CallGraphMemberSelector generic = Graph(
            declaringType,
            FunctionPointer(
                SignatureCallingConvention.CDecl,
                SignatureAttributes.Generic,
                requiredParameterCount: 1,
                genericParameterCount: 1));
        CallGraphMemberSelector varargs = Graph(
            declaringType,
            FunctionPointer(
                SignatureCallingConvention.CDecl,
                SignatureAttributes.None,
                requiredParameterCount: 0,
                genericParameterCount: 0));

        Assert.NotEqual(cdecl.Key, instance.Key);
        Assert.NotEqual(instance.Key, explicitThis.Key);
        Assert.NotEqual(cdecl.Key, generic.Key);
        Assert.NotEqual(cdecl.Key, varargs.Key);

        ApiMember cdeclMember = Method(
            "delegate* unmanaged[Cdecl]<int, void>",
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.CDecl,
                hasThis: false,
                explicitThis: false,
                genericParameterCount: 0,
                requiredParameterCount: 1,
                ["System.Int32"],
                "System.Void"));
        cdeclMember.MetadataToken = 0x06000003;
        type.Members.Add(cdeclMember);

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, cdeclMember).Key,
            cdecl.Key);
        Assert.Same(
            cdeclMember,
            CallGraphMemberResolver.Resolve(type, cdecl.Name, cdecl.Key)!.Member);
    }

    [Fact]
    public void Selector_UsesPositionalGenericIdentityWhenStructuralTypeIsAbsent()
    {
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            TypeParameters = [new TypeParameter { Name = "TOuter" }],
            Members = [],
        };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            ReturnType = "void",
            MetadataToken = 0x06000001,
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                TypeParameters = [new TypeParameter { Name = "TMethod" }],
                Parameters =
                [
                    new ApiParameter { Name = "method", Type = "TMethod" },
                    new ApiParameter { Name = "outer", Type = "TOuter" },
                    new ApiParameter { Name = "list", Type = "System.Collections.Generic.List<TMethod>" },
                ],
            },
        };
        type.Members = [member];
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            [
                TypeRef.MethodGenericParameter(0),
                TypeRef.GenericParameter(0),
                TypeRef.GenericInstance(
                    TypeRef.Definition("corelib", "System.Collections.Generic", "List`1"),
                    [TypeRef.MethodGenericParameter(0)]),
            ],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
            GenericArity = 1,
        });

        Assert.Null(member.SignatureModel!.Parameters[0].StructuralType);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
        Assert.Equal("M0", graph.ParameterTypes[0]);
        Assert.Equal("T0", graph.ParameterTypes[1]);
        Assert.Same(
            member,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.Member);
    }

    [Fact]
    public void Selector_KeepsNestedGenericDisplayIdentityWhenStructuralTypeIsAbsent()
    {
        var nested = Method("Samples.Outer<int>.Inner<string>");
        nested.MetadataToken = 0x06000001;
        var flat = Method("Samples.Outer<int, string>");
        flat.MetadataToken = 0x06000002;
        var type = new ApiType
        {
            Namespace = "Samples",
            Name = "Owner",
            Members = [nested, flat],
        };

        CallGraphMemberSelector nestedSelector =
            CallGraphMemberResolver.CreateSelector(type, nested);
        CallGraphMemberSelector flatSelector =
            CallGraphMemberResolver.CreateSelector(type, flat);

        Assert.NotEqual(nestedSelector.Key, flatSelector.Key);
        Assert.Contains(".Inner{", nestedSelector.ParameterTypes[0], StringComparison.Ordinal);
        Assert.Same(
            nested,
            CallGraphMemberResolver.Resolve(type, nestedSelector.Name, nestedSelector.Key)!.Member);
        Assert.Same(
            flat,
            CallGraphMemberResolver.Resolve(type, flatSelector.Name, flatSelector.Key)!.Member);
    }

    [Fact]
    public void Selector_MatchesRefReadonlyGenericAcrossProducers()
    {
        TypeRef modified = TypeRef.UnsupportedModified(
            TypeRef.CoreLib("System.Runtime.InteropServices", "InAttribute"),
            TypeRef.ByRef(TypeRef.MethodGenericParameter(0)),
            isRequired: true);
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            ImmutableArray<TypeRef>.Empty,
            modified,
            MemberKind.Method)
        {
            HasThis = true,
            GenericArity = 1,
        });
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            ReturnType = "ref readonly T",
            MetadataToken = 0x06000001,
            SignatureModel = new ApiSignature
            {
                ReturnType = "ref readonly T",
                StructuralReturnType = StructuralTypeIdentity.Modified(
                    required: true,
                    "System.Runtime.InteropServices.InAttribute",
                    "M0@"),
                TypeParameters = [new TypeParameter { Name = "T" }],
            },
        };
        type.Members = [member];

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
        Assert.Same(
            member,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.Member);
    }

    [Fact]
    public void Selector_MatchesNestedGenericUnderPayloadAcrossProducers()
    {
        TypeRef inAttribute = TypeRef.CoreLib("System.Runtime.InteropServices", "InAttribute");
        TypeRef nested = TypeRef.UnsupportedModified(
            inAttribute,
            TypeRef.ByRef(
                TypeRef.GenericInstance(
                    TypeRef.Definition("Samples", "Samples", "Outer`1+Inner`1"),
                    [
                        TypeRef.CoreLib("System", "Int32"),
                        TypeRef.CoreLib("System", "String"),
                    ])),
            isRequired: true);
        TypeRef flat = TypeRef.UnsupportedModified(
            inAttribute,
            TypeRef.ByRef(
                TypeRef.GenericInstance(
                    TypeRef.Definition("Samples", "Samples", "Outer`2"),
                    [
                        TypeRef.CoreLib("System", "Int32"),
                        TypeRef.CoreLib("System", "String"),
                    ])),
            isRequired: true);
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        CallGraphMemberSelector nestedGraph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            ImmutableArray<TypeRef>.Empty,
            nested,
            MemberKind.Method)
        {
            HasThis = true,
        });
        CallGraphMemberSelector flatGraph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            ImmutableArray<TypeRef>.Empty,
            flat,
            MemberKind.Method)
        {
            HasThis = true,
        });
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var nestedMember = new ApiMember
        {
            Name = "M",
            Kind = "method",
            ReturnType = "ref readonly Samples.Outer<int>.Inner<string>",
            MetadataToken = 0x06000001,
            SignatureModel = new ApiSignature
            {
                ReturnType = "ref readonly Samples.Outer<int>.Inner<string>",
                StructuralReturnType = StructuralTypeIdentity.Modified(
                    required: true,
                    "System.Runtime.InteropServices.InAttribute",
                    "Samples.Outer{System.Int32}.Inner{System.String}@"),
            },
        };
        var flatMember = new ApiMember
        {
            Name = "M",
            Kind = "method",
            ReturnType = "ref readonly Samples.Outer<int, string>",
            MetadataToken = 0x06000002,
            SignatureModel = new ApiSignature
            {
                ReturnType = "ref readonly Samples.Outer<int, string>",
                StructuralReturnType = StructuralTypeIdentity.Modified(
                    required: true,
                    "System.Runtime.InteropServices.InAttribute",
                    "Samples.Outer{System.Int32,System.String}@"),
            },
        };
        type.Members = [nestedMember, flatMember];

        Assert.NotEqual(nestedGraph.Key, flatGraph.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, nestedMember).Key,
            nestedGraph.Key);
        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, flatMember).Key,
            flatGraph.Key);
        Assert.Same(
            nestedMember,
            CallGraphMemberResolver.Resolve(type, nestedGraph.Name, nestedGraph.Key)!.Member);
        Assert.Same(
            flatMember,
            CallGraphMemberResolver.Resolve(type, flatGraph.Name, flatGraph.Key)!.Member);
    }

    [Fact]
    public void Selector_MatchesFunctionPointerOfGenericInstantiationAcrossProducers()
    {
        var signature = new MethodSignature<TypeRef>(
            new SignatureHeader(
                SignatureKind.Method,
                SignatureCallingConvention.Default,
                SignatureAttributes.None),
            TypeRef.CoreLib("System", "Void"),
            requiredParameterCount: 1,
            genericParameterCount: 0,
            [
                TypeRef.GenericInstance(
                    TypeRef.Definition("corelib", "System.Collections.Generic", "List`1"),
                    [TypeRef.MethodGenericParameter(0)]),
            ]);
        var graph = CallGraphMemberResolver.CreateSelector(new MemberRef(
            TypeRef.Definition("Samples", "Samples", "Owner"),
            "M",
            [TypeRef.UnsupportedFunctionPointer(signature)],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
            GenericArity = 1,
        });
        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var member = Method(
            "delegate*<System.Collections.Generic.List<T>, void>",
            StructuralTypeIdentity.FunctionPointer(
                SignatureCallingConvention.Default,
                hasThis: false,
                explicitThis: false,
                genericParameterCount: 0,
                requiredParameterCount: 1,
                ["System.Collections.Generic.List{M0}"],
                "System.Void"));
        member.MetadataToken = 0x06000001;
        member.SignatureModel!.TypeParameters = [new TypeParameter { Name = "T" }];
        type.Members = [member];

        Assert.Equal(
            CallGraphMemberResolver.CreateSelector(type, member).Key,
            graph.Key);
        Assert.Same(
            member,
            CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.Member);
    }

    [Fact]
    public void Selector_DoesNotInventStructureFromDisplayOrUnsupportedPayload()
    {
        var declaringType = TypeRef.Definition("Samples", "Samples", "Owner");
        CallGraphMemberSelector bareUnsupported = Graph(
            declaringType,
            TypeRef.Unsupported("function pointer"));
        CallGraphMemberSelector specified = Graph(
            declaringType,
            FunctionPointer(
                SignatureCallingConvention.CDecl,
                SignatureAttributes.None,
                requiredParameterCount: 1,
                genericParameterCount: 0));

        Assert.NotEqual(bareUnsupported.Key, specified.Key);

        var type = new ApiType { Namespace = "Samples", Name = "Owner" };
        var displayOnly = Method("delegate* unmanaged[Cdecl]<int, void>");
        displayOnly.MetadataToken = 0x06000001;
        type.Members = [displayOnly];

        Assert.NotEqual(
            specified.Key,
            CallGraphMemberResolver.CreateSelector(type, displayOnly).Key);
        Assert.Null(
            CallGraphMemberResolver.Resolve(type, specified.Name, specified.Key));
        Assert.Same(
            displayOnly,
            CallGraphMemberResolver.Resolve(
                type,
                displayOnly.Name,
                CallGraphMemberResolver.CreateSelector(type, displayOnly).Key)!
                .Member);
    }

    static CallGraphMemberSelector Graph(TypeRef declaringType, TypeRef parameter) =>
        CallGraphMemberResolver.CreateSelector(new MemberRef(
            declaringType,
            "M",
            [parameter],
            TypeRef.CoreLib("System", "Void"),
            MemberKind.Method)
        {
            HasThis = true,
        });

    static TypeRef FunctionPointer(
        SignatureCallingConvention convention,
        SignatureAttributes attributes,
        int requiredParameterCount,
        int genericParameterCount) =>
        TypeRef.UnsupportedFunctionPointer(
            new MethodSignature<TypeRef>(
                new SignatureHeader(SignatureKind.Method, convention, attributes),
                TypeRef.CoreLib("System", "Void"),
                requiredParameterCount,
                genericParameterCount,
                [TypeRef.CoreLib("System", "Int32")]));

    static void AssertCompiledSelectorAgreement(
        ApiType type,
        MetadataReader mdReader,
        string memberName)
        => AssertCompiledSelectorAgreement(
            type,
            mdReader,
            Assert.Single(type.Members, candidate => candidate.Name == memberName));

    static void AssertCompiledSelectorAgreement(
        ApiType type,
        MetadataReader mdReader,
        ApiMember member)
    {
        foreach (CallGraphMemberBodySelector selector in
            CallGraphMemberResolver.CreateBodySelectors(type, member))
        {
            MemberRef reference = MemberResolver.ResolveMethod(
                mdReader,
                MetadataTokens.EntityHandle(selector.BodyToken),
                GenericScope.Empty);
            CallGraphMemberSelector graph = CallGraphMemberResolver.CreateSelector(reference);
            Assert.Equal(graph.Name, selector.MemberName);
            Assert.Equal(graph.Key, selector.SelectorKey);
            Assert.Equal(
                selector.BodyToken,
                CallGraphMemberResolver.Resolve(type, graph.Name, graph.Key)!.BodyToken);
        }
    }

    static ApiMember Method(string parameterType, string? structuralType = null) => new()
    {
        Name = "M",
        Kind = "method",
        ReturnType = "void",
        SignatureModel = new ApiSignature
        {
            ReturnType = "void",
            Parameters =
            [
                new ApiParameter
                {
                    Name = "value",
                    Type = parameterType,
                    StructuralType = structuralType,
                },
            ],
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

public sealed class InitAccessorFixtures
{
    public int Value { get; init; }
}

public interface IExplicitAccessor
{
    int Value { get; }
}

public sealed class ExplicitAccessorFixtures : IExplicitAccessor
{
    int IExplicitAccessor.Value => 1;
}

public sealed class NestedGenericKeyFixtures
{
    public sealed class Outer<T>
    {
        public sealed class Inner<U>
        {
        }

        public sealed class Plain
        {
        }
    }

    public static void TakeNested(Outer<int>.Inner<string> value)
    {
    }

    public static void TakeRefNested(ref Outer<int>.Inner<string> value)
    {
    }

    public static void TakeRefPlain(ref Outer<int>.Plain value)
    {
    }

    public static void TakeList(List<int> value)
    {
    }

    public static void TakeRefInt(ref int value)
    {
    }

    public static void TakeListOfOuter(List<Outer<int>> value)
    {
    }

    public static void TakeListOfNested(List<Outer<int>.Inner<string>> value)
    {
    }

    public static void TakeWrapped(List<Outer<int>> value)
    {
    }

    public static void TakeWrapped(List<Outer<int>.Inner<string>> value)
    {
    }

    static void TakeHidden(HiddenOuter<int>.HiddenInner<string> value)
    {
    }

    static void TakeRefHidden(ref HiddenOuter<int>.HiddenInner<string> value)
    {
    }

    sealed class HiddenOuter<T>
    {
        public sealed class HiddenInner<U>
        {
        }
    }
}
