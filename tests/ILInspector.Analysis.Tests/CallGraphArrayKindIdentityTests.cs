using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed partial class CallGraphArrayKindIdentityTests
{
    [Fact]
    public void Resolve_PreservesArrayKindAcrossExtractedApiAndMemberRefSelectors()
    {
        byte[] image = BuildArrayKindImage();
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "ArrayCallGraphKinds");
        ApiMember[] members =
        [
            .. type.Members
                .Where(member => member.Kind == "method")
                .OrderBy(member => member.MetadataToken),
        ];

        Assert.Equal(11, members.Length);
        var selectors = new Dictionary<
            int,
            (CallGraphMemberSelector Api, CallGraphMemberSelector Reference)>();
        foreach (ApiMember member in members)
        {
            int token = Assert.IsType<int>(member.MetadataToken);
            MemberRef reference = MemberResolver.ResolveMethod(
                reader,
                MetadataTokens.EntityHandle(token),
                GenericScope.Empty);
            CallGraphMemberSelector apiSelector =
                CallGraphMemberResolver.CreateSelector(type, member);
            CallGraphMemberSelector referenceSelector =
                CallGraphMemberResolver.CreateSelector(reference);
            selectors.Add(token, (apiSelector, referenceSelector));

            Assert.Equal(referenceSelector.ParameterTypes, apiSelector.ParameterTypes);
            Assert.Equal(referenceSelector.ReturnType, apiSelector.ReturnType);
            Assert.Equal(referenceSelector.Key, apiSelector.Key);
            Assert.Same(
                member,
                CallGraphMemberResolver.Resolve(
                    type,
                    referenceSelector.Name,
                    referenceSelector.Key)!
                    .Member);
            Assert.Same(
                member,
                CallGraphMemberResolver.Resolve(
                    type,
                    referenceSelector.Name,
                    referenceSelector.Key,
                    metadataToken: token)!
                    .Member);
        }

        ApiMember vector = Assert.Single(
            members,
            member => member.Name == "M"
                && Parameter(member).Type == "int[]");
        ApiMember nonSz = Assert.Single(
            members,
            member => member.Name == "M"
                && Parameter(member).Type == "int[*]");
        CallGraphMemberSelector vectorSelector =
            selectors[vector.MetadataToken!.Value].Reference;
        CallGraphMemberSelector nonSzSelector =
            selectors[nonSz.MetadataToken!.Value].Reference;

        Assert.Equal("System.Int32[]", vectorSelector.StructuralParameterTypes[0]);
        Assert.Equal("System.Int32[*]", nonSzSelector.StructuralParameterTypes[0]);
        Assert.Equal(vectorSelector.ParameterTypes, nonSzSelector.ParameterTypes);
        Assert.NotEqual(vectorSelector.Key, nonSzSelector.Key);
        AssertStructuralParameter(
            type,
            selectors,
            "Nested",
            "System.Collections.Generic.List{System.Int32[*]}");
        AssertStructuralParameter(
            type,
            selectors,
            "Pointer",
            "System.Int32[*]*");
        AssertStructuralParameter(
            type,
            selectors,
            "ByRef",
            "System.Int32[*]@");
        AssertStructuralParameter(
            type,
            selectors,
            "Tuple",
            "System.ValueTuple{System.Int32[*],System.Int32[]}");
        AssertStructuralParameter(
            type,
            selectors,
            "Generic",
            "M0[*]");
        AssertStructuralParameter(
            type,
            selectors,
            "ModifiedVector",
            "modreq{System.Runtime.CompilerServices.IsVolatile}{System.Int32}[][]");
        AssertStructuralParameter(
            type,
            selectors,
            "ModifiedMd1",
            "modreq{System.Runtime.CompilerServices.IsVolatile}{System.Int32}[][*]");
        Assert.Equal(
            "System.Int32[*]",
            Selector(type, selectors, "ReturnMd1").StructuralReturnType);

        Parameter(nonSz).StructuralType = null;
        Assert.Null(
            CallGraphMemberResolver.Resolve(
                type,
                nonSzSelector.Name,
                nonSzSelector.Key));
        Assert.Same(
            nonSz,
            CallGraphMemberResolver.Resolve(
                type,
                nonSzSelector.Name,
                nonSzSelector.Key,
                metadataToken: nonSz.MetadataToken)!
                .Member);
    }

    [Fact]
    public void Resolve_PreservesLiteralArrayNamesAcrossTypeShapes()
    {
        byte[] image = BuildLiteralArrayNameImage();
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "ArrayCallGraphKinds");
        ApiMember[] members =
        [
            .. type.Members
                .Where(member => member.Name == "M")
                .OrderBy(member => member.MetadataToken),
        ];

        Assert.Equal(9, members.Length);
        string[] expected =
        [
            "N.A[][*]",
            @"N.A\[\][*]",
            @"N.A\[\][]",
            "N.A[][]",
            @"System.Collections.Generic.List{N.A\[\]}",
            "System.Collections.Generic.List{N.A[]}",
            @"N.A\[\]",
            "N.A[]",
            @"N.G\[\]{N.A}",
        ];
        var selectors = new List<CallGraphMemberSelector>(members.Length);
        for (int index = 0; index < members.Length; index++)
        {
            ApiMember member = members[index];
            int token = Assert.IsType<int>(member.MetadataToken);
            MemberRef reference = MemberResolver.ResolveMethod(
                reader,
                MetadataTokens.EntityHandle(token),
                GenericScope.Empty);
            CallGraphMemberSelector apiSelector =
                CallGraphMemberResolver.CreateSelector(type, member);
            CallGraphMemberSelector referenceSelector =
                CallGraphMemberResolver.CreateSelector(reference);
            selectors.Add(apiSelector);

            Assert.Equal(
                expected[index],
                apiSelector.StructuralParameterTypes[0]);
            Assert.Equal(referenceSelector.Key, apiSelector.Key);
            Assert.Same(
                member,
                CallGraphMemberResolver.Resolve(
                    type,
                    referenceSelector.Name,
                    referenceSelector.Key)!
                    .Member);
            Assert.Same(
                member,
                CallGraphMemberResolver.Resolve(
                    type,
                    referenceSelector.Name,
                    referenceSelector.Key,
                    metadataToken: token)!
                    .Member);
        }

        Assert.Equal(
            selectors.Count,
            selectors.Select(selector => selector.Key).Distinct().Count());

        Assembly assembly = Assembly.Load(image);
        Type loadedType = assembly.GetType(
            "N.ArrayCallGraphKinds",
            throwOnError: true)!;
        MethodInfo[] loadedMethods = loadedType.GetMethods(
            BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);
        Assert.Equal(members.Length, loadedMethods.Length);
        foreach (MethodInfo method in loadedMethods)
        {
            _ = method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
                _ = parameter.ParameterType;
        }
    }

    [Fact]
    public void Resolve_PreservesArrayKindForEventAccessors()
    {
        byte[] image = BuildEventArrayKindImage();
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "ArrayCallGraphEvents");

        ApiMember nonSzEvent = Assert.Single(
            type.Members,
            member => member is { Kind: "event", Name: "G" });
        ApiMember vectorEvent = Assert.Single(
            type.Members,
            member => member is { Kind: "event", Name: "H" });
        ApiMember literalNameEvent = Assert.Single(
            type.Members,
            member => member is { Kind: "event", Name: "E" });
        Assert.Equal(
            "System.Action{System.Int32[*]}",
            nonSzEvent.SignatureModel!.StructuralReturnType);
        Assert.Null(vectorEvent.SignatureModel!.StructuralReturnType);
        Assert.Equal(
            @"N.D\[\]",
            literalNameEvent.SignatureModel!.StructuralReturnType);

        CallGraphMemberBodySelector[] bodies =
        [
            .. type.Members.SelectMany(
                member => CallGraphMemberResolver.CreateBodySelectors(
                    type,
                    member)),
        ];
        Assert.Equal(8, bodies.Length);
        foreach (CallGraphMemberBodySelector body in bodies)
        {
            MemberRef reference = MemberResolver.ResolveMethod(
                reader,
                MetadataTokens.EntityHandle(body.BodyToken),
                GenericScope.Empty);
            CallGraphMemberSelector referenceSelector =
                CallGraphMemberResolver.CreateSelector(reference);

            Assert.Equal(referenceSelector.Name, body.MemberName);
            Assert.Equal(referenceSelector.Key, body.SelectorKey);
            Assert.Equal(
                body.BodyToken,
                CallGraphMemberResolver.Resolve(
                    type,
                    referenceSelector.Name,
                    referenceSelector.Key)!
                    .BodyToken);
            Assert.Equal(
                body.BodyToken,
                CallGraphMemberResolver.Resolve(
                    type,
                    referenceSelector.Name,
                    referenceSelector.Key,
                    metadataToken: body.BodyToken)!
                    .BodyToken);
        }

        Assert.Equal(
            bodies.Length,
            bodies.Select(body => body.SelectorKey).Distinct().Count());

        Assembly assembly = Assembly.Load(image);
        Type loadedType = assembly.GetType(
            "N.ArrayCallGraphEvents",
            throwOnError: true)!;
        EventInfo[] loadedEvents = loadedType.GetEvents(
            BindingFlags.Public
                | BindingFlags.Instance
                | BindingFlags.DeclaredOnly);
        Assert.Equal(3, loadedEvents.Length);
        foreach (EventInfo loadedEvent in loadedEvents)
            Assert.NotNull(loadedEvent.EventHandlerType);
    }

    static void AssertStructuralParameter(
        ApiType type,
        IReadOnlyDictionary<
            int,
            (CallGraphMemberSelector Api, CallGraphMemberSelector Reference)> selectors,
        string memberName,
        string expected) =>
        Assert.Equal(
            expected,
            Selector(type, selectors, memberName).StructuralParameterTypes[0]);

    static CallGraphMemberSelector Selector(
        ApiType type,
        IReadOnlyDictionary<
            int,
            (CallGraphMemberSelector Api, CallGraphMemberSelector Reference)> selectors,
        string memberName)
    {
        ApiMember member = Assert.Single(
            type.Members,
            candidate => candidate.Name == memberName);
        return selectors[member.MetadataToken!.Value].Reference;
    }

    static ApiParameter Parameter(ApiMember member) =>
        Assert.Single(member.SignatureModel!.Parameters);

    static byte[] BuildArrayKindImage()
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(8, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle systemCollections =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Collections"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle list = metadata.AddTypeReference(
            systemCollections,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
        TypeReferenceHandle valueTuple = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("ValueTuple`2"));
        TypeReferenceHandle isVolatile = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("IsVolatile"));

        byte[] md1 = MdArray(Int32, rank: 1);
        byte[] sz = Sz(Int32);
        byte[] modifiedVector = Sz(RequiredModifier(isVolatile, Int32));
        byte[] nested = GenericInstance(
            isValueType: false,
            list,
            md1);
        byte[] tuple = GenericInstance(
            isValueType: true,
            valueTuple,
            md1,
            sz);

        return BuildImage(
            [
                new("M", sz, Void, IsGeneric: false),
                new("M", md1, Void, IsGeneric: false),
                new("Nested", nested, Void, IsGeneric: false),
                new("Pointer", Pointer(md1), Void, IsGeneric: false),
                new("ByRef", ByRef(md1), Void, IsGeneric: false),
                new("Tuple", tuple, Void, IsGeneric: false),
                new(
                    "Generic",
                    MdArray(MethodGeneric0, rank: 1),
                    Void,
                    IsGeneric: true),
                new(
                    "ModifiedVector",
                    Sz(modifiedVector),
                    Void,
                    IsGeneric: false),
                new(
                    "ModifiedMd1",
                    MdArray(modifiedVector, rank: 1),
                    Void,
                    IsGeneric: false),
                new("ReturnVector", null, sz, IsGeneric: false),
                new("ReturnMd1", null, md1, IsGeneric: false),
            ],
            metadata);
    }

    static byte[] BuildLiteralArrayNameImage()
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        AssemblyReferenceHandle systemCollections =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Collections"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        TypeReferenceHandle list = metadata.AddTypeReference(
            systemCollections,
            metadata.GetOrAddString("System.Collections.Generic"),
            metadata.GetOrAddString("List`1"));
        TypeDefinitionHandle ordinary = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("A"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle literalGeneric = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("G[]`1"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddGenericParameter(
            literalGeneric,
            GenericParameterAttributes.None,
            metadata.GetOrAddString("T"),
            index: 0);
        TypeDefinitionHandle literal = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("A[]"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        return BuildImage(
            [
                new(
                    "M",
                    MdArray(Sz(Class(ordinary)), rank: 1),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    MdArray(Class(literal), rank: 1),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    Sz(Class(literal)),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    Sz(Sz(Class(ordinary))),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    GenericInstance(
                        isValueType: false,
                        list,
                        Class(literal)),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    GenericInstance(
                        isValueType: false,
                        list,
                        Sz(Class(ordinary))),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    Class(literal),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    Sz(Class(ordinary)),
                    Void,
                    IsGeneric: false),
                new(
                    "M",
                    GenericInstance(
                        isValueType: false,
                        literalGeneric,
                        Class(ordinary)),
                    Void,
                    IsGeneric: false),
            ],
            metadata);
    }

    static byte[] BuildEventArrayKindImage()
    {
        var metadata = CreateMetadata();
        AssemblyReferenceHandle systemRuntime =
            metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(8, 0, 0, 0),
                default,
                default,
                default,
                default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        TypeReferenceHandle multicastDelegate = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("MulticastDelegate"));
        TypeReferenceHandle action = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Action`1"));
        TypeDefinitionHandle literalDelegate = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("D[]"),
            multicastDelegate,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(2);
        constructorSignature.WriteBytes(Void);
        constructorSignature.WriteByte(0x1c);
        constructorSignature.WriteByte(0x18);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.SpecialName
                | MethodAttributes.RTSpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        var invokeSignature = new BlobBuilder();
        invokeSignature.WriteByte(0x20);
        invokeSignature.WriteCompressedInteger(0);
        invokeSignature.WriteBytes(Void);
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Invoke"),
            metadata.GetOrAddBlob(invokeSignature),
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

        TypeDefinitionHandle ordinaryDelegate = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("D"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));
        TypeDefinitionHandle probe = metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("ArrayCallGraphEvents"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(3));

        byte[] actionOfMd1 = GenericInstance(
            isValueType: false,
            action,
            MdArray(Int32, rank: 1));
        byte[] actionOfVector = GenericInstance(
            isValueType: false,
            action,
            Sz(Int32));
        int parameterRow = 1;
        MethodDefinitionHandle AddMethod(
            string name,
            byte[] parameterType,
            bool isEventAccessor)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(0x20);
            signature.WriteCompressedInteger(1);
            signature.WriteBytes(Void);
            signature.WriteBytes(parameterType);

            MethodAttributes attributes =
                MethodAttributes.Public
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot;
            if (isEventAccessor)
                attributes |= MethodAttributes.SpecialName;
            MethodDefinitionHandle handle =
                metadata.AddMethodDefinition(
                    attributes,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(name),
                    metadata.GetOrAddBlob(signature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(parameterRow));
            metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("value"),
                sequenceNumber: 1);
            parameterRow++;
            return handle;
        }

        MethodDefinitionHandle addG =
            AddMethod("add_G", actionOfMd1, isEventAccessor: true);
        MethodDefinitionHandle removeG =
            AddMethod("remove_G", actionOfMd1, isEventAccessor: true);
        MethodDefinitionHandle addH =
            AddMethod("add_H", actionOfVector, isEventAccessor: true);
        MethodDefinitionHandle removeH =
            AddMethod("remove_H", actionOfVector, isEventAccessor: true);
        MethodDefinitionHandle addE =
            AddMethod(
                "add_E",
                Class(literalDelegate),
                isEventAccessor: true);
        MethodDefinitionHandle removeE =
            AddMethod(
                "remove_E",
                Class(literalDelegate),
                isEventAccessor: true);
        AddMethod(
            "add_E",
            Sz(Class(ordinaryDelegate)),
            isEventAccessor: false);
        AddMethod(
            "remove_E",
            Sz(Class(ordinaryDelegate)),
            isEventAccessor: false);

        metadata.AddEventMap(
            probe,
            MetadataTokens.EventDefinitionHandle(1));

        TypeSpecificationHandle md1EventType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(actionOfMd1));
        TypeSpecificationHandle vectorEventType =
            metadata.AddTypeSpecification(
                metadata.GetOrAddBlob(actionOfVector));
        EventDefinitionHandle eventG = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("G"),
            md1EventType);
        metadata.AddMethodSemantics(
            eventG,
            MethodSemanticsAttributes.Adder,
            addG);
        metadata.AddMethodSemantics(
            eventG,
            MethodSemanticsAttributes.Remover,
            removeG);
        EventDefinitionHandle eventH = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("H"),
            vectorEventType);
        metadata.AddMethodSemantics(
            eventH,
            MethodSemanticsAttributes.Adder,
            addH);
        metadata.AddMethodSemantics(
            eventH,
            MethodSemanticsAttributes.Remover,
            removeH);
        EventDefinitionHandle eventE = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("E"),
            literalDelegate);
        metadata.AddMethodSemantics(
            eventE,
            MethodSemanticsAttributes.Adder,
            addE);
        metadata.AddMethodSemantics(
            eventE,
            MethodSemanticsAttributes.Remover,
            removeE);

        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildImage(
        IReadOnlyList<MethodSpec> methods,
        MetadataBuilder metadata)
    {
        var methodHandles =
            new List<MethodDefinitionHandle>(methods.Count);
        int parameterRow = 1;
        foreach (MethodSpec method in methods)
        {
            var signature = new BlobBuilder();
            signature.WriteByte(
                method.IsGeneric ? (byte)0x30 : (byte)0x20);
            if (method.IsGeneric)
                signature.WriteCompressedInteger(1);
            signature.WriteCompressedInteger(
                method.ParameterType is null ? 0 : 1);
            signature.WriteBytes(method.ReturnType);
            if (method.ParameterType is not null)
                signature.WriteBytes(method.ParameterType);

            MethodDefinitionHandle methodHandle =
                metadata.AddMethodDefinition(
                    MethodAttributes.Public
                        | MethodAttributes.Abstract
                        | MethodAttributes.Virtual
                        | MethodAttributes.HideBySig
                        | MethodAttributes.NewSlot,
                    MethodImplAttributes.IL,
                    metadata.GetOrAddString(method.Name),
                    metadata.GetOrAddBlob(signature),
                    bodyOffset: -1,
                    MetadataTokens.ParameterHandle(parameterRow));
            methodHandles.Add(methodHandle);
            if (method.ParameterType is not null)
            {
                metadata.AddParameter(
                    ParameterAttributes.None,
                    metadata.GetOrAddString("value"),
                    sequenceNumber: 1);
                parameterRow++;
            }
            if (method.IsGeneric)
            {
                metadata.AddGenericParameter(
                    methodHandle,
                    GenericParameterAttributes.None,
                    metadata.GetOrAddString("T"),
                    index: 0);
            }
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Interface,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("ArrayCallGraphKinds"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            methodHandles[0]);

        var peBuilder = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            new BlobBuilder());
        var image = new BlobBuilder();
        peBuilder.Serialize(image);
        return image.ToArray();
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddAssembly(
            metadata.GetOrAddString("ArrayCallGraphKinds"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddModule(
            generation: 0,
            metadata.GetOrAddString("ArrayCallGraphKinds.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static byte[] GenericInstance(
        bool isValueType,
        TypeReferenceHandle definition,
        params byte[][] arguments)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(isValueType ? (byte)0x11 : (byte)0x12);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(definition) << 2) | 1);
        signature.WriteCompressedInteger(arguments.Length);
        foreach (byte[] argument in arguments)
            signature.WriteBytes(argument);
        return signature.ToArray();
    }

    static byte[] GenericInstance(
        bool isValueType,
        TypeDefinitionHandle definition,
        params byte[][] arguments)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x15);
        signature.WriteByte(isValueType ? (byte)0x11 : (byte)0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(definition) << 2);
        signature.WriteCompressedInteger(arguments.Length);
        foreach (byte[] argument in arguments)
            signature.WriteBytes(argument);
        return signature.ToArray();
    }

    static byte[] RequiredModifier(
        TypeReferenceHandle modifier,
        byte[] inner)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x1f);
        signature.WriteCompressedInteger(
            (MetadataTokens.GetRowNumber(modifier) << 2) | 1);
        signature.WriteBytes(inner);
        return signature.ToArray();
    }

    static byte[] Class(TypeDefinitionHandle definition)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x12);
        signature.WriteCompressedInteger(
            MetadataTokens.GetRowNumber(definition) << 2);
        return signature.ToArray();
    }

    static byte[] MdArray(byte[] elementType, int rank)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x14);
        signature.WriteBytes(elementType);
        signature.WriteCompressedInteger(rank);
        signature.WriteCompressedInteger(0);
        signature.WriteCompressedInteger(0);
        return signature.ToArray();
    }

    static byte[] Sz(byte[] elementType) => [0x1d, .. elementType];

    static byte[] Pointer(byte[] elementType) => [0x0f, .. elementType];

    static byte[] ByRef(byte[] elementType) => [0x10, .. elementType];

    static readonly byte[] Void = [0x01];
    static readonly byte[] Int32 = [0x08];
    static readonly byte[] MethodGeneric0 = [0x1e, 0x00];

    sealed record MethodSpec(
        string Name,
        byte[]? ParameterType,
        byte[] ReturnType,
        bool IsGeneric);
}
