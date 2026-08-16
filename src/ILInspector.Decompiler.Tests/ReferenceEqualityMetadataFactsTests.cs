using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class ReferenceEqualityMetadataFactsTests
{
    [Fact]
    public void DistinctAssemblyVersions_DoNotShareOperatorFacts()
    {
        string directory = Directory.CreateTempSubdirectory("reference-equality-identities-").FullName;
        try
        {
            string v1 = Path.Combine(directory, "v1", "Twin.dll");
            string v2 = Path.Combine(directory, "v2", "Twin.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(v1)!);
            Directory.CreateDirectory(Path.GetDirectoryName(v2)!);
            File.WriteAllBytes(v1, BuildTwin(new Version(1, 0, 0, 0), hasEquality: false));
            File.WriteAllBytes(v2, BuildTwin(new Version(2, 0, 0, 0), hasEquality: true));
            string consumer = Path.Combine(directory, "Consumer.dll");
            File.WriteAllBytes(consumer, BuildIdentityConsumer());

            AssertOrder(consumer, v1, v2, "V1Identity", "V2Identity");
            AssertOrder(consumer, v1, v2, "V2Identity", "V1Identity");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WideInterfaceHierarchy_ExhaustsWorkBudget()
    {
        string path = Path.Combine(
            Directory.CreateTempSubdirectory("reference-equality-wide-").FullName,
            "Wide.dll");
        try
        {
            File.WriteAllBytes(path, BuildWideInterfaceImage(edgeCount: 5000));
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var type = TypeRef.Definition(
                TypeRefDecoder.CanonicalSelf(source.Reader),
                "Wide",
                "IWide");

            Assert.Equal(
                MetadataFactState.Unknown,
                source.HasOperatorInBindingHierarchy(type, "op_Equality"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void MalformedDynamicAttribute_RemainsUnknown()
    {
        string directory = Directory.CreateTempSubdirectory("malformed-dynamic-fact-").FullName;
        string path = Path.Combine(directory, "MalformedDynamic.dll");
        try
        {
            File.WriteAllBytes(path, BuildMalformedDynamicField());
            using var source = MetadataSource.OpenWithoutSymbols(path);
            var reader = source.Reader;
            var type = reader.TypeDefinitions
                .Select(reader.GetTypeDefinition)
                .Single(type => reader.GetString(type.Name) == "Carrier");
            var field = reader.GetFieldDefinition(Assert.Single(type.GetFields()));
            var objectType = TypeRef.CoreLib("System", "Object");

            Assert.Equal(
                MetadataFactState.Unknown,
                MethodDefinitionFacts.FieldDynamicFact(reader, field, objectType, objectType));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static void AssertOrder(
        string consumer,
        string v1,
        string v2,
        string first,
        string second)
    {
        var resolver = new VersionResolver(v1, v2);
        using var context = new MetadataContext(resolver);
        using var source = MetadataSource.OpenWithoutSymbols(consumer, resolver, context);

        var firstFunction = Import(source, first);
        var secondFunction = Import(source, second);
        var firstType = Assert.Single(firstFunction.Descendants.OfType<Comparison>()).Left.ResultType!;
        var secondType = Assert.Single(secondFunction.Descendants.OfType<Comparison>()).Left.ResultType!;

        Assert.Equal(
            first == "V1Identity" ? MetadataFactState.No : MetadataFactState.Yes,
            source.HasOperatorInBindingHierarchy(firstType, "op_Equality"));
        Assert.Equal(
            second == "V1Identity" ? MetadataFactState.No : MetadataFactState.Yes,
            source.HasOperatorInBindingHierarchy(secondType, "op_Equality"));
        var v1Type = first == "V1Identity" ? firstType : secondType;
        var v2Type = first == "V2Identity" ? firstType : secondType;
        var v1Identity = TypeDefinitionIdentity.Create(v1Type)!.Value;
        var v2Identity = TypeDefinitionIdentity.Create(v2Type)!.Value;
        Assert.NotEqual(v1Identity, v2Identity);
        AssertEquivalentAssemblyIdentityMatches(v1Type);
        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(v1Type, v2Type, allowCoreLibraryAliases: false));
        AssertCoreLibraryVersionAliasesMatch();
        AssertPlatformVersionAliasesMatch();

        var operatorFree = ImmutableHashSet.Create(v1Identity);
        Assert.Contains("return left == right;", PrintSynthetic(v1Type, operatorFree));
        Assert.Contains("return (object)left == (object)right;", PrintSynthetic(v2Type, operatorFree));
    }

    static void AssertEquivalentAssemblyIdentityMatches(TypeRef type)
    {
        var equivalent = TypeRef.DefinitionWithResolution(
            type.Assembly,
            type.Namespace,
            type.Name,
            type.ValueTypeHint,
            type.InlineArray,
            type.EnclosingType,
            type.DefinitionName!,
            type.ResolutionAssembly);

        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(type, equivalent, allowCoreLibraryAliases: false));
    }

    static void AssertCoreLibraryVersionAliasesMatch()
    {
        const string ns = "System.Collections.Generic";
        var definitionName = MetadataTypeDefinitionName.Create(ns, ["List`1"]) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("List metadata name is invalid"),
        };
        var runtime8 = new AssemblyReferenceIdentity(
            "System.Runtime",
            new Version(8, 0, 0, 0),
            null,
            "b03f5f7f11d50a3a");
        var runtime11 = runtime8 with { Version = new Version(11, 0, 0, 0) };
        var first = TypeRef.DefinitionWithResolution(
            TypeRef.CoreLibrary,
            ns,
            "List`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            runtime8);
        var second = TypeRef.DefinitionWithResolution(
            TypeRef.CoreLibrary,
            ns,
            "List`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            runtime11);

        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(first, second, allowCoreLibraryAliases: false));
    }

    static void AssertPlatformVersionAliasesMatch()
    {
        const string ns = "System.Linq";
        var definitionName = MetadataTypeDefinitionName.Create(ns, ["IOrderedEnumerable`1"]) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
            _ => throw new InvalidOperationException("IOrderedEnumerable metadata name is invalid"),
        };
        var systemLinq8 = new AssemblyReferenceIdentity(
            "System.Linq",
            new Version(8, 0, 0, 0),
            null,
            "b03f5f7f11d50a3a");
        var systemLinq11 = systemLinq8 with { Version = new Version(11, 0, 0, 0) };
        var first = TypeRef.DefinitionWithResolution(
            "System.Linq",
            ns,
            "IOrderedEnumerable`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            systemLinq8);
        var second = TypeRef.DefinitionWithResolution(
            "System.Linq",
            ns,
            "IOrderedEnumerable`1",
            ValueTypeHint.ReferenceType,
            MetadataFactState.Unknown,
            null,
            definitionName,
            systemLinq11);

        Assert.False(CrossAssemblyTypeResolver.SameSignatureType(first, second, allowCoreLibraryAliases: false));
        Assert.True(CrossAssemblyTypeResolver.SameSignatureType(first, second, allowCoreLibraryAliases: true));
    }

    static IrFunction Import(MetadataSource source, string methodName)
    {
        var function = IrImporter.Import(source, "IdentityFixture.Cases", methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static string PrintSynthetic(
        TypeRef type,
        IReadOnlySet<TypeDefinitionIdentity> equalityOperatorFreeTypes)
    {
        var comparison = new Comparison(
            ComparisonKind.Equal,
            isUnsigned: false,
            new LoadArgument(0, "left", type),
            new LoadArgument(1, "right", type));
        var block = new Block();
        block.Add(new Return(comparison));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.Definition("Synthetic", "Samples", "Owner"),
            new MethodSignature(
                TypeRef.CoreLib("System", "Boolean"),
                [
                    new ILInspector.Decompiler.Pipeline.Parameter("left", type),
                    new ILInspector.Decompiler.Pipeline.Parameter("right", type),
                ],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            body)
        {
            TypeShapes = new Dictionary<TypeRef, TypeShape> { [type] = TypeShape.Reference },
            EqualityOperatorFreeTypes = equalityOperatorFreeTypes,
        };
        return CSharpPrinter.Print(function).Output!;
    }

    sealed class VersionResolver(string v1, string v2) : IAssemblyReferenceResolver
    {
        readonly Dictionary<Version, string> _paths = new()
        {
            [new Version(1, 0, 0, 0)] = v1,
            [new Version(2, 0, 0, 0)] = v2,
        };

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
            => identity.Name == "Twin"
                && identity.Version is { } version
                && _paths.TryGetValue(version, out var path)
                    ? ResolvedAssemblyReference.CreateFromPath(
                        path,
                        AssemblyResolutionProvenance.Local("ReferenceEqualityMetadataFactsTests"))
                    : null;
    }

    static byte[] BuildTwin(Version version, bool hasEquality)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Twin.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Twin"),
            version,
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("TwinNs"),
            metadata.GetOrAddString("C"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        if (hasEquality)
        {
            var firstParameter = metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("left"),
                sequenceNumber: 1);
            metadata.AddParameter(
                ParameterAttributes.None,
                metadata.GetOrAddString("right"),
                sequenceNumber: 2);
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("op_Equality"),
                EqualitySignature(metadata, type),
                bodyOffset: 0,
                firstParameter);
        }

        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildMalformedDynamicField()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MalformedDynamic.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedDynamic"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var systemRuntime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var expressions = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Linq.Expressions"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            systemRuntime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var dynamicAttribute = metadata.AddTypeReference(
            expressions,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("DynamicAttribute"));
        var constructorSignature = new BlobBuilder();
        constructorSignature.WriteByte(0x20);
        constructorSignature.WriteCompressedInteger(0);
        constructorSignature.WriteByte(0x01);
        var constructor = metadata.AddMemberReference(
            dynamicAttribute,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(constructorSignature));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            default,
            metadata.GetOrAddString("Carrier"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06);
        fieldSignature.WriteByte(0x1c);
        var field = metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(fieldSignature));
        metadata.AddCustomAttribute(
            field,
            constructor,
            metadata.GetOrAddBlob(new byte[] { 0, 0, 0, 0 }));
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] BuildIdentityConsumer()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Consumer.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Consumer"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var twinV1 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var twinV2 = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Twin"),
            new Version(2, 0, 0, 0),
            default,
            default,
            default,
            default);
        var cV1 = metadata.AddTypeReference(
            twinV1,
            metadata.GetOrAddString("TwinNs"),
            metadata.GetOrAddString("C"));
        var cV2 = metadata.AddTypeReference(
            twinV2,
            metadata.GetOrAddString("TwinNs"),
            metadata.GetOrAddString("C"));
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("IdentityFixture"),
            metadata.GetOrAddString("Cases"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int v1Body = AddCeqBody(bodyEncoder);
        int v2Body = AddCeqBody(bodyEncoder);
        var v1Parameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        var v2Parameters = metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("left"),
            sequenceNumber: 1);
        metadata.AddParameter(
            ParameterAttributes.None,
            metadata.GetOrAddString("right"),
            sequenceNumber: 2);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("V1Identity"),
            EqualitySignature(metadata, cV1),
            v1Body,
            v1Parameters);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("V2Identity"),
            EqualitySignature(metadata, cV2),
            v2Body,
            v2Parameters);

        return Serialize(metadata, methodBodies);
    }

    static int AddCeqBody(MethodBodyStreamEncoder encoder)
    {
        var body = new BlobBuilder();
        var instructions = new InstructionEncoder(body, new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldarg_0);
        instructions.OpCode(ILOpCode.Ldarg_1);
        instructions.OpCode(ILOpCode.Ceq);
        instructions.OpCode(ILOpCode.Ret);
        return encoder.AddMethodBody(instructions, maxStack: 2);
    }

    static BlobHandle EqualitySignature(MetadataBuilder metadata, EntityHandle type)
    {
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature()
            .Parameters(
                2,
                returnType => returnType.Type().Boolean(),
                parameters =>
                {
                    parameters.AddParameter().Type().Type(type, isValueType: false);
                    parameters.AddParameter().Type().Type(type, isValueType: false);
                });
        return metadata.GetOrAddBlob(signature);
    }

    static byte[] BuildWideInterfaceImage(int edgeCount)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Wide.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Wide"),
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
        var baseInterface = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddString("IBase"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var wideInterface = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString("Wide"),
            metadata.GetOrAddString("IWide"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < edgeCount; i++)
            metadata.AddInterfaceImplementation(wideInterface, baseInterface);
        return Serialize(metadata, new BlobBuilder());
    }

    static byte[] Serialize(MetadataBuilder metadata, BlobBuilder methodBodies)
    {
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}
