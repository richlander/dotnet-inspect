using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataDeclarationQueryTests
{
    static readonly PEReader PeReader;
    static readonly MetadataReader Reader;

    static MetadataDeclarationQueryTests()
    {
        PeReader = new PEReader(File.OpenRead(typeof(MetadataDeclarationQueryTests).Assembly.Location));
        Reader = PeReader.GetMetadataReader();
    }

    [Fact]
    public void MethodDeclaration_ExposesAccessibilityModifiersParametersAndReturnAttributes()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, "ProtectedVirtual");

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        Assert.Equal("protected", declaration.Accessibility);
        Assert.True(declaration.IsPublicOrProtected);
        Assert.True(declaration.IsVirtual);
        Assert.False(declaration.IsAbstract);
        Assert.Equal("string", declaration.Signature.ReturnType);
        Assert.Equal(["System.Diagnostics.CodeAnalysis.NotNull"], declaration.Signature.ReturnAttributes);
        Assert.Equal("value", declaration.Signature.Parameters[0].Name);
        Assert.Equal("string", declaration.Signature.Parameters[0].Type);
        Assert.Equal(
            ["System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)"],
            declaration.Signature.Parameters[0].Attributes);
        Assert.True(declaration.Signature.Parameters[1].HasDefault);
        Assert.Equal("2", declaration.Signature.Parameters[1].DefaultValueText);
    }

    [Fact]
    public void MethodDeclaration_ExposesAttributedDecimalDefaults()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, nameof(MetadataDeclarationQueryFixtures.DecimalDefault));

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        var parameter = Assert.Single(declaration.Signature.Parameters);
        Assert.Equal("System.Decimal", parameter.Type);
        Assert.True(parameter.HasDefault);
        Assert.Equal("5m", parameter.DefaultValueText);
    }

    [Fact]
    public void MethodDeclaration_SynthesizesParameterWhenParamRowIsAbsent()
    {
        string path = EmitMethodWithoutParamRow();
        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            var typeHandle = reader.TypeDefinitions.Single(handle =>
                reader.GetString(reader.GetTypeDefinition(handle).Name) == "MissingParamSample");
            var type = reader.GetTypeDefinition(typeHandle);
            var method = type.GetMethods()
                .Select(reader.GetMethodDefinition)
                .Single(candidate => reader.GetString(candidate.Name) == "Echo");

            Assert.Empty(method.GetParameters());

            var declaration = MetadataDeclarationQuery.GetMethod(reader, type, method);
            var parameter = Assert.Single(declaration.Signature.Parameters);

            Assert.Equal("arg0", parameter.Name);
            Assert.Equal("int", parameter.Type);
            Assert.Empty(parameter.Attributes);
            Assert.False(parameter.HasDefault);
            Assert.Null(parameter.DefaultValueText);
            Assert.Null(declaration.SignatureDecodeStatus);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PropertyDeclaration_ExposesAbstractAccessorShape()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.AbstractBase));
        var property = GetProperty(type, "Name");

        var declaration = MetadataDeclarationQuery.GetProperty(Reader, type, property);

        Assert.Equal("protected", declaration.Accessibility);
        Assert.True(declaration.IsAbstract);
        Assert.Equal("string", declaration.Signature.ReturnType);
        Assert.Equal(["get", "set"], declaration.Signature.Accessors.Select(accessor => accessor.Kind).ToArray());
    }

    [Fact]
    public void TypeSurface_IncludesNonPublicMembersWhenRequested()
    {
        var handle = GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures));

        var publicOnly = MetadataDeclarationQuery.GetTypeSurface(Reader, handle);
        var all = MetadataDeclarationQuery.GetTypeSurface(Reader, handle, includeNonPublicMembers: true);

        Assert.DoesNotContain(publicOnly.Members, member => member.Name == "ProtectedVirtual");
        var method = Assert.Single(all.Members, member => member.Name == "ProtectedVirtual");
        Assert.Equal("protected", method.Accessibility);
        Assert.True(method.IsVirtual);

        var field = Assert.Single(all.Members, member => member.Name == "_count");
        Assert.Equal("private", field.Accessibility);
        Assert.Equal("int", field.ReturnType);
    }

    [Fact]
    public void TypeSurface_RecordsExecutableBodyPresence()
    {
        var concrete = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        var abstractType = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.AbstractBase)),
            includeNonPublicMembers: true);

        Assert.True(Assert.Single(concrete.Members, member => member.Name == "Count").HasMethodBody);
        Assert.True(Assert.Single(concrete.Members, member => member.Name == "while").HasMethodBody);
        Assert.False(Assert.Single(abstractType.Members, member => member.Name == "Name").HasMethodBody);
    }

    [Fact]
    public void TypeSurface_AccessorFactsPreserveDistinctAccessibility()
    {
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        var property = Assert.Single(
            surface.Members,
            member => member.Name == nameof(MetadataDeclarationQueryFixtures.Restricted));

        Assert.Equal(
            "private",
            Assert.Single(property.AccessorFacts, accessor => accessor.Kind == "set")
                .Accessibility);
        Assert.Equal(
            "private",
            Assert.Single(property.SignatureModel!.Accessors, accessor => accessor.Kind == "set")
                .Accessibility);
    }

    [Fact]
    public void PropertyDeclarationAndTypeSurface_PreserveInitSetterStructuralReturn()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var property = GetProperty(type, nameof(MetadataDeclarationQueryFixtures.InitValue));
        var declaration = MetadataDeclarationQuery.GetProperty(Reader, type, property);
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        string structuralReturn = StructuralTypeIdentity.Modified(
            required: true,
            "System.Runtime.CompilerServices.IsExternalInit",
            "System.Void");

        Assert.Equal(
            structuralReturn,
            declaration.Signature.Accessors.Single(accessor => accessor.Kind is "set" or "init")
                .StructuralReturnType);
        Assert.Equal(
            structuralReturn,
            surface.Members.Single(member => member.Name == nameof(MetadataDeclarationQueryFixtures.InitValue))
                .AccessorFacts.Single(accessor => accessor.Kind is "set" or "init")
                .StructuralReturnType);
    }

    [Fact]
    public void TypeSurface_RecordsUnsafeBodylessSignatures()
    {
        var interfaceSurface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.IUnsafeSurface)));
        var classSurface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.UnsafeFields)));

        var method = Assert.Single(interfaceSurface.Members, member => member.Name == "Consume");

        Assert.False(method.HasMethodBody);
        Assert.True(method.IsUnsafe);
        Assert.True(Assert.Single(classSurface.Members, member => member.Name == "Pointer").IsUnsafe);
        Assert.True(Assert.Single(classSurface.Members, member => member.Name == "Value").IsUnsafe);
        var changed = Assert.Single(classSurface.Members, member => member.Name == "Changed");
        Assert.Equal("event", changed.Kind);
        Assert.True(changed.HasMethodBody);
        Assert.DoesNotContain(classSurface.Members, member => member.Name is "add_Changed" or "remove_Changed");
    }

    [Fact]
    public void TypeSurface_PreservesEventRaiserBodies()
    {
        string path = typeof(VbCustomEventFixture.CustomEvents).Assembly.Location;
        using var peReader = new PEReader(File.OpenRead(path));
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(
            reader,
            typeof(VbCustomEventFixture.CustomEvents).FullName!);

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            type => type.FullName == typeof(VbCustomEventFixture.CustomEvents).FullName);

        foreach (var surface in new[] { queried, extracted })
        {
            Assert.Single(surface.Members, member => member is { Name: "Changed", Kind: "event" });
            Assert.DoesNotContain(
                surface.Members,
                member => member.Name is "add_Changed" or "remove_Changed");
            Assert.Contains(
                surface.Members,
                member => member is
                {
                    Name: "raise_Changed",
                    Kind: "method",
                    HasMethodBody: true
                });
        }

        var typeDefinition = reader.GetTypeDefinition(typeHandle);
        var raiser = typeDefinition.GetMethods()
            .Select(reader.GetMethodDefinition)
            .Single(method => reader.GetString(method.Name) == "raise_Changed");
        Assert.NotEmpty(peReader.GetMethodBody(raiser.RelativeVirtualAddress).ExceptionRegions);
    }

    [Fact]
    public void TypeSurface_PreservesMetadataEventOtherAccessors()
    {
        string path = EmitEventWithOtherAccessor();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "EventOtherSample");

            var queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
                type => type.FullName == "EventOtherSample");

            foreach (var surface in new[] { queried, extracted })
            {
                Assert.Single(surface.Members, member => member is { Name: "Changed", Kind: "event" });
                Assert.Contains(
                    surface.Members,
                    member => member is
                    {
                        Name: "other_Changed",
                        Kind: "method",
                        HasMethodBody: true
                    });
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_PreservesRemoveOnlyEvents()
    {
        string path = EmitRemoveOnlyEvent();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "RemoveOnlyEventSample");
            var queried = MetadataDeclarationQuery.GetTypeSurface(reader, typeHandle);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader).Types,
                type => type.FullName == "RemoveOnlyEventSample");

            foreach (var surface in new[] { queried, extracted })
            {
                var evt = Assert.Single(surface.Members, member => member.Name == "Changed");
                Assert.Null(evt.AdderToken);
                Assert.NotNull(evt.RemoverToken);
                Assert.Single(evt.AccessorFacts, accessor => accessor.Kind == "remove");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_PreservesUnassociatedAccessorLikeMethods()
    {
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)),
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
            type => type.FullName == typeof(MetadataDeclarationQueryFixtures).FullName);

        foreach (var surface in new[] { queried, extracted })
            Assert.Single(surface.Members, member => member is { Name: "get_Orphan", Kind: "method" });
    }

    [Fact]
    public void TypeSurface_FoldsCompilerProducedExtensionPropertyAndIndexerImplementations()
    {
        using var stream = File.OpenRead(typeof(ExtensionPropertyIdentityFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(
            reader,
            typeof(ExtensionPropertyIdentityFixture).FullName!);
        var queried = MetadataDeclarationQuery.GetTypeSurface(reader, typeHandle);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader).Types,
            type => type.FullName == typeof(ExtensionPropertyIdentityFixture).FullName);

        using var summaryStream =
            File.OpenRead(typeof(ExtensionPropertyIdentityFixture).Assembly.Location);
        using var summaryReader = new PEReader(summaryStream);
        var summarized = Assert.Single(
            ApiSurfaceExtractor.ExtractSummary(summaryReader).Types,
            type => type.FullName == typeof(ExtensionPropertyIdentityFixture).FullName);

        foreach (var surface in new[] { queried, extracted, summarized })
        {
            Assert.Equal(
                3,
                surface.Members.Count(member => member.Kind == "method"));
            Assert.Contains(
                surface.Members,
                member => member is { Name: "get_Standalone", Kind: "method" });
            Assert.Contains(
                surface.Members,
                member => member is { Name: "set_Standalone", Kind: "method" });
            Assert.Contains(
                surface.Members,
                member => member is { Name: "get_Scoped", Kind: "method" });
            Assert.DoesNotContain(
                surface.Members,
                member => member.Kind == "method"
                    && member.Name is "get_HasValue"
                        or "get_Item"
                        or "get_StaticValue"
                        or "get_Capacity"
                        or "set_Capacity");
        }
    }

    [Fact]
    public void ExtensionIndexerImplementationMatching_DecodesCandidatesLinearly()
    {
        using var stream = File.OpenRead(
            typeof(ExtensionIndexerScalingFixture).Assembly.Location);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = GetTypeDefinitionHandle(
            reader,
            typeof(ExtensionIndexerScalingFixture).FullName!);
        int observedWork = 0;

        HashSet<MethodDefinitionHandle> implementations =
            ExtensionMethodScanner
                .GetDeclaredExtensionPropertyImplementationMethods(
                    reader,
                    reader.GetTypeDefinition(typeHandle),
                    _ => observedWork++);

        Assert.Equal(16, implementations.Count);
        Assert.True(
            observedWork < 200,
            $"Extension indexer matching performed {observedWork} materialization steps.");
    }

    [Fact]
    public void TypeSurface_PreservesQualifiedMethodImplAccessors()
    {
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures.ExplicitSurface)),
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
            type => type.FullName.EndsWith(
                "." + nameof(MetadataDeclarationQueryFixtures.ExplicitSurface),
                StringComparison.Ordinal));

        Assert.Contains(
            queried.Interfaces,
            interfaceName => interfaceName.EndsWith(
                ".IExplicitSurface",
                StringComparison.Ordinal));
        Assert.Contains(
            queried.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(".get_Value", StringComparison.Ordinal));
        Assert.Contains(
            queried.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(".add_Changed", StringComparison.Ordinal));
        Assert.Contains(
            queried.Members,
            member => member is
            {
                Kind: "property",
                IsExplicitInterfaceImplementation: true
            } && member.Name.EndsWith(".Value", StringComparison.Ordinal));
        Assert.Contains(
            queried.Members,
            member => member is
            {
                Kind: "event",
                IsExplicitInterfaceImplementation: true
            } && member.Name.EndsWith(".Changed", StringComparison.Ordinal));
        var queriedEvent = Assert.Single(
            queried.Members,
            member => member is
            {
                Kind: "event",
                IsExplicitInterfaceImplementation: true
            } && member.Name.EndsWith(".Changed", StringComparison.Ordinal));
        Assert.Equal(
            ["System.Diagnostics.CodeAnalysis.NotNull"],
            queriedEvent.SignatureModel!.Accessors[0].ReturnAttributes);
        Assert.Equal(
            ["System.Diagnostics.CodeAnalysis.MaybeNull"],
            queriedEvent.SignatureModel.Accessors[1].ReturnAttributes);
        Assert.Contains(
            extracted.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(".get_Value", StringComparison.Ordinal));
        Assert.Contains(
            extracted.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(".add_Changed", StringComparison.Ordinal));

        var property = Assert.Single(
            queried.Members,
            member => member.Kind == "property"
                && member.Name.EndsWith(".Value", StringComparison.Ordinal));
        var signatureAccessor = Assert.Single(property.SignatureModel!.Accessors);
        var factAccessor = Assert.Single(property.AccessorFacts);
        Assert.Null(signatureAccessor.MethodName);
        Assert.EndsWith(".get_Value", factAccessor.MethodName, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitAggregateIdentity_RequiresEveryAccessorToTargetAnInterface()
    {
        var typeDef = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.ExplicitSurface));
        var eventHandle = Assert.Single(typeDef.GetEvents());
        var eventDefinition = Reader.GetEventDefinition(eventHandle);
        var accessors = eventDefinition.GetAccessors();
        string eventName = Reader.GetString(eventDefinition.Name);
        var interfaceTargets =
            ApiSurfaceExtractor.GetExplicitInterfaceImplementationTargets(Reader, typeDef);
        var adderTarget = Assert.Single(interfaceTargets[accessors.Adder]);
        var removerTarget = Assert.Single(interfaceTargets[accessors.Remover]);

        Assert.True(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            eventName,
            interfaceTargets,
            (accessors.Adder, "add_"),
            (accessors.Remover, "remove_")));
        Assert.False(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            eventName,
            new Dictionary<MethodDefinitionHandle, List<ExplicitInterfaceMethodTarget>>
            {
                [accessors.Adder] = [adderTarget]
            },
            (accessors.Adder, "add_"),
            (accessors.Remover, "remove_")));
        int separator = eventName.LastIndexOf('.');
        Assert.False(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            eventName[..(separator + 1)] + "Other",
            interfaceTargets,
            (accessors.Adder, "add_"),
            (accessors.Remover, "remove_")));
        Assert.False(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            eventName,
            new Dictionary<MethodDefinitionHandle, List<ExplicitInterfaceMethodTarget>>
            {
                [accessors.Adder] = [adderTarget],
                [accessors.Remover] =
                [
                    removerTarget with
                    {
                        InterfaceType = new ExplicitInterfaceTypeIdentity(
                            "Other.IExplicitSurface",
                            "Other.IExplicitSurface")
                    }
                ]
            },
            (accessors.Adder, "add_"),
            (accessors.Remover, "remove_")));

        Assert.False(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            eventName,
            new Dictionary<MethodDefinitionHandle, List<ExplicitInterfaceMethodTarget>>
            {
                [accessors.Adder] =
                [
                    adderTarget with
                    {
                        InterfaceType = adderTarget.InterfaceType with
                        {
                            Key = "assembly-a"
                        }
                    }
                ],
                [accessors.Remover] =
                [
                    removerTarget with
                    {
                        InterfaceType = removerTarget.InterfaceType with
                        {
                            Key = "assembly-b"
                        }
                    }
                ]
            },
            (accessors.Adder, "add_"),
            (accessors.Remover, "remove_")));
    }

    [Fact]
    public void ExplicitInterfaceTypeIdentity_DistinguishesArrayShapesAndAssemblyEquivalence()
    {
        var provider = new ExplicitInterfaceTypeIdentityProvider();
        var element = provider.GetPrimitiveType(PrimitiveTypeCode.Int32);
        var vector = provider.GetSZArrayType(element);
        var variableBound = provider.GetArrayType(
            element,
            new ArrayShape(1, [], []));
        var sized = provider.GetArrayType(
            element,
            new ArrayShape(1, [4], [1]));

        Assert.NotEqual(vector.Key, variableBound.Key);
        Assert.NotEqual(variableBound.Key, sized.Key);
        Assert.False(vector.IsInterface);
        Assert.False(variableBound.IsInterface);
        Assert.False(provider.GetPointerType(element).IsInterface);
        Assert.False(provider.GetByReferenceType(element).IsInterface);
        Assert.Equal(
            ExplicitInterfaceTypeIdentityProvider.AssemblyKey(
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    "neutral",
                    "AABBCC")),
            ExplicitInterfaceTypeIdentityProvider.AssemblyKey(
                new AssemblyReferenceIdentity(
                    "dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    "aabbcc")));

        var invalidVariable = provider.GetGenericTypeParameter(
            new ExplicitInterfaceSignatureContext(
                Names: null,
                TypeArguments: default,
                TypeParameterCount: 0),
            index: 0);
        Assert.True(invalidVariable.IsDegraded);

        var nullable = provider.GetGenericInstantiation(
            new ExplicitInterfaceTypeIdentity(
                "nullable",
                "System.Nullable",
                GenericArity: 1,
                IsWellKnownNullable: true),
            [element]);
        var nullableInterface = provider.GetGenericInstantiation(
            new ExplicitInterfaceTypeIdentity(
                "interface",
                "Samples.IContract",
                GenericArity: 1),
            [nullable]);
        Assert.Equal("Samples.IContract<System.Int32?>", nullableInterface.AggregateAliasName);
        var spoofNullable = provider.GetGenericInstantiation(
            new ExplicitInterfaceTypeIdentity(
                "spoof-nullable",
                "System.Nullable`1",
                GenericArity: 1),
            [element]);
        Assert.Null(spoofNullable.AggregateAliasName);
        Assert.Throws<BadImageFormatException>(() =>
            provider.GetGenericInstantiation(
                new ExplicitInterfaceTypeIdentity(
                    "generic",
                    "Samples.IContract`1",
                    GenericArity: 1),
                [element, element]));
        Assert.False(provider.GetGenericInstantiation(
            new ExplicitInterfaceTypeIdentity(
                "class",
                "Samples.NotAnInterface`1",
                GenericArity: 1,
                IsInterface: false),
            [element]).IsInterface);
        var constructed = provider.GetGenericInstantiation(
            new ExplicitInterfaceTypeIdentity(
                "definition",
                "Samples.IContract`1",
                GenericArity: 1,
                IsInterface: true),
            [element]);
        Assert.True(constructed.IsConstructedGeneric);
        Assert.Throws<BadImageFormatException>(
            () => provider.GetGenericInstantiation(constructed, [element]));

        Assert.NotNull(typeof(Dictionary<,>.KeyCollection));
        var nestedReference = PeReader.GetMetadataReader()
            .TypeReferences
            .Single(handle =>
                PeReader.GetMetadataReader().GetString(
                    PeReader.GetMetadataReader().GetTypeReference(handle).Name)
                    == "KeyCollection");
        Assert.Equal(
            2,
            provider.FromHandle(
                PeReader.GetMetadataReader(),
                nestedReference,
                context: null).GenericArity);
    }

    [Fact]
    public void ExplicitInterfaceTypeSpecCache_DistinguishesSubstitutedContexts()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        signature.WriteByte(0x13); // ELEMENT_TYPE_VAR
        signature.WriteCompressedInteger(0);
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(signature));
        using var peReader = new PEReader(
            new MemoryStream(Serialize(metadata), writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        var provider = new ExplicitInterfaceTypeIdentityProvider();
        var intType = provider.GetPrimitiveType(PrimitiveTypeCode.Int32);
        var stringType = provider.GetPrimitiveType(PrimitiveTypeCode.String);

        ExplicitInterfaceTypeIdentity first = provider.GetTypeFromSpecification(
            reader,
            new ExplicitInterfaceSignatureContext(null, [intType], 1),
            typeSpec,
            rawTypeKind: 0x12);
        ExplicitInterfaceTypeIdentity second = provider.GetTypeFromSpecification(
            reader,
            new ExplicitInterfaceSignatureContext(null, [stringType], 1),
            typeSpec,
            rawTypeKind: 0x12);

        Assert.Equal("System.Int32", first.MetadataName);
        Assert.Equal("System.String", second.MetadataName);
        Assert.NotEqual(first.Key, second.Key);
    }

    [Fact]
    public void ExplicitInterfaceTypeIdentity_RejectsNoncanonicalTypeRefArity()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Synthetic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle assemblyReference =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("Contracts"),
                new Version(1, 0, 0, 0),
                default,
                default,
                default,
                default);
        metadata.AddTypeReference(
            assemblyReference,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("I`01"));
        var signature = new BlobBuilder();
        signature.WriteByte(0x15); // ELEMENT_TYPE_GENERICINST
        signature.WriteByte(0x12); // ELEMENT_TYPE_CLASS
        signature.WriteByte(0x05); // TypeRef row 1
        signature.WriteCompressedInteger(1);
        signature.WriteByte(0x08); // ELEMENT_TYPE_I4
        TypeSpecificationHandle typeSpec = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(signature));
        using var peReader = new PEReader(
            new MemoryStream(Serialize(metadata), writable: false));

        Assert.Throws<BadImageFormatException>(
            () => new ExplicitInterfaceTypeIdentityProvider()
                .GetTypeFromSpecification(
                    peReader.GetMetadataReader(),
                    ExplicitInterfaceSignatureContext.Open(names: null),
                    typeSpec,
                    rawTypeKind: 0x12));
    }

    [Fact]
    public void ExplicitInterfaceTypeIdentity_RejectsOversizedAssemblyIdentityBlob()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Synthetic.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Synthetic"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var assemblyReference = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Dependency"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(
                new byte[ExplicitInterfaceTypeIdentityProvider.MaxAssemblyIdentityBlobBytes + 1]),
            flags: default,
            hashValue: default);
        var typeReference = metadata.AddTypeReference(
            assemblyReference,
            metadata.GetOrAddString("Samples"),
            metadata.GetOrAddString("IContract"));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        using var peReader = new PEReader(new MemoryStream(image.ToArray()));

        Assert.Throws<BadImageFormatException>(() =>
            new ExplicitInterfaceTypeIdentityProvider().FromHandle(
                peReader.GetMetadataReader(),
                typeReference,
                context: null));
    }

    [Fact]
    public void ExplicitAggregateIdentity_AcceptsIndexerMetadataAlias()
    {
        var accessor = MetadataTokens.MethodDefinitionHandle(1);
        var interfaceType = new ExplicitInterfaceTypeIdentity(
            "ilist",
            "System.Collections.IList");

        Assert.True(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            "System.Collections.IList.this[]",
            new Dictionary<MethodDefinitionHandle, List<ExplicitInterfaceMethodTarget>>
            {
                [accessor] =
                [
                    new ExplicitInterfaceMethodTarget(
                        default,
                        interfaceType,
                        "get_Item")
                ]
            },
            (accessor, "get_")));

        Assert.True(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
            "Alias::System.Collections.IList.this[]",
            new Dictionary<MethodDefinitionHandle, List<ExplicitInterfaceMethodTarget>>
            {
                [accessor] =
                [
                    new ExplicitInterfaceMethodTarget(
                        default,
                        interfaceType,
                        "get_Item")
                ]
            },
            (accessor, "get_")));
    }

    [Fact]
    public void TypeSurface_ClassifiesNativeIntegerExplicitInterfaceAggregates()
    {
        using var peReader = new PEReader(File.OpenRead(typeof(nint).Assembly.Location));
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(reader, "System.IntPtr");

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);

        Assert.Contains(
            queried.Members,
            member => member.IsExplicitInterfaceImplementation
                && member.Name.Contains("IMinMaxValue<nint>.MinValue", StringComparison.Ordinal));
    }

    [Fact]
    public void TypeSurface_ClassifiesNullableExplicitInterfaceAggregates()
    {
        var typeHandle = GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.NullableExplicitAggregateMetadataFixture));
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            typeHandle,
            includeNonPublicMembers: true);

        var aggregates = queried.Members.Where(member =>
                member.IsExplicitInterfaceImplementation
                && member.Kind is "property" or "event").ToArray();

        Assert.Equal(2, aggregates.Length);
        Assert.All(
            aggregates,
            member => Assert.Contains("System.Int32?", member.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void MetadataAccessorSemantics_DistinguishesInitAndRetainsMixedAbstraction()
    {
        var fixture = Reader.GetTypeDefinition(GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.NullableExplicitAggregateMetadataFixture)));
        var contract = Reader.GetTypeDefinition(GetTypeDefinitionHandle(
            typeof(MetadataDeclarationQueryFixtures.INullableExplicitAggregateMetadataFixture<>)));
        var ordinarySetter = fixture.GetMethods().Single(handle =>
            Reader.GetString(Reader.GetMethodDefinition(handle).Name) == "set_Ordinary");
        var initSetter = fixture.GetMethods().Single(handle =>
            Reader.GetString(Reader.GetMethodDefinition(handle).Name) == "set_Initial");
        var abstractGetter = contract.GetMethods().Single(handle =>
            Reader.GetString(Reader.GetMethodDefinition(handle).Name) == "get_Value");

        Assert.Equal("set", MetadataAccessorSemantics.Kind(Reader, ordinarySetter, "set"));
        Assert.Equal("init", MetadataAccessorSemantics.Kind(Reader, initSetter, "set"));
        Assert.False(MetadataAccessorSemantics.AreAllAbstract(Reader, ordinarySetter));
        Assert.True(MetadataAccessorSemantics.AreAllAbstract(Reader, abstractGetter));
        Assert.False(MetadataAccessorSemantics.AreAllAbstract(
            Reader,
            abstractGetter,
            ordinarySetter));
    }

    [Fact]
    public void MetadataAccessorSemantics_RejectsNestedAndMalformedInitModifiers()
    {
        using (var peReader = new PEReader(new MemoryStream(
                   BuildHostileInitModifierImage(cyclicScope: false))))
        {
            var reader = peReader.GetMetadataReader();
            Assert.Equal(
                "set",
                MetadataAccessorSemantics.Kind(
                    reader,
                    MetadataTokens.MethodDefinitionHandle(1),
                    "set"));
        }

        using var cyclicReader = new PEReader(new MemoryStream(
            BuildHostileInitModifierImage(cyclicScope: true)));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataAccessorSemantics.Kind(
                cyclicReader.GetMetadataReader(),
                MetadataTokens.MethodDefinitionHandle(1),
                "set"));

        using var typeSpecReader = new PEReader(new MemoryStream(
            BuildHostileInitModifierImage(
                cyclicScope: false,
                typeSpecModifier: true)));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataAccessorSemantics.Kind(
                typeSpecReader.GetMetadataReader(),
                MetadataTokens.MethodDefinitionHandle(1),
                "set"));
    }

    [Fact]
    public void MetadataAccessorSemantics_BoundsAndChargesInitModifierNames()
    {
        using (var chargedReader = new PEReader(new MemoryStream(
                   BuildHostileInitModifierImage(
                       cyclicScope: false,
                       nestedModifier: false,
                       modifierNamespace:
                           "System.Runtime.CompilerServices",
                       modifierName: "IsExternalInit"))))
        {
            int decodeWork = 0;
            string kind = MetadataAccessorSemantics.Kind(
                chargedReader.GetMetadataReader(),
                MetadataTokens.MethodDefinitionHandle(1),
                "set",
                amount => decodeWork += amount);

            Assert.Equal("init", kind);
            Assert.True(decodeWork > 0);
        }

        using var oversizedReader = new PEReader(new MemoryStream(
            BuildHostileInitModifierImage(
                cyclicScope: false,
                nestedModifier: false,
                modifierNamespace: "Contracts",
                modifierName: new string(
                    'M',
                    MetadataSafetyPolicy.MaxTypeNameCharacters + 1))));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataAccessorSemantics.Kind(
                oversizedReader.GetMetadataReader(),
                MetadataTokens.MethodDefinitionHandle(1),
                "set",
                _ => throw new Xunit.Sdk.XunitException(
                    "Oversized names must fail before decode work is credited.")));
    }

    [Fact]
    public void TypeSurface_RejectsContextInvalidTypeVisibility()
    {
        using var peReader = new PEReader(new MemoryStream(
            BuildContextInvalidVisibilityImage()));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle invalid =
            MetadataTokens.TypeDefinitionHandle(2);

        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeSurface(
                reader,
                invalid,
                includeNonPublicMembers: true));

        ApiSurface full =
            ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        Assert.DoesNotContain(
            full.Types,
            type => type.Name == "InvalidTopLevel");
        Assert.Contains(
            full.InspectionFailures,
            failure => failure.Detail.Contains(
                "top-level type has nested visibility metadata",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitAggregateIdentity_RejectsBaseClassMethodImplTargets()
    {
        string path = EmitBaseClassMethodImplEvent();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "DerivedEvent");
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var eventHandle = Assert.Single(typeDef.GetEvents());
            var eventDefinition = reader.GetEventDefinition(eventHandle);
            var accessors = eventDefinition.GetAccessors();

            Assert.False(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
                reader.GetString(eventDefinition.Name),
                ApiSurfaceExtractor.GetExplicitInterfaceImplementationTargets(reader, typeDef),
                (accessors.Adder, "add_"),
                (accessors.Remover, "remove_")));

            ApiType extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader).Types,
                candidate => candidate.Name == "DerivedEvent");
            Assert.DoesNotContain(
                extracted.Members,
                member => member.Name is "add_BaseChanged" or "remove_BaseChanged"
                    || member.Kind == "explicit-interface-implementation");

            ApiType queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: false);
            Assert.DoesNotContain(
                queried.Members,
                member => member.Name is "add_BaseChanged" or "remove_BaseChanged");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A class or struct aggregate whose accessors disagree about abstractness has no legal C#
    /// spelling: abstractness belongs to the member, not the accessor. Projecting it as the
    /// concrete aggregate the majority of its accessors suggest would invent a member the
    /// metadata does not declare, so both projections reject it and say so.
    /// </summary>
    [Fact]
    public void MixedAbstractionAggregates_FailVisibly()
    {
        string path = EmitMixedAbstractionAggregates();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var mixed = Assert.Single(surface.Types, type => type.Name == "MixedAbstraction");

            Assert.DoesNotContain(
                mixed.Members,
                member => member.Kind is "property" or "event");
            Assert.Contains(
                surface.InspectionFailures,
                failure => failure.Operation == "property modifiers");
            Assert.Contains(
                surface.InspectionFailures,
                failure => failure.Operation == "event modifiers");
            ApiSurface publicSurface = ApiSurfaceExtractor.Extract(peReader);
            Assert.Contains(
                publicSurface.InspectionFailures,
                failure => failure.Operation == "event modifiers");
            ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(peReader);
            ApiType summarized = Assert.Single(
                summary.Types,
                type => type.Name == "MixedAbstraction");
            Assert.DoesNotContain(
                summarized.Members,
                member => member.Kind is "property" or "event");
            Assert.Contains(
                summary.InspectionFailures,
                failure => failure.Operation == "property modifiers");
            Assert.Contains(
                summary.InspectionFailures,
                failure => failure.Operation == "event modifiers");
            Assert.Throws<BadImageFormatException>(() =>
                MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    GetTypeDefinitionHandle(reader, "MixedAbstraction"),
                    includeNonPublicMembers: true));

            // The close negatives: uniform abstractness either way is ordinary metadata and
            // must keep projecting, or the guard has eaten the shape it exists to protect.
            foreach (string uniform in new[] { "UniformAbstract", "UniformConcrete" })
            {
                var extracted = Assert.Single(surface.Types, type => type.Name == uniform);
                Assert.Contains(
                    extracted.Members,
                    member => member.Kind == "property" && member.Name == "Value");
                Assert.Contains(
                    extracted.Members,
                    member => member.Kind == "event" && member.Name == "Changed");
                var queried = MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    GetTypeDefinitionHandle(reader, uniform),
                    includeNonPublicMembers: true);
                Assert.Contains(
                    queried.Members,
                    member => member.Kind == "property" && member.Name == "Value");
                Assert.Contains(
                    queried.Members,
                    member => member.Kind == "event" && member.Name == "Changed");
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// An interface aggregate legitimately mixes an abstract accessor with a defaulted one
    /// (C# 8 default interface members), so the uniformity requirement applies to classes and
    /// structs only. C# has no syntax for the mixed property — <c>{ get; set { } }</c> is read
    /// as an auto-property — so the shape is emitted, and the compiler-produced peer below
    /// covers the uniformly abstract interface it must not be confused with.
    /// </summary>
    [Fact]
    public void MixedAbstractionInterfaceAggregates_AreRetained()
    {
        string path = EmitMixedAbstractionAggregates();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var extracted = Assert.Single(
                surface.Types,
                type => type.Name == "MixedAbstractionInterface");

            Assert.Contains(
                extracted.Members,
                member => member.Kind == "property" && member.Name == "Value");

            var queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                GetTypeDefinitionHandle(reader, "MixedAbstractionInterface"),
                includeNonPublicMembers: true);
            Assert.Contains(
                queried.Members,
                member => member.Kind == "property" && member.Name == "Value");
        }
        finally
        {
            File.Delete(path);
        }

        var uniformInterface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.IMixedAbstractionSurface)),
            includeNonPublicMembers: true);
        Assert.Contains(
            uniformInterface.Members,
            member => member.Kind == "property" && member.Name == "Value");
        Assert.Contains(
            uniformInterface.Members,
            member => member.Kind == "event" && member.Name == "Changed");
    }

    /// <summary>
    /// ECMA-335 II.15.4.2.4 forbids an abstract method from declaring a body, so an accessor
    /// that claims both is malformed whatever its declaring type. The image is synthetic
    /// because no compiler emits the shape.
    /// </summary>
    [Fact]
    public void AbstractAccessorWithBody_FailsVisibly()
    {
        byte[] image = BuildAbstractAccessorWithBodyImage();
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var extracted = Assert.Single(surface.Types, type => type.Name == "AbstractBody");

        Assert.DoesNotContain(extracted.Members, member => member.Kind == "property");
        var failure = Assert.Single(
            surface.InspectionFailures,
            candidate => candidate.Operation == "property modifiers");
        Assert.Contains("abstract accessor", failure.Detail, StringComparison.Ordinal);
        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(peReader);
        ApiType summarized = Assert.Single(
            summary.Types,
            type => type.Name == "AbstractBody");
        Assert.DoesNotContain(
            summarized.Members,
            member => member.Kind == "property");
        Assert.Contains(
            summary.InspectionFailures,
            candidate => candidate.Operation == "property modifiers"
                && candidate.Detail.Contains(
                    "abstract accessor",
                    StringComparison.Ordinal));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeSurface(
                reader,
                GetTypeDefinitionHandle(reader, "Fixtures.AbstractBody"),
                includeNonPublicMembers: false));
        Assert.Throws<BadImageFormatException>(() =>
            MetadataDeclarationQuery.GetTypeSurface(
                reader,
                GetTypeDefinitionHandle(reader, "Fixtures.AbstractBody"),
                includeNonPublicMembers: true));
    }

    [Fact]
    public void AbstractNonVirtualAccessors_FailVisibly()
    {
        string path = EmitAbstractNonVirtualProperty();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            MetadataReader reader = peReader.GetMetadataReader();
            ApiSurface full =
                ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            ApiType extracted = Assert.Single(
                full.Types,
                type => type.Name == "AbstractNonVirtual");

            Assert.DoesNotContain(
                extracted.Members,
                member => member.Kind == "property");
            Assert.Contains(
                full.InspectionFailures,
                failure => failure.Operation == "property modifiers"
                    && failure.Detail.Contains(
                        "abstract accessor that is not virtual",
                        StringComparison.Ordinal));

            ApiSurface summary =
                ApiSurfaceExtractor.ExtractSummary(peReader);
            Assert.Contains(
                summary.InspectionFailures,
                failure => failure.Operation == "property modifiers"
                    && failure.Detail.Contains(
                        "abstract accessor that is not virtual",
                        StringComparison.Ordinal));

            Assert.Throws<BadImageFormatException>(() =>
                MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    GetTypeDefinitionHandle(
                        reader,
                        "AbstractNonVirtual"),
                    includeNonPublicMembers: true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Explicit aggregate classification matches accessor names against the MethodImpl target
    /// map, and the MethodImpl check proves each accessor body implements the interface method
    /// it names. Neither reads the Property or Event row itself, so the row's own signature has
    /// to be validated against the accessors or a hostile image renders an explicit member no
    /// accessor implements.
    /// </summary>
    [Fact]
    public void ExplicitAggregateRowSignature_MustMatchAccessors()
    {
        string path = EmitRowSignatureMismatchedExplicitAggregates();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

            var mismatched = Assert.Single(
                surface.Types,
                type => type.Name == "RowSignatureMismatch");
            Assert.DoesNotContain(
                mismatched.Members,
                member => member.Kind is "property" or "event");
            Assert.Contains(
                surface.InspectionFailures,
                failure => failure.Operation == "property signature");
            Assert.Contains(
                surface.InspectionFailures,
                failure => failure.Operation == "event signature");
            Assert.Throws<BadImageFormatException>(() =>
                MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    GetTypeDefinitionHandle(reader, "RowSignatureMismatch"),
                    includeNonPublicMembers: true));

            // The close negative differs only in that its rows tell the truth.
            var truthful = Assert.Single(
                surface.Types,
                type => type.Name == "RowSignatureMatch");
            Assert.Contains(
                truthful.Members,
                member => member.Kind == "property" && member.IsExplicitInterfaceImplementation);
            Assert.Contains(
                truthful.Members,
                member => member.Kind == "event" && member.IsExplicitInterfaceImplementation);
            var queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                GetTypeDefinitionHandle(reader, "RowSignatureMatch"),
                includeNonPublicMembers: true);
            Assert.Contains(
                queried.Members,
                member => member.Kind == "property" && member.IsExplicitInterfaceImplementation);
            Assert.Contains(
                queried.Members,
                member => member.Kind == "event" && member.IsExplicitInterfaceImplementation);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ExplicitAggregateRowSignature_RejectsModifiedVoidAccessorReturns()
    {
        byte[] privateImage =
            BuildModifiedVoidExplicitAggregateImage(MethodAttributes.Private);
        using var stream = new MemoryStream(privateImage, writable: false);
        using var peReader = new PEReader(stream);

        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType host = Assert.Single(surface.Types, type => type.Name == "Host");
        Assert.DoesNotContain(host.Members, member => member.Kind is "property" or "event");
        Assert.Equal(
            2,
            surface.InspectionFailures.Count(failure =>
                failure.Operation == "property signature"
                && failure.Detail == "The property setter does not return void."));
        Assert.Equal(
            2,
            surface.InspectionFailures.Count(failure =>
                failure.Operation == "event signature"
                && failure.Detail == "The event adder does not return void."));

        byte[] publicImage =
            BuildModifiedVoidExplicitAggregateImage(MethodAttributes.Public);
        using var publicStream = new MemoryStream(publicImage, writable: false);
        using var publicReader = new PEReader(publicStream);
        ApiSurface publicSurface = ApiSurfaceExtractor.Extract(publicReader);
        using var summaryStream = new MemoryStream(publicImage, writable: false);
        using var summaryReader = new PEReader(summaryStream);
        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(summaryReader);
        ApiType summaryHost = Assert.Single(summary.Types, type => type.Name == "Host");
        Assert.DoesNotContain(
            summaryHost.Members,
            member => member.Kind is "property" or "event");
        Assert.Equal(publicSurface.PublicMethodCount, summary.PublicMethodCount);
        Assert.Equal(publicSurface.PublicPropertyCount, summary.PublicPropertyCount);
        Assert.Equal(publicSurface.PublicEventCount, summary.PublicEventCount);
        Assert.Equal(
            publicSurface.InspectionFailures.Select(
                failure => (failure.Operation, failure.Detail)),
            summary.InspectionFailures.Select(failure => (failure.Operation, failure.Detail)));
    }

    [Fact]
    public void ExtractSummary_PreservesPublicExplicitAggregateFacets()
    {
        byte[] image = BuildModifiedVoidExplicitAggregateImage(
            MethodAttributes.Public,
            modifiedReturns: false);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);

        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(peReader);
        ApiType host = Assert.Single(
            summary.Types,
            type => type.Name == "Host");
        ApiMember[] aggregates = host.Members
            .Where(member => member.Kind is "property" or "event")
            .ToArray();

        Assert.Equal(4, aggregates.Length);
        Assert.All(
            aggregates,
            member =>
            {
                Assert.True(member.IsExplicitInterfaceImplementation);
                Assert.Null(member.Accessibility);
            });
        Assert.Empty(summary.InspectionFailures);
    }

    /// <summary>
    /// The compiler-produced close negatives for the row-signature check: an explicit
    /// <c>init</c> setter carries <c>modreq(IsExternalInit)</c> over its <c>void</c> return, and
    /// an explicit indexer's row parameters live on the row rather than only on its accessors.
    /// Both must keep classifying and projecting as explicit aggregates.
    /// </summary>
    [Fact]
    public void ExplicitAggregateRowSignature_AcceptsInitAndIndexerShapes()
    {
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(
                typeof(MetadataDeclarationQueryFixtures.ExplicitRowSignatureSurface)),
            includeNonPublicMembers: true);

        var aggregates = queried.Members
            .Where(member => member.IsExplicitInterfaceImplementation)
            .ToArray();

        Assert.Contains(aggregates, member => member.Name.EndsWith(".Value", StringComparison.Ordinal));
        Assert.Contains(aggregates, member => member.Name.EndsWith(".Item", StringComparison.Ordinal));
        Assert.Contains(aggregates, member => member.Kind == "event");

        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
            type => type.FullName.EndsWith(
                "." + nameof(MetadataDeclarationQueryFixtures.ExplicitRowSignatureSurface),
                StringComparison.Ordinal));
        Assert.Contains(
            extracted.Members,
            member => member is { Kind: "property", IsExplicitInterfaceImplementation: true }
                && member.Name.EndsWith(".Value", StringComparison.Ordinal));
    }

    [Fact]
    public void EventSealing_RejectsMixedAccessorState()
    {
        string path = EmitIncoherentEventSealing();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "DerivedEventSealing");
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var extracted = Assert.Single(
                surface.Types,
                type => type.Name == "DerivedEventSealing");

            Assert.DoesNotContain(extracted.Members, member => member.Kind == "event");
            Assert.Contains(
                surface.InspectionFailures,
                failure => failure.Operation == "event modifiers");
            Assert.Throws<BadImageFormatException>(() =>
                MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    typeHandle,
                    includeNonPublicMembers: true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PropertySlots_RejectMixedNewSlotAndOverrideAccessors()
    {
        string path = EmitIncoherentPropertySlots();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinitionHandle typeHandle =
                GetTypeDefinitionHandle(reader, "DerivedPropertySlots");
            ApiSurface surface =
                ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            ApiType extracted = Assert.Single(
                surface.Types,
                type => type.Name == "DerivedPropertySlots");

            Assert.DoesNotContain(
                extracted.Members,
                member => member.Kind == "property");
            Assert.Contains(
                surface.InspectionFailures,
                failure => failure.Operation == "property modifiers");
            Assert.Throws<BadImageFormatException>(() =>
                MetadataDeclarationQuery.GetTypeSurface(
                    reader,
                    typeHandle,
                    includeNonPublicMembers: true));

            using var summaryReader =
                new PEReader(File.OpenRead(path));
            ApiSurface summary =
                ApiSurfaceExtractor.ExtractSummary(summaryReader);
            ApiType summarized = Assert.Single(
                summary.Types,
                type => type.Name == "DerivedPropertySlots");
            Assert.DoesNotContain(
                summarized.Members,
                member => member.Kind == "property");
            Assert.Contains(
                summary.InspectionFailures,
                failure => failure.Operation == "property modifiers");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_ClassifiesGenericExplicitInterfaceAggregates()
    {
        Type[] fixtureTypes =
        [
            typeof(MetadataDeclarationQueryFixtures.GenericSurface<,>),
            typeof(MetadataDeclarationQueryFixtures.StringSurface),
            typeof(MetadataDeclarationQueryFixtures.StringArraySurface)
        ];

        foreach (Type fixtureType in fixtureTypes)
        {
            var queried = MetadataDeclarationQuery.GetTypeSurface(
                Reader,
                GetTypeDefinitionHandle(fixtureType),
                includeNonPublicMembers: true);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
                type => StripGenericArity(type.FullName).EndsWith(
                    "." + StripGenericArity(fixtureType.Name),
                    StringComparison.Ordinal));

            foreach (var surface in new[] { queried, extracted })
            {
                Assert.Contains(
                    surface.Members,
                    member => member is
                    {
                        Kind: "property",
                        IsExplicitInterfaceImplementation: true
                    } && member.Name.EndsWith(".Value", StringComparison.Ordinal));
                Assert.Contains(
                    surface.Members,
                    member => member is
                    {
                        Kind: "event",
                        IsExplicitInterfaceImplementation: true
                    } && member.Name.EndsWith(".Changed", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(typeof(MetadataDeclarationQueryFixtures.SealedImplicitSurface))]
    [InlineData(typeof(MetadataDeclarationQueryFixtures.StructImplicitSurface))]
    public void TypeSurface_DoesNotProjectMetadataVirtualAsCSharpVirtual(Type fixtureType)
    {
        var queried = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(fixtureType),
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(PeReader, includeAll: true).Types,
            type => type.FullName.EndsWith(
                "." + fixtureType.Name,
                StringComparison.Ordinal));

        foreach (var surface in new[] { queried, extracted })
        {
            var property = Assert.Single(
                surface.Members,
                member => member.Kind == "property" && member.Name == "Value");

            Assert.False(property.IsVirtual);
        }
    }

    [Fact]
    public void ExplicitAggregateIdentity_RejectsSignatureIncompatibleMethodImplTargets()
    {
        string path = EmitSignatureIncompatibleMethodImplProperty();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "BadProperty");
            var typeDef = reader.GetTypeDefinition(typeHandle);
            var property = reader.GetPropertyDefinition(Assert.Single(typeDef.GetProperties()));
            var accessors = property.GetAccessors();

            Assert.False(ApiSurfaceExtractor.IsExplicitInterfaceAggregate(
                reader.GetString(property.Name),
                ApiSurfaceExtractor.GetExplicitInterfaceImplementationTargets(reader, typeDef),
                (accessors.Getter, "get_"),
                (accessors.Setter, "set_")));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeSurface_PreservesUnqualifiedMethodImplAccessors(bool isPublic)
    {
        string path = EmitUnqualifiedMethodImplAccessor(isPublic);
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "UnqualifiedAccessor");
            var queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle);
            var queriedAll = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true);
            var extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
                type => type.FullName == "UnqualifiedAccessor");

            foreach (var surface in new[] { queried, queriedAll })
            {
                // Read is a real Property accessor with a deliberately noncanonical name.
                // Association alone must not hide it as compiler extension-property plumbing.
                Assert.Contains(
                    surface.Members,
                    member => member is
                    {
                        Name: "Read",
                        Kind: "explicit-interface-implementation"
                    });
            }
            Assert.Contains(
                extracted.Members,
                member => member is
                {
                    Name: "Read",
                    Kind: "explicit-interface-implementation"
                });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MethodImplProjectionRequiresCategoryConstraintsAndInterfaceClosure()
    {
        string path = EmitMethodImplSemanticCases();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            MetadataReader reader = peReader.GetMetadataReader();
            TypeDefinitionHandle typeHandle =
                GetTypeDefinitionHandle(reader, "SemanticImplementation");
            ApiType queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: false);
            ApiType extracted = Assert.Single(
                ApiSurfaceExtractor.Extract(
                    peReader,
                    includeAll: false).Types,
                type => type.FullName == "SemanticImplementation");

            foreach (ApiType surface in new[] { queried, extracted })
            {
                ApiMember propertyShapedBody = Assert.Single(
                    surface.Members,
                    member => member.Name == "get_Value");
                Assert.True(
                    propertyShapedBody.IsExplicitInterfaceImplementation);
                Assert.False(
                    propertyShapedBody.CanUseImplicitInterfaceSyntax);

                ApiMember constraintMismatch = Assert.Single(
                    surface.Members,
                    member => member.Name == "Convert");
                Assert.True(
                    constraintMismatch.IsExplicitInterfaceImplementation);
                Assert.False(
                    constraintMismatch.CanUseImplicitInterfaceSyntax);

                ApiMember constraintMatch = Assert.Single(
                    surface.Members,
                    member => member.Name == "Transform");
                Assert.True(
                    constraintMatch.IsExplicitInterfaceImplementation);
                Assert.True(
                    constraintMatch.CanUseImplicitInterfaceSyntax);

                ApiMember inheritedTarget = Assert.Single(
                    surface.Members,
                    member => member.Name == "InheritedBody");
                Assert.True(inheritedTarget.IsExplicitInterfaceImplementation);
                Assert.Equal(
                    InterfaceImplementationResolution.Proven,
                    inheritedTarget.InterfaceImplementationResolution);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_FoldsPublicInterfaceMethodImplAccessors()
    {
        using var stream = File.OpenRead(typeof(Half).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(reader, typeof(Half).FullName!);

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            type => type.FullName == typeof(Half).FullName);

        foreach (var surface in new[] { queried, extracted })
        {
            Assert.Contains(surface.Members, member => member is { Name: "MaxValue", Kind: "property" });
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_MaxValue");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_Zero");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_Pi");
        }
    }

    [Fact]
    public void TypeSurface_FoldsCanonicalVisualBasicMethodImplAccessors()
    {
        string runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        using var peReader = new PEReader(
            File.OpenRead(Path.Combine(runtimeDirectory, "Microsoft.VisualBasic.Core.dll")));
        var reader = peReader.GetMetadataReader();
        var typeHandle = GetTypeDefinitionHandle(reader, "Microsoft.VisualBasic.Collection");

        var queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        var extracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            type => type.FullName == "Microsoft.VisualBasic.Collection");

        foreach (var surface in new[] { queried, extracted })
        {
            Assert.Contains(surface.Members, member => member.Name == "ICollectionCount");
            Assert.Contains(surface.Members, member => member.Name == "IListItem");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_ICollectionCount");
            Assert.DoesNotContain(surface.Members, member => member.Name == "get_IListItem");
            Assert.DoesNotContain(surface.Members, member => member.Name == "set_IListItem");
        }

        var defaultQueried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: false);
        var defaultExtracted = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: false).Types,
            type => type.FullName == "Microsoft.VisualBasic.Collection");
        foreach (var surface in new[] { defaultQueried, defaultExtracted })
        {
            Assert.DoesNotContain(surface.Members, member => member.Name == "ICollectionCount");
            Assert.Contains(surface.Members, member => member.Name == "get_ICollectionCount");
            ApiMember inheritedTarget = Assert.Single(
                surface.Members,
                member => member.Name == "ICollectionGetEnumerator");
            Assert.True(inheritedTarget.IsExplicitInterfaceImplementation);
            Assert.Equal(
                InterfaceImplementationResolution.Undetermined,
                inheritedTarget.InterfaceImplementationResolution);
        }
    }

    [Fact]
    public void ExplicitInterfaceBodies_KeepAssemblyScopeInDeclarationIdentity()
    {
        string path = EmitSameNamedBaseAndInterfaceMethodImpl();
        try
        {
            using var peReader = new PEReader(File.OpenRead(path));
            var reader = peReader.GetMetadataReader();
            var typeHandle = GetTypeDefinitionHandle(reader, "Derived");
            var typeDef = reader.GetTypeDefinition(typeHandle);

            var bodies = ApiSurfaceExtractor.GetExplicitInterfaceImplementationTargets(
                reader,
                typeDef).Keys;
            var names = bodies
                .Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name))
                .ToList();

            Assert.Contains("ImplementInterface", names);
            Assert.DoesNotContain("OverrideBase", names);

            ApiType queried = MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: false);
            Assert.Contains(queried.Members, member => member.Name == "ImplementInterface");
            Assert.DoesNotContain(queried.Members, member => member.Name == "OverrideBase");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TypeSurface_AccessorFactsPreserveReturnAttributes()
    {
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)),
            includeNonPublicMembers: true);
        var property = Assert.Single(
            surface.Members,
            member => member.Name == nameof(
                MetadataDeclarationQueryFixtures.PropertyWithReturnNotNull));
        var getter = Assert.Single(
            property.AccessorFacts,
            accessor => accessor.Kind == "get");

        Assert.Contains(
            getter.ReturnAttributes,
            attribute => attribute.Contains(
                "System.Diagnostics.CodeAnalysis.NotNull",
                StringComparison.Ordinal));
    }

    [Fact]
    public void AccessibilityMapping_HandlesPrivateScopeAndRejectsReservedValues()
    {
        var methodAccessibility = typeof(MetadataDeclarationQuery).GetMethod(
            "AccessibilityKeyword",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(MethodAttributes)]);
        var fieldAccessibility = typeof(MetadataDeclarationQuery).GetMethod(
            "AccessibilityKeyword",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(FieldAttributes)]);

        Assert.Equal("private", methodAccessibility!.Invoke(null, [MethodAttributes.PrivateScope]));
        Assert.Equal("private", fieldAccessibility!.Invoke(null, [FieldAttributes.PrivateScope]));
        Assert.IsType<BadImageFormatException>(
            Assert.Throws<TargetInvocationException>(
                () => methodAccessibility.Invoke(null, [(MethodAttributes)0x0007]))
                .InnerException);
        Assert.IsType<BadImageFormatException>(
            Assert.Throws<TargetInvocationException>(
                () => fieldAccessibility.Invoke(null, [(FieldAttributes)0x0007]))
                .InnerException);
    }

    [Fact]
    public void ReservedMethodAccessibility_IsRejected()
    {
        var methodAccessibility = typeof(MetadataDeclarationQuery).GetMethod(
            "AccessibilityKeyword",
            BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(MethodAttributes)]);

        var exception = Assert.Throws<TargetInvocationException>(
            () => methodAccessibility!.Invoke(null, [(MethodAttributes)0x7]));
        Assert.IsType<BadImageFormatException>(exception.InnerException);
    }

    [Fact]
    public void TypeSurface_EscapesKeywordMemberNames()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, "class");

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);
        var surface = MetadataDeclarationQuery.GetTypeSurface(Reader, GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));
        var property = Assert.Single(surface.Members, member => member.Name == "while");
        var field = Assert.Single(surface.Members, member => member.Name == "event");

        Assert.Equal("@class", declaration.CSharpName);
        Assert.Equal("@class", declaration.Signature.MemberName);
        Assert.Contains(surface.Members, member => member.Name == "class" && member.Signature!.Contains("@class", StringComparison.Ordinal));
        Assert.Equal("@while", property.SignatureModel!.MemberName);
        Assert.Contains("@while", property.Signature);
        Assert.Equal("@event", field.SignatureModel!.MemberName);
    }

    [Fact]
    public void TypeSurface_EscapesQualifiedKeywordParameterTypesInCompatibilitySignatures()
    {
        var surface = MetadataDeclarationQuery.GetTypeSurface(
            Reader,
            GetTypeDefinitionHandle(typeof(MetadataDeclarationQueryFixtures)));

        var method = Assert.Single(surface.Members, member => member.Name == "QualifiedKeyword");

        Assert.Contains(
            "MetadataDeclarationQueryFixtures.@namespace @class",
            method.Signature,
            StringComparison.Ordinal);
        Assert.Contains("\".namespace\"", method.Signature, StringComparison.Ordinal);

        var globalKeyword = Assert.Single(
            surface.Members,
            member => member.Name == "GlobalKeyword");
        Assert.Contains(
            "GlobalType(typeof(@class), (@event)1)",
            globalKeyword.SignatureModel!.Parameters[0].Attributes);
        Assert.Equal(
            "(@event)1",
            globalKeyword.SignatureModel.Parameters[2].DefaultValueText);
        Assert.Contains("@class value", globalKeyword.Signature, StringComparison.Ordinal);
        Assert.Contains("List<@class> values = null", globalKeyword.Signature, StringComparison.Ordinal);
        Assert.Contains("@event mode = (@event)1", globalKeyword.Signature, StringComparison.Ordinal);
        Assert.Contains(
            "GlobalType(typeof(@class), (@event)1)",
            globalKeyword.Signature,
            StringComparison.Ordinal);
        Assert.Contains("\"a\\\"b.class\"", globalKeyword.Signature, StringComparison.Ordinal);

        var syntaxKeywords = Assert.Single(
            surface.Members,
            member => member.Name == "SyntaxKeywordTypes");
        Assert.Contains("@delegate delegateValue", syntaxKeywords.Signature, StringComparison.Ordinal);
        Assert.Contains("@readonly readonlyValue", syntaxKeywords.Signature, StringComparison.Ordinal);
        Assert.Contains("@scoped scopedValue", syntaxKeywords.Signature, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodDeclaration_PreservesNestedGenericTypeArgumentPlacement()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var method = GetMethod(type, nameof(MetadataDeclarationQueryFixtures.NestedGeneric));

        var declaration = MetadataDeclarationQuery.GetMethod(Reader, type, method);

        const string nestedType =
            "ILInspector.Metadata.Tests.MetadataDeclarationQueryFixtures.Container<int>.Row<string>";
        Assert.Equal(nestedType, declaration.Signature.ReturnType);
        Assert.Equal(nestedType, Assert.Single(declaration.Signature.Parameters).Type);
        Assert.Null(declaration.SignatureDecodeStatus);
    }

    [Fact]
    public void SelfTypeSignature_IncludesDeclaringGenericParameters()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures.Container<>.Row<>));

        var signature = MetadataDeclarationQuery.SelfTypeSignature(Reader, type);

        Assert.Equal("ILInspector.Metadata.Tests.MetadataDeclarationQueryFixtures.Container<T>.Row<U>", signature);
    }

    [Fact]
    public void GetGenericConstraintClauses_RendersSpecialConstraints()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));

        var structClauses = MetadataDeclarationQuery.GetGenericConstraintClauses(
            Reader, type, GetMethod(type, nameof(MetadataDeclarationQueryFixtures.StructConstraint)));
        Assert.Equal("struct", Assert.Contains("T", structClauses));

        var classClauses = MetadataDeclarationQuery.GetGenericConstraintClauses(
            Reader, type, GetMethod(type, nameof(MetadataDeclarationQueryFixtures.ClassNewConstraint)));
        Assert.Equal("class, new()", Assert.Contains("T", classClauses));
    }

    [Fact]
    public void SpellableConstraintClause_DropsExplicitObjectConstraint()
    {
        // C# forbids `where T : object` (CS0702); it must be dropped even though
        // Roslyn never emits it (non-C# compilers can).
        Assert.Null(MetadataDeclarationQuery.SpellableConstraintClause(
            new TypeParameter { Name = "T", Constraints = { "System.Object" } }));
        Assert.Equal("System.IComparable", MetadataDeclarationQuery.SpellableConstraintClause(
            new TypeParameter { Name = "T", Constraints = { "System.Object", "System.IComparable" } }));
        Assert.Equal("class, new()", MetadataDeclarationQuery.SpellableConstraintClause(
            new TypeParameter { Name = "T", Constraints = { "class", "new()" } }));
    }

    [Fact]
    public void SpellableConstraintClause_EscapesKeywordTypeNamesUsingStructuredKind()
    {
        // A special-constraint keyword and a type literally named the same keyword are
        // indistinguishable as raw strings; the structured kind disambiguates them so
        // the type name is escaped (@struct) while the keyword constraint stays verbatim.
        var parameter = new TypeParameter
        {
            Name = "T",
            Constraints = { "struct", "N.struct", "System.IComparable" },
            StructuredConstraints =
            [
                new TypeParameterConstraint("struct", IsTypeName: false),
                new TypeParameterConstraint("N.struct", IsTypeName: true),
                new TypeParameterConstraint("System.IComparable", IsTypeName: true),
            ],
        };

        Assert.Equal(
            "struct, N.@struct, System.IComparable",
            MetadataDeclarationQuery.SpellableConstraintClause(parameter));

        var globalKeyword = new TypeParameter
        {
            Name = "T",
            Constraints = { "class" },
            StructuredConstraints = [new TypeParameterConstraint("class", IsTypeName: true)],
        };
        Assert.Equal("@class", MetadataDeclarationQuery.SpellableConstraintClause(globalKeyword));
    }

    [Fact]
    public void IsVolatileField_DetectsVolatileModreq()
    {
        var type = GetTypeDefinition(typeof(MetadataDeclarationQueryFixtures));
        var context = GenericContext.ForType(Reader, type);
        Assert.True(MetadataDeclarationQuery.IsVolatileField(Reader, GetField(type, "VolatileField"), context));
        Assert.False(MetadataDeclarationQuery.IsVolatileField(Reader, GetField(type, "PlainField"), context));
    }

    [Fact]
    public void HasRequiredModifier_RequiresExactNamespace_NotSuffix()
    {
        var inner = new NamedTypeNode("System.Int32", isReferenceType: false);
        var exact = new ModifiedTypeNode(
            new NamedTypeNode("System.Runtime.CompilerServices.IsVolatile", isReferenceType: true), inner, isRequired: true);
        var wrongNamespace = new ModifiedTypeNode(
            new NamedTypeNode("Other.Namespace.IsVolatile", isReferenceType: true), inner, isRequired: true);
        var globalNamespace = new ModifiedTypeNode(
            new NamedTypeNode("IsVolatile", isReferenceType: true), inner, isRequired: true);

        Assert.True(exact.HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile"));
        Assert.False(wrongNamespace.HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile"));
        Assert.False(globalNamespace.HasRequiredModifier("System.Runtime.CompilerServices", "IsVolatile"));
    }

    static string EmitMethodWithoutParamRow()
    {
        var assemblyName = new AssemblyName("MissingParamRow");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("MissingParamSample", TypeAttributes.Public);
        var method = type.DefineMethod(
            "Echo",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(int),
            [typeof(int)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ret);
        type.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"MissingParamRow-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitEventWithOtherAccessor()
    {
        var assemblyName = new AssemblyName("EventOther");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("EventOtherSample", TypeAttributes.Public);
        const MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        var add = type.DefineMethod("add_Changed", attributes, typeof(void), [typeof(EventHandler)]);
        add.GetILGenerator().Emit(OpCodes.Ret);
        var remove = type.DefineMethod("remove_Changed", attributes, typeof(void), [typeof(EventHandler)]);
        remove.GetILGenerator().Emit(OpCodes.Ret);
        var other = type.DefineMethod("other_Changed", attributes, typeof(void), Type.EmptyTypes);
        other.GetILGenerator().Emit(OpCodes.Ret);
        var eventBuilder = type.DefineEvent("Changed", EventAttributes.None, typeof(EventHandler));
        eventBuilder.SetAddOnMethod(add);
        eventBuilder.SetRemoveOnMethod(remove);
        eventBuilder.AddOtherMethod(other);
        type.CreateType();

        string path = Path.Combine(Path.GetTempPath(), $"EventOther-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitRemoveOnlyEvent()
    {
        var assemblyName = new AssemblyName("RemoveOnlyEvent");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var type = module.DefineType("RemoveOnlyEventSample", TypeAttributes.Public);
        const MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig;
        var remove = type.DefineMethod(
            "remove_Changed",
            attributes,
            typeof(void),
            [typeof(EventHandler)]);
        remove.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineEvent("Changed", EventAttributes.None, typeof(EventHandler))
            .SetRemoveOnMethod(remove);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"RemoveOnlyEvent-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitUnqualifiedMethodImplAccessor(bool isPublic)
    {
        var assemblyName = new AssemblyName("UnqualifiedMethodImplAccessor");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);

        var interfaceBuilder = module.DefineType(
            "IUnqualifiedAccessor",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceGetter = interfaceBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        interfaceBuilder
            .DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
            .SetGetMethod(interfaceGetter);
        var interfaceType = interfaceBuilder.CreateType();

        var type = module.DefineType("UnqualifiedAccessor", TypeAttributes.Public);
        type.AddInterfaceImplementation(interfaceType);
        var getter = type.DefineMethod(
            "Read",
            (isPublic ? MethodAttributes.Public : MethodAttributes.Private)
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        getter.GetILGenerator().Emit(OpCodes.Ldc_I4, 42);
        getter.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
            .SetGetMethod(getter);
        type.DefineMethodOverride(getter, interfaceType.GetMethod("get_Value")!);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"UnqualifiedMethodImplAccessor-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitMethodImplSemanticCases()
    {
        var assemblyName = new AssemblyName("MethodImplSemanticCases");
        var assembly = new PersistedAssemblyBuilder(
            assemblyName,
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName.Name!);

        TypeBuilder propertyInterface = module.DefineType(
            "IPropertyContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        MethodBuilder interfaceGetter = propertyInterface.DefineMethod(
            "get_Value",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName,
            typeof(int),
            Type.EmptyTypes);
        propertyInterface
            .DefineProperty(
                "Value",
                PropertyAttributes.None,
                typeof(int),
                null)
            .SetGetMethod(interfaceGetter);
        Type propertyInterfaceType = propertyInterface.CreateType();

        TypeBuilder genericInterface = module.DefineType(
            "IGenericContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        MethodBuilder interfaceGeneric = genericInterface.DefineMethod(
            "Convert",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder interfaceParameter =
            Assert.Single(interfaceGeneric.DefineGenericParameters("T"));
        interfaceParameter.SetGenericParameterAttributes(
            GenericParameterAttributes.ReferenceTypeConstraint);
        interfaceGeneric.SetReturnType(typeof(void));
        interfaceGeneric.SetParameters(Type.EmptyTypes);
        MethodBuilder interfaceTransform = genericInterface.DefineMethod(
            "Transform",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder interfaceTransformParameter =
            Assert.Single(interfaceTransform.DefineGenericParameters("T"));
        interfaceTransformParameter.SetGenericParameterAttributes(
            GenericParameterAttributes.ReferenceTypeConstraint);
        interfaceTransform.SetReturnType(typeof(void));
        interfaceTransform.SetParameters(Type.EmptyTypes);
        Type genericInterfaceType = genericInterface.CreateType();

        TypeBuilder structInterface = module.DefineType(
            "IStructContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        MethodBuilder structGeneric = structInterface.DefineMethod(
            "Convert",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder structParameter =
            Assert.Single(structGeneric.DefineGenericParameters("T"));
        structParameter.SetGenericParameterAttributes(
            GenericParameterAttributes.NotNullableValueTypeConstraint);
        structGeneric.SetReturnType(typeof(void));
        structGeneric.SetParameters(Type.EmptyTypes);
        Type structInterfaceType = structInterface.CreateType();

        TypeBuilder rootInterface = module.DefineType(
            "IRootContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        MethodBuilder rootMethod = rootInterface.DefineMethod(
            "Run",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes);
        Type rootInterfaceType = rootInterface.CreateType();
        TypeBuilder childInterface = module.DefineType(
            "IChildContract",
            TypeAttributes.Public
                | TypeAttributes.Interface
                | TypeAttributes.Abstract);
        childInterface.AddInterfaceImplementation(rootInterfaceType);
        Type childInterfaceType = childInterface.CreateType();

        TypeBuilder implementation = module.DefineType(
            "SemanticImplementation",
            TypeAttributes.Public);
        implementation.AddInterfaceImplementation(propertyInterfaceType);
        implementation.AddInterfaceImplementation(genericInterfaceType);
        implementation.AddInterfaceImplementation(structInterfaceType);
        implementation.AddInterfaceImplementation(childInterfaceType);

        MethodBuilder getterBody = implementation.DefineMethod(
            "get_Value",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName,
            typeof(int),
            Type.EmptyTypes);
        getterBody.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        getterBody.GetILGenerator().Emit(OpCodes.Ret);
        implementation.DefineMethodOverride(
            getterBody,
            propertyInterfaceType.GetMethod("get_Value")!);

        MethodBuilder genericBody = implementation.DefineMethod(
            "Convert",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder bodyParameter =
            Assert.Single(genericBody.DefineGenericParameters("T"));
        bodyParameter.SetGenericParameterAttributes(
            GenericParameterAttributes.NotNullableValueTypeConstraint);
        genericBody.SetReturnType(typeof(void));
        genericBody.SetParameters(Type.EmptyTypes);
        genericBody.GetILGenerator().Emit(OpCodes.Ret);
        implementation.DefineMethodOverride(
            genericBody,
            genericInterfaceType.GetMethod("Convert")!);
        implementation.DefineMethodOverride(
            genericBody,
            structInterfaceType.GetMethod("Convert")!);

        MethodBuilder transformBody = implementation.DefineMethod(
            "Transform",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot);
        GenericTypeParameterBuilder transformParameter =
            Assert.Single(transformBody.DefineGenericParameters("T"));
        transformParameter.SetGenericParameterAttributes(
            GenericParameterAttributes.ReferenceTypeConstraint);
        transformBody.SetReturnType(typeof(void));
        transformBody.SetParameters(Type.EmptyTypes);
        transformBody.GetILGenerator().Emit(OpCodes.Ret);
        implementation.DefineMethodOverride(
            transformBody,
            genericInterfaceType.GetMethod("Transform")!);

        MethodBuilder inheritedBody = implementation.DefineMethod(
            "InheritedBody",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot,
            typeof(void),
            Type.EmptyTypes);
        inheritedBody.GetILGenerator().Emit(OpCodes.Ret);
        implementation.DefineMethodOverride(
            inheritedBody,
            rootInterfaceType.GetMethod("Run")!);
        implementation.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"MethodImplSemanticCases-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitSameNamedBaseAndInterfaceMethodImpl()
    {
        var baseAssembly = new PersistedAssemblyBuilder(
            new AssemblyName("SameNameBase"),
            typeof(object).Assembly);
        var baseModule = baseAssembly.DefineDynamicModule("SameNameBase");
        var baseBuilder = baseModule.DefineType("N.Slot", TypeAttributes.Public);
        var baseMethod = baseBuilder.DefineMethod(
            "BaseSlot",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        baseMethod.GetILGenerator().Emit(OpCodes.Ldc_I4_1);
        baseMethod.GetILGenerator().Emit(OpCodes.Ret);
        var baseType = baseBuilder.CreateType();

        var interfaceAssembly = new PersistedAssemblyBuilder(
            new AssemblyName("SameNameInterface"),
            typeof(object).Assembly);
        var interfaceModule = interfaceAssembly.DefineDynamicModule("SameNameInterface");
        var interfaceBuilder = interfaceModule.DefineType(
            "N.Slot",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceMethod = interfaceBuilder.DefineMethod(
            "InterfaceSlot",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        var interfaceType = interfaceBuilder.CreateType();

        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("SameNameConsumer"),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("SameNameConsumer");
        var type = module.DefineType("Derived", TypeAttributes.Public, baseType);
        type.AddInterfaceImplementation(interfaceType);

        var interfaceBody = type.DefineMethod(
            "ImplementInterface",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        interfaceBody.GetILGenerator().Emit(OpCodes.Ldc_I4_2);
        interfaceBody.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethodOverride(interfaceBody, interfaceMethod);

        var baseBody = type.DefineMethod(
            "OverrideBase",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        baseBody.GetILGenerator().Emit(OpCodes.Ldc_I4_3);
        baseBody.GetILGenerator().Emit(OpCodes.Ret);
        type.DefineMethodOverride(baseBody, baseMethod);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"SameNameConsumer-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitBaseClassMethodImplEvent()
    {
        var assemblyName = new AssemblyName("BaseClassMethodImplEvent");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        const MethodAttributes baseAttributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;
        var baseType = module.DefineType("BaseEvent", TypeAttributes.Public);
        var baseAdder = baseType.DefineMethod(
            "add_Changed",
            baseAttributes,
            typeof(void),
            [typeof(EventHandler)]);
        baseAdder.GetILGenerator().Emit(OpCodes.Ret);
        var baseRemover = baseType.DefineMethod(
            "remove_Changed",
            baseAttributes,
            typeof(void),
            [typeof(EventHandler)]);
        baseRemover.GetILGenerator().Emit(OpCodes.Ret);
        var baseEvent = baseType.DefineEvent("Changed", EventAttributes.None, typeof(EventHandler));
        baseEvent.SetAddOnMethod(baseAdder);
        baseEvent.SetRemoveOnMethod(baseRemover);
        var createdBase = baseType.CreateType();

        const MethodAttributes derivedAttributes =
            MethodAttributes.Private
            | MethodAttributes.Virtual
            | MethodAttributes.Final
            | MethodAttributes.NewSlot
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;
        var derivedType = module.DefineType("DerivedEvent", TypeAttributes.Public, createdBase);
        var adder = derivedType.DefineMethod(
            "add_BaseChanged",
            derivedAttributes,
            typeof(void),
            [typeof(EventHandler)]);
        adder.GetILGenerator().Emit(OpCodes.Ret);
        var remover = derivedType.DefineMethod(
            "remove_BaseChanged",
            derivedAttributes,
            typeof(void),
            [typeof(EventHandler)]);
        remover.GetILGenerator().Emit(OpCodes.Ret);
        var derivedEvent = derivedType.DefineEvent(
            "BaseEvent.Changed",
            EventAttributes.None,
            typeof(EventHandler));
        derivedEvent.SetAddOnMethod(adder);
        derivedEvent.SetRemoveOnMethod(remover);
        derivedType.DefineMethodOverride(adder, createdBase.GetMethod("add_Changed")!);
        derivedType.DefineMethodOverride(remover, createdBase.GetMethod("remove_Changed")!);
        derivedType.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"BaseClassMethodImplEvent-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitIncoherentEventSealing()
    {
        var assemblyName = new AssemblyName("IncoherentEventSealing");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        const MethodAttributes accessorAttributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;

        var baseBuilder = module.DefineType("BaseEventSealing", TypeAttributes.Public);
        var baseAdder = baseBuilder.DefineMethod(
            "add_Changed",
            accessorAttributes | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(Action)]);
        baseAdder.GetILGenerator().Emit(OpCodes.Ret);
        var baseRemover = baseBuilder.DefineMethod(
            "remove_Changed",
            accessorAttributes | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(Action)]);
        baseRemover.GetILGenerator().Emit(OpCodes.Ret);
        var baseEvent = baseBuilder.DefineEvent("Changed", EventAttributes.None, typeof(Action));
        baseEvent.SetAddOnMethod(baseAdder);
        baseEvent.SetRemoveOnMethod(baseRemover);
        var baseType = baseBuilder.CreateType();

        var derivedBuilder = module.DefineType(
            "DerivedEventSealing",
            TypeAttributes.Public,
            baseType);
        var adder = derivedBuilder.DefineMethod(
            "add_Changed",
            accessorAttributes,
            typeof(void),
            [typeof(Action)]);
        adder.GetILGenerator().Emit(OpCodes.Ret);
        var remover = derivedBuilder.DefineMethod(
            "remove_Changed",
            accessorAttributes | MethodAttributes.Final,
            typeof(void),
            [typeof(Action)]);
        remover.GetILGenerator().Emit(OpCodes.Ret);
        var @event = derivedBuilder.DefineEvent("Changed", EventAttributes.None, typeof(Action));
        @event.SetAddOnMethod(adder);
        @event.SetRemoveOnMethod(remover);
        derivedBuilder.DefineMethodOverride(adder, baseType.GetMethod("add_Changed")!);
        derivedBuilder.DefineMethodOverride(remover, baseType.GetMethod("remove_Changed")!);
        derivedBuilder.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"IncoherentEventSealing-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitIncoherentPropertySlots()
    {
        var assemblyName = new AssemblyName("IncoherentPropertySlots");
        var assembly =
            new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        const MethodAttributes accessorAttributes =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;

        var baseBuilder =
            module.DefineType(
                "BasePropertySlots",
                TypeAttributes.Public);
        var baseGetter = baseBuilder.DefineMethod(
            "get_P",
            accessorAttributes | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        baseGetter.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
        baseGetter.GetILGenerator().Emit(OpCodes.Ret);
        var baseSetter = baseBuilder.DefineMethod(
            "set_P",
            accessorAttributes | MethodAttributes.NewSlot,
            typeof(void),
            [typeof(int)]);
        baseSetter.GetILGenerator().Emit(OpCodes.Ret);
        var baseProperty = baseBuilder.DefineProperty(
            "P",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        baseProperty.SetGetMethod(baseGetter);
        baseProperty.SetSetMethod(baseSetter);
        Type baseType = baseBuilder.CreateType();

        var derivedBuilder = module.DefineType(
            "DerivedPropertySlots",
            TypeAttributes.Public,
            baseType);
        var getter = derivedBuilder.DefineMethod(
            "get_P",
            accessorAttributes | MethodAttributes.NewSlot,
            typeof(int),
            Type.EmptyTypes);
        getter.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
        getter.GetILGenerator().Emit(OpCodes.Ret);
        var setter = derivedBuilder.DefineMethod(
            "set_P",
            accessorAttributes,
            typeof(void),
            [typeof(int)]);
        setter.GetILGenerator().Emit(OpCodes.Ret);
        var property = derivedBuilder.DefineProperty(
            "P",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        derivedBuilder.DefineMethodOverride(
            setter,
            baseType.GetMethod("set_P")!);
        derivedBuilder.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"IncoherentPropertySlots-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitAbstractNonVirtualProperty()
    {
        var assemblyName =
            new AssemblyName("AbstractNonVirtualProperty");
        var assembly =
            new PersistedAssemblyBuilder(
                assemblyName,
                typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName.Name!);
        TypeBuilder type = module.DefineType(
            "AbstractNonVirtual",
            TypeAttributes.Public | TypeAttributes.Abstract);
        const MethodAttributes attributes =
            MethodAttributes.Public
            | MethodAttributes.Abstract
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;
        MethodBuilder getter = type.DefineMethod(
            "get_Value",
            attributes,
            typeof(int),
            Type.EmptyTypes);
        MethodBuilder setter = type.DefineMethod(
            "set_Value",
            attributes,
            typeof(void),
            [typeof(int)]);
        PropertyBuilder property = type.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(int),
            Type.EmptyTypes);
        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"AbstractNonVirtualProperty-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static byte[] BuildHostileInitModifierImage(
        bool cyclicScope,
        bool typeSpecModifier = false,
        bool nestedModifier = true,
        string modifierNamespace =
            "System.Runtime.CompilerServices",
        string modifierName = "IsExternalInit")
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("HostileInitModifier.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("HostileInitModifier"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Host"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        EntityHandle outerScope;
        if (cyclicScope)
        {
            outerScope = MetadataTokens.TypeReferenceHandle(2);
        }
        else
        {
            outerScope = metadata.AddAssemblyReference(
                metadata.GetOrAddString("Dependency"),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
        }
        TypeReferenceHandle outer = metadata.AddTypeReference(
            outerScope,
            default,
            metadata.GetOrAddString("MarkerHost"));
        TypeReferenceHandle modifier = metadata.AddTypeReference(
            cyclicScope || nestedModifier
                ? outer
                : outerScope,
            metadata.GetOrAddString(modifierNamespace),
            metadata.GetOrAddString(modifierName));
        EntityHandle modifierHandle = modifier;
        if (typeSpecModifier)
        {
            var typeSpec = new BlobBuilder();
            typeSpec.WriteByte(0x12); // CLASS
            typeSpec.WriteByte((byte)(MetadataTokens.GetRowNumber(modifier) << 2 | 1));
            modifierHandle = metadata.AddTypeSpecification(metadata.GetOrAddBlob(typeSpec));
        }

        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // DEFAULT
        signature.WriteByte(0x01); // parameter count
        signature.WriteByte(0x1F); // CMOD_REQD
        signature.WriteByte((byte)(
            MetadataTokens.GetRowNumber(modifierHandle) << 2
            | (modifierHandle.Kind == HandleKind.TypeSpecification ? 2 : 1)));
        signature.WriteByte(0x01); // VOID
        signature.WriteByte(0x08); // I4
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("set_Value"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildContextInvalidVisibilityImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName:
                metadata.GetOrAddString(
                    "ContextInvalidVisibility.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                "ContextInvalidVisibility"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.NestedPrivate,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("InvalidTopLevel"),
            baseType: default,
            fieldList:
                MetadataTokens.FieldDefinitionHandle(1),
            methodList:
                MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] BuildModifiedVoidExplicitAggregateImage(
        MethodAttributes accessorAccessibility,
        bool modifiedReturns = true)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("ModifiedVoidExplicitAggregate.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ModifiedVoidExplicitAggregate"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle host = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Host"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle contracts = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle interfaceType = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("IRow"));
        TypeReferenceHandle modifier = metadata.AddTypeReference(
            contracts,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("ArbitraryModifier"));
        metadata.AddInterfaceImplementation(host, interfaceType);

        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature)
            .PropertySignature(isInstanceProperty: true)
            .Parameters(0, returnType => returnType.Type().Int32(), _ => { });
        BlobHandle propertySignatureHandle = metadata.GetOrAddBlob(propertySignature);
        bool propertyMapAdded = false;
        AddProperty("RequiredValue", isRequiredModifier: true);
        AddProperty("OptionalValue", isRequiredModifier: false);

        bool eventMapAdded = false;
        AddEvent("RequiredChanged", isRequiredModifier: true);
        AddEvent("OptionalChanged", isRequiredModifier: false);

        return Serialize(metadata);

        void AddProperty(string name, bool isRequiredModifier)
        {
            BlobHandle signature = AddModifiedVoidAccessorSignature(
                isRequiredModifier,
                parameterType: default,
                parameterTypeCode: 0x08);
            MethodDefinitionHandle setter = metadata.AddMethodDefinition(
                accessorAccessibility | ExplicitAccessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"Contracts.IRow.set_{name}"),
                signature,
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            MemberReferenceHandle declaration = metadata.AddMemberReference(
                interfaceType,
                metadata.GetOrAddString($"set_{name}"),
                signature);
            metadata.AddMethodImplementation(host, setter, declaration);
            PropertyDefinitionHandle property = metadata.AddProperty(
                PropertyAttributes.None,
                metadata.GetOrAddString($"Contracts.IRow.{name}"),
                propertySignatureHandle);
            if (!propertyMapAdded)
            {
                metadata.AddPropertyMap(host, property);
                propertyMapAdded = true;
            }
            metadata.AddMethodSemantics(
                property,
                MethodSemanticsAttributes.Setter,
                setter);
        }

        void AddEvent(string name, bool isRequiredModifier)
        {
            BlobHandle signature = AddModifiedVoidAccessorSignature(
                isRequiredModifier,
                interfaceType,
                parameterTypeCode: 0x12);
            MethodDefinitionHandle adder = metadata.AddMethodDefinition(
                accessorAccessibility | ExplicitAccessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"Contracts.IRow.add_{name}"),
                signature,
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            MethodDefinitionHandle remover = metadata.AddMethodDefinition(
                accessorAccessibility | ExplicitAccessorFlags,
                MethodImplAttributes.IL,
                metadata.GetOrAddString($"Contracts.IRow.remove_{name}"),
                signature,
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            metadata.AddMethodImplementation(
                host,
                adder,
                metadata.AddMemberReference(
                    interfaceType,
                    metadata.GetOrAddString($"add_{name}"),
                    signature));
            metadata.AddMethodImplementation(
                host,
                remover,
                metadata.AddMemberReference(
                    interfaceType,
                    metadata.GetOrAddString($"remove_{name}"),
                    signature));
            EventDefinitionHandle @event = metadata.AddEvent(
                EventAttributes.None,
                metadata.GetOrAddString($"Contracts.IRow.{name}"),
                interfaceType);
            if (!eventMapAdded)
            {
                metadata.AddEventMap(host, @event);
                eventMapAdded = true;
            }
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Adder,
                adder);
            metadata.AddMethodSemantics(
                @event,
                MethodSemanticsAttributes.Remover,
                remover);
        }

        BlobHandle AddModifiedVoidAccessorSignature(
            bool isRequiredModifier,
            EntityHandle parameterType,
            byte parameterTypeCode)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x20); // HASTHIS
            signature.WriteByte(0x01); // parameter count
            if (modifiedReturns)
            {
                signature.WriteByte(
                    isRequiredModifier ? (byte)0x1F : (byte)0x20);
                signature.WriteCompressedInteger(
                    MetadataTokens.GetRowNumber(modifier) << 2 | 1);
            }
            signature.WriteByte(0x01); // VOID
            signature.WriteByte(parameterTypeCode);
            if (!parameterType.IsNil)
                signature.WriteCompressedInteger(
                    MetadataTokens.GetRowNumber(parameterType) << 2 | 1);
            return metadata.GetOrAddBlob(signature);
        }
    }

    const MethodAttributes ExplicitAccessorFlags =
        MethodAttributes.Virtual
        | MethodAttributes.Final
        | MethodAttributes.NewSlot
        | MethodAttributes.SpecialName
        | MethodAttributes.HideBySig;

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static string EmitMixedAbstractionAggregates()
    {
        var assemblyName = new AssemblyName("MixedAbstractionAggregates");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        const MethodAttributes Shared =
            MethodAttributes.Public
            | MethodAttributes.Virtual
            | MethodAttributes.NewSlot
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;

        void DefineAggregates(
            string typeName,
            bool abstractGetter,
            bool abstractSetter,
            bool publicEvent = true)
        {
            var builder = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Abstract);

            var getter = builder.DefineMethod(
                "get_Value",
                abstractGetter ? Shared | MethodAttributes.Abstract : Shared,
                typeof(int),
                Type.EmptyTypes);
            if (!abstractGetter)
            {
                var getterIl = getter.GetILGenerator();
                getterIl.Emit(OpCodes.Ldc_I4_0);
                getterIl.Emit(OpCodes.Ret);
            }

            var setter = builder.DefineMethod(
                "set_Value",
                abstractSetter ? Shared | MethodAttributes.Abstract : Shared,
                typeof(void),
                [typeof(int)]);
            if (!abstractSetter)
                setter.GetILGenerator().Emit(OpCodes.Ret);

            var property = builder.DefineProperty(
                "Value",
                PropertyAttributes.None,
                typeof(int),
                null);
            property.SetGetMethod(getter);
            property.SetSetMethod(setter);

            MethodAttributes eventAttributes = publicEvent
                ? Shared
                : (Shared & ~MethodAttributes.MemberAccessMask)
                    | MethodAttributes.Private;
            var adder = builder.DefineMethod(
                "add_Changed",
                abstractGetter
                    ? eventAttributes | MethodAttributes.Abstract
                    : eventAttributes,
                typeof(void),
                [typeof(Action)]);
            if (!abstractGetter)
                adder.GetILGenerator().Emit(OpCodes.Ret);

            var remover = builder.DefineMethod(
                "remove_Changed",
                abstractSetter
                    ? eventAttributes | MethodAttributes.Abstract
                    : eventAttributes,
                typeof(void),
                [typeof(Action)]);
            if (!abstractSetter)
                remover.GetILGenerator().Emit(OpCodes.Ret);

            var @event = builder.DefineEvent("Changed", EventAttributes.None, typeof(Action));
            @event.SetAddOnMethod(adder);
            @event.SetRemoveOnMethod(remover);
            builder.CreateType();
        }

        DefineAggregates(
            "MixedAbstraction",
            abstractGetter: true,
            abstractSetter: false,
            publicEvent: false);
        DefineAggregates("UniformAbstract", abstractGetter: true, abstractSetter: true);
        DefineAggregates("UniformConcrete", abstractGetter: false, abstractSetter: false);

        // The legitimate peer: an interface may pair an abstract accessor with a defaulted one.
        var interfaceBuilder = module.DefineType(
            "MixedAbstractionInterface",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceGetter = interfaceBuilder.DefineMethod(
            "get_Value",
            Shared | MethodAttributes.Abstract,
            typeof(int),
            Type.EmptyTypes);
        var interfaceSetter = interfaceBuilder.DefineMethod(
            "set_Value",
            Shared,
            typeof(void),
            [typeof(int)]);
        interfaceSetter.GetILGenerator().Emit(OpCodes.Ret);
        var interfaceProperty = interfaceBuilder.DefineProperty(
            "Value",
            PropertyAttributes.None,
            typeof(int),
            null);
        interfaceProperty.SetGetMethod(interfaceGetter);
        interfaceProperty.SetSetMethod(interfaceSetter);
        interfaceBuilder.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"MixedAbstractionAggregates-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    /// <summary>
    /// A property whose getter carries <see cref="MethodAttributes.Abstract"/> and a nonzero
    /// method RVA. No compiler emits it, and Reflection.Emit refuses to, so the image is built
    /// row by row with a body offset that resolves to a real RVA.
    /// </summary>
    static byte[] BuildAbstractAccessorWithBodyImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("AbstractAccessorBody.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("AbstractAccessorBody"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("AbstractBody"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        var body = new InstructionEncoder(new BlobBuilder());
        body.LoadConstantI4(0);
        body.OpCode(ILOpCode.Ret);
        int bodyOffset = bodies.AddMethodBody(body);

        var getterSignature = new BlobBuilder();
        new BlobEncoder(getterSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, returnType => returnType.Type().Int32(), _ => { });
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.Private
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(getterSignature),
            bodyOffset,
            parameterList: MetadataTokens.ParameterHandle(1));

        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature)
            .PropertySignature(isInstanceProperty: true)
            .Parameters(0, returnType => returnType.Type().Int32(), _ => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(property, MethodSemanticsAttributes.Getter, getter);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            il,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    /// <summary>
    /// Two explicit-interface implementers whose accessors and MethodImpl rows are identical
    /// and truthful. The first declares Property and Event rows that disagree with those
    /// accessors; the second declares rows that agree.
    /// </summary>
    static string EmitRowSignatureMismatchedExplicitAggregates()
    {
        var assemblyName = new AssemblyName("RowSignatureExplicitAggregates");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        const MethodAttributes InterfaceAccessor =
            MethodAttributes.Public
            | MethodAttributes.Abstract
            | MethodAttributes.Virtual
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;
        const MethodAttributes ExplicitAccessor =
            MethodAttributes.Private
            | MethodAttributes.Virtual
            | MethodAttributes.Final
            | MethodAttributes.NewSlot
            | MethodAttributes.SpecialName
            | MethodAttributes.HideBySig;

        var interfaceBuilder = module.DefineType(
            "IRowSignature",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceGetter = interfaceBuilder.DefineMethod(
            "get_Value",
            InterfaceAccessor,
            typeof(int),
            Type.EmptyTypes);
        interfaceBuilder
            .DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
            .SetGetMethod(interfaceGetter);
        var interfaceAdder = interfaceBuilder.DefineMethod(
            "add_Changed",
            InterfaceAccessor,
            typeof(void),
            [typeof(Action)]);
        var interfaceRemover = interfaceBuilder.DefineMethod(
            "remove_Changed",
            InterfaceAccessor,
            typeof(void),
            [typeof(Action)]);
        var interfaceEvent = interfaceBuilder.DefineEvent(
            "Changed",
            EventAttributes.None,
            typeof(Action));
        interfaceEvent.SetAddOnMethod(interfaceAdder);
        interfaceEvent.SetRemoveOnMethod(interfaceRemover);
        var interfaceType = interfaceBuilder.CreateType();

        void DefineImplementer(string typeName, bool truthfulRows)
        {
            var builder = module.DefineType(typeName, TypeAttributes.Public);
            builder.AddInterfaceImplementation(interfaceType);

            var getter = builder.DefineMethod(
                "IRowSignature.get_Value",
                ExplicitAccessor,
                typeof(int),
                Type.EmptyTypes);
            var getterIl = getter.GetILGenerator();
            getterIl.Emit(OpCodes.Ldc_I4_0);
            getterIl.Emit(OpCodes.Ret);
            builder
                .DefineProperty(
                    "IRowSignature.Value",
                    PropertyAttributes.None,
                    truthfulRows ? typeof(int) : typeof(string),
                    null)
                .SetGetMethod(getter);
            builder.DefineMethodOverride(getter, interfaceType.GetMethod("get_Value")!);

            var adder = builder.DefineMethod(
                "IRowSignature.add_Changed",
                ExplicitAccessor,
                typeof(void),
                [typeof(Action)]);
            adder.GetILGenerator().Emit(OpCodes.Ret);
            var remover = builder.DefineMethod(
                "IRowSignature.remove_Changed",
                ExplicitAccessor,
                typeof(void),
                [typeof(Action)]);
            remover.GetILGenerator().Emit(OpCodes.Ret);
            var @event = builder.DefineEvent(
                "IRowSignature.Changed",
                EventAttributes.None,
                truthfulRows ? typeof(Action) : typeof(EventHandler));
            @event.SetAddOnMethod(adder);
            @event.SetRemoveOnMethod(remover);
            builder.DefineMethodOverride(adder, interfaceType.GetMethod("add_Changed")!);
            builder.DefineMethodOverride(remover, interfaceType.GetMethod("remove_Changed")!);
            builder.CreateType();
        }

        DefineImplementer("RowSignatureMismatch", truthfulRows: false);
        DefineImplementer("RowSignatureMatch", truthfulRows: true);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"RowSignatureExplicitAggregates-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static string EmitSignatureIncompatibleMethodImplProperty()
    {
        var assemblyName = new AssemblyName("SignatureIncompatibleMethodImplProperty");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);

        var interfaceBuilder = module.DefineType(
            "IBadProperty",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var interfaceGetter = interfaceBuilder.DefineMethod(
            "get_Value",
            MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(int),
            Type.EmptyTypes);
        interfaceBuilder
            .DefineProperty("Value", PropertyAttributes.None, typeof(int), null)
            .SetGetMethod(interfaceGetter);
        var interfaceType = interfaceBuilder.CreateType();

        var type = module.DefineType("BadProperty", TypeAttributes.Public);
        type.AddInterfaceImplementation(interfaceType);
        var getter = type.DefineMethod(
            "IBadProperty.get_Value",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            typeof(string),
            Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldstr, "bad");
        il.Emit(OpCodes.Ret);
        type
            .DefineProperty("IBadProperty.Value", PropertyAttributes.None, typeof(string), null)
            .SetGetMethod(getter);
        type.DefineMethodOverride(getter, interfaceType.GetMethod("get_Value")!);
        type.CreateType();

        string path = Path.Combine(
            Path.GetTempPath(),
            $"SignatureIncompatibleMethodImplProperty-{Guid.NewGuid():N}.dll");
        assembly.Save(path);
        return path;
    }

    static TypeDefinition GetTypeDefinition(Type type)
        => Reader.GetTypeDefinition(GetTypeDefinitionHandle(type));

    static TypeDefinitionHandle GetTypeDefinitionHandle(Type type)
        => GetTypeDefinitionHandle(Reader, type.FullName!.Replace('+', '.'));

    static TypeDefinitionHandle GetTypeDefinitionHandle(MetadataReader reader, string fullName)
    {
        var metadataName = fullName.Replace('+', '.');
        metadataName = StripGenericArity(metadataName);
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (StripGenericArity(TypeResolver.GetFullName(reader, definition)) == metadataName)
                return handle;
        }

        throw new InvalidOperationException($"Type '{metadataName}' was not found.");
    }

    static MethodDefinition GetMethod(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetMethods())
        {
            var method = Reader.GetMethodDefinition(handle);
            if (Reader.GetString(method.Name) == name)
                return method;
        }

        throw new InvalidOperationException($"Method '{name}' was not found.");
    }

    static PropertyDefinition GetProperty(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetProperties())
        {
            var property = Reader.GetPropertyDefinition(handle);
            if (Reader.GetString(property.Name) == name)
                return property;
        }

        throw new InvalidOperationException($"Property '{name}' was not found.");
    }

    static FieldDefinition GetField(TypeDefinition type, string name)
    {
        foreach (var handle in type.GetFields())
        {
            var field = Reader.GetFieldDefinition(handle);
            if (Reader.GetString(field.Name) == name)
                return field;
        }

        throw new InvalidOperationException($"Field '{name}' was not found.");
    }

    static string StripGenericArity(string value)
    {
        var tick = value.IndexOf('`');
        while (tick >= 0)
        {
            var end = tick + 1;
            while (end < value.Length && char.IsDigit(value[end]))
                end++;
            value = value[..tick] + value[end..];
            tick = value.IndexOf('`');
        }

        return value;
    }
}

public class MetadataDeclarationQueryFixtures
{
    private readonly int _count = 1;

    [return: System.Diagnostics.CodeAnalysis.NotNull]
    protected virtual string? ProtectedVirtual(
        [MarshalAs(UnmanagedType.LPWStr)] string? value,
        int count = 2) => value ?? count.ToString();

    public decimal DecimalDefault(decimal amount = 5m) => amount;

    public int Count() => _count;

    public int @class() => 0;

    public int @while { get; set; }

    public int Restricted { get; private set; }

    public string InitValue { get; init; } = "";

    public string PropertyWithReturnNotNull
    {
        [return: System.Diagnostics.CodeAnalysis.NotNull]
        get => "value";
    }

    public int @event = 1;

    public void QualifiedKeyword(@namespace @class, string text = ".namespace") { }

    public void GlobalKeyword(
        [global::GlobalType(typeof(global::@class), (global::@event)1)] global::@class value,
        List<global::@class>? values = null,
        global::@event mode = (global::@event)1,
        string text = "a\"b.class")
    {
    }

    public interface INullableExplicitAggregateMetadataFixture<T>
    {
        T Value { get; }
        event Action Changed;
    }

    public interface IMixedAbstractionSurface
    {
        int Value { get; }

        event Action Changed;
    }

    public interface IExplicitRowSignature
    {
        int Value { get; init; }

        string this[int index] { get; }

        event Action Changed;
    }

    public sealed class ExplicitRowSignatureSurface : IExplicitRowSignature
    {
        int IExplicitRowSignature.Value
        {
            get => 0;
            init { }
        }

        string IExplicitRowSignature.this[int index] => index.ToString();

        event Action IExplicitRowSignature.Changed
        {
            add { }
            remove { }
        }
    }

    public sealed class NullableExplicitAggregateMetadataFixture
        : INullableExplicitAggregateMetadataFixture<int?>
    {
        public int Ordinary { get; set; }

        public int Initial { get; init; }

        int? INullableExplicitAggregateMetadataFixture<int?>.Value => null;

        event Action INullableExplicitAggregateMetadataFixture<int?>.Changed
        {
            add { }
            remove { }
        }
    }

    public int get_Orphan() => 0;

    public interface IExplicitSurface
    {
        int Value { get; }
        event EventHandler Changed;
    }

    public sealed class ExplicitSurface : IExplicitSurface
    {
        int IExplicitSurface.Value => 42;

        event EventHandler IExplicitSurface.Changed
        {
            [return: System.Diagnostics.CodeAnalysis.NotNull]
            add { }

            [return: System.Diagnostics.CodeAnalysis.MaybeNull]
            remove { }
        }
    }

    public interface IImplicitSurface
    {
        int Value { get; }
    }

    public sealed class SealedImplicitSurface : IImplicitSurface
    {
        public int Value => 42;
    }

    public readonly struct StructImplicitSurface : IImplicitSurface
    {
        public int Value => 42;
    }

    public interface IGenericSurface<TLeft, TRight>
    {
        TRight Value { get; }
        event Action<TLeft> Changed;
    }

    public sealed class GenericSurface<TLeft, TRight> : IGenericSurface<TLeft, TRight>
    {
        TRight IGenericSurface<TLeft, TRight>.Value => default!;

        event Action<TLeft> IGenericSurface<TLeft, TRight>.Changed
        {
            add { }
            remove { }
        }
    }

    public interface IAliasSurface<T>
    {
        T Value { get; }
        event Action<T> Changed;
    }

    public sealed class StringSurface : IAliasSurface<string>
    {
        string IAliasSurface<string>.Value => "";

        event Action<string> IAliasSurface<string>.Changed
        {
            add { }
            remove { }
        }
    }

    public sealed class StringArraySurface : IAliasSurface<string[]>
    {
        string[] IAliasSurface<string[]>.Value => [];

        event Action<string[]> IAliasSurface<string[]>.Changed
        {
            add { }
            remove { }
        }
    }

    public void SyntaxKeywordTypes(
        global::@delegate delegateValue,
        global::@readonly readonlyValue,
        global::@scoped scopedValue)
    {
    }

    public Container<int>.Row<string> NestedGeneric(Container<int>.Row<string> value)
        => value;

    public volatile int VolatileField;

    public int PlainField;

    public void StructConstraint<T>() where T : struct { }

    public void ClassNewConstraint<T>() where T : class, new() { }

    public abstract class AbstractBase
    {
        protected abstract string Name { get; set; }
    }

    public unsafe interface IUnsafeSurface
    {
        void Consume(int* pointer);
    }

    public unsafe class UnsafeFields
    {
        public int* Pointer;
        public int* Value { get; set; }
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }
    }

    public class Container<T>
    {
        public class Row<U>
        {
        }
    }

    public class @namespace
    {
    }
}
