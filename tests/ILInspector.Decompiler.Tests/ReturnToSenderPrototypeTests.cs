using ILInspector.DecompilerHarness;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.Instructions;
using DotnetInspector.RoundTripCompilation;
using DotnetInspector.Services;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Collection(ConsoleMutatorCollection.Name)]
[Trait("Area", "RoundTrip")]
public partial class ReturnToSenderPrototypeTests
{

    static string WriteTempSource(string fileName, string source, out string directory)
    {
        directory = Path.Combine(Path.GetTempPath(), $"rts-signature-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, source);
        return path;
    }

    static string CanonicalMethodSignature(string declaration)
    {
        MemberSignatureShapeResult result = SourceMemberSignatureShape.Create(
            declaration,
            SourceMemberSignatureKind.Method);
        return result.Shape is { } shape
            ? MemberSignatureShapeCodec.Encode(shape)
            : throw new InvalidOperationException(result.UnavailableReason);
    }

    static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    static ReturnToSender.FaultIsolationResult? TryIsolateRecompileFailureForMethod(
        string assemblyPath,
        string sourcePath,
        string rejectedTargetBody,
        string? authoredBody,
        bool useRawSourceIndex = false,
        string methodName = "M",
        int overload = 0,
        Guid? correlatedModuleVersionIdOverride = null,
        bool addDuplicateCorrelatedToken = false,
        int? correlatedMetadataTokenOverride = null)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);

        var (typeHandle, methodHandle) = FindMethod(reader, "Class1", methodName, overload);
        var moduleVersionId = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        var typeDefinition = reader.GetTypeDefinition(typeHandle);
        string? signature = MetadataMemberSignatureShape.Create(reader, methodHandle).Shape is { } shape
            ? MemberSignatureShapeCodec.Encode(shape)
            : null;
        if (signature is not null
            && !ReturnToSender.ResolvesUniquelyBySignature(
                reader,
                typeDefinition,
                methodName,
                signature,
                methodHandle))
        {
            signature = null;
        }
        var function = IrImporter.Import(source, "Class1", methodName, overload)
            ?? throw new InvalidOperationException($"Could not import Class1::{methodName}#{overload}.");
        var request = new MethodArtifactRequest(
            AssemblyPath: assemblyPath,
            Reader: reader,
            Function: function,
            TargetType: typeHandle,
            TargetMethod: methodHandle,
            TargetBody: new ProductTargetBody(rejectedTargetBody, []),
            FullType: "Class1",
            MethodName: methodName,
            Overload: overload,
            SignatureText: "",
            ClosureRoots: new HashSet<TypeDefinitionHandle> { typeHandle },
            ClosureFacts: new Dictionary<TypeDefinitionHandle, List<CompileBackFact>>());
        ReturnToSenderSourceIndex? sourceIndex;
        if (useRawSourceIndex)
        {
            sourceIndex = ReturnToSenderSourceIndex.TryCreate([sourcePath]);
        }
        else
        {
            var correlatedMembers = new List<ReturnToSenderSourceMember>
            {
                new(
                    "Class1",
                    methodName,
                    overload,
                    signature ?? "",
                    sourcePath,
                    authoredBody,
                    correlatedMetadataTokenOverride ?? MetadataTokens.GetToken(methodHandle),
                    correlatedModuleVersionIdOverride ?? moduleVersionId),
            };
            if (addDuplicateCorrelatedToken)
            {
                correlatedMembers.Add(new(
                    "Other",
                    methodName,
                    overload,
                    signature ?? "",
                    sourcePath,
                    authoredBody,
                    MetadataTokens.GetToken(methodHandle),
                    moduleVersionId));
            }

            sourceIndex = ReturnToSenderSourceIndex.FromCorrelatedMembers(
                correlatedMembers,
                reader);
        }
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            allowUnsafe: true);
        var references = RoslynTestReferences.TrustedPlatform.ToArray();

        var decompiledArtifact = CompileBackSourceComposer.Compose(request);
        Assert.NotNull(decompiledArtifact.SourceArtifact.ReplaceableBodyRange);
        var decompiledTree = CSharpSyntaxTree.ParseText(decompiledArtifact.Source, parseOptions);
        var decompiledDiagnostics = CSharpCompilation
            .Create("return-to-sender-decompiled", [decompiledTree], references, compileOptions)
            .GetDiagnostics();

        return ReturnToSender.TryIsolateRecompileFailure(
            decompiledArtifact,
            decompiledDiagnostics,
            sourceIndex,
            parseOptions,
            compileOptions,
            references);
    }

    static (TypeDefinitionHandle Type, MethodDefinitionHandle Method) FindMethod(
        MetadataReader reader,
        string typeName,
        string methodName,
        int overload = 0)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (!string.Equals(reader.GetFullTypeName(type), typeName, StringComparison.Ordinal))
                continue;

            int seen = 0;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (string.Equals(reader.GetString(method.Name), methodName, StringComparison.Ordinal)
                    && seen++ == overload)
                {
                    return (typeHandle, methodHandle);
                }
            }
        }

        throw new InvalidOperationException($"Could not find {typeName}::{methodName}#{overload}.");
    }

    static bool ContainsType(
        MetadataReader reader,
        string typeName) =>
        reader.TypeDefinitions.Any(
            handle =>
                string.Equals(
                    reader.GetFullTypeName(
                        reader.GetTypeDefinition(handle)),
                    typeName,
                    StringComparison.Ordinal));

    static AssemblyReferenceIdentity Identity(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(pe.GetMetadataReader());
    }

    static void RewriteAssemblyReferenceVersion(
        string path,
        string assemblyName,
        Version version)
    {
        byte[] image = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        MetadataReader reader = pe.GetMetadataReader();
        AssemblyReferenceHandle handle = Assert.Single(
            reader.AssemblyReferences,
            handle => reader.GetString(reader.GetAssemblyReference(handle).Name) == assemblyName);
        int rowOffset =
            pe.PEHeaders.MetadataStartOffset
            + reader.GetTableMetadataOffset(TableIndex.AssemblyRef)
            + ((MetadataTokens.GetRowNumber(handle) - 1)
                * reader.GetTableRowSize(TableIndex.AssemblyRef));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(rowOffset),
            checked((ushort)version.Major));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(rowOffset + 2),
            checked((ushort)version.Minor));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(rowOffset + 4),
            checked((ushort)version.Build));
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(rowOffset + 6),
            checked((ushort)version.Revision));
        File.WriteAllBytes(path, image);
    }

    // Emits a loadable IL-only assembly containing a single-member public interface whose type
    // definition carries [CompilerFeatureRequired("<unknown feature>")]. The attribute is not
    // authorable from C# source (CS8335 even when self-defined), so it is written directly as
    // metadata: a TypeReference to the real corelib CompilerFeatureRequiredAttribute, a
    // MemberReference to its (string) constructor, and a CustomAttribute naming an unknown feature
    // with IsOptional defaulting to false — the shape a downlevel/hand-authored producer emits and
    // that raises CS9041 when a consumer binds to the interface.
    static byte[] BuildCompilerFeatureRequiredInterfaceImage(
        string assemblyName,
        string namespaceName,
        string typeName,
        string methodName)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString($"{assemblyName}.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(assemblyName),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: AssemblyHashAlgorithm.None);

        var coreLib = typeof(object).Assembly.GetName();
        var coreLibRef = metadata.AddAssemblyReference(
            metadata.GetOrAddString(coreLib.Name!),
            coreLib.Version!,
            culture: default,
            publicKeyOrToken: metadata.GetOrAddBlob(coreLib.GetPublicKeyToken()!),
            flags: default,
            hashValue: default);
        var cfrTypeRef = metadata.AddTypeReference(
            coreLibRef,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("CompilerFeatureRequiredAttribute"));
        var cfrCtorSig = new BlobBuilder();
        cfrCtorSig.WriteByte(0x20); // HASTHIS, default calling convention
        cfrCtorSig.WriteCompressedInteger(1); // one parameter
        cfrCtorSig.WriteByte(0x01); // return type: void
        cfrCtorSig.WriteByte(0x0e); // parameter type: string
        var cfrCtorRef = metadata.AddMemberReference(
            cfrTypeRef,
            metadata.GetOrAddString(".ctor"),
            metadata.GetOrAddBlob(cfrCtorSig));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var interfaceHandle = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString(namespaceName),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodSig = new BlobBuilder();
        methodSig.WriteByte(0x20); // HASTHIS, default calling convention
        methodSig.WriteCompressedInteger(0); // no parameters
        methodSig.WriteByte(0x01); // return type: void
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(methodName),
            metadata.GetOrAddBlob(methodSig),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var attributeValue = new BlobBuilder();
        attributeValue.WriteUInt16(0x0001); // custom attribute prolog
        attributeValue.WriteSerializedString("TotallyUnknownFeature");
        attributeValue.WriteUInt16(0); // zero named arguments
        metadata.AddCustomAttribute(
            interfaceHandle,
            cfrCtorRef,
            metadata.GetOrAddBlob(attributeValue));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildConfusableAssemblyImage(
        string platformPath,
        Version? version = null)
    {
        using var stream = File.OpenRead(platformPath);
        using var reader = new PEReader(stream);
        var platformMetadata = reader.GetMetadataReader();
        var platformAssembly = platformMetadata.GetAssemblyDefinition();

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                platformMetadata.GetString(platformAssembly.Name) + ".dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                platformMetadata.GetString(platformAssembly.Name)),
            version ?? platformAssembly.Version,
            platformAssembly.Culture.IsNil
                ? default
                : metadata.GetOrAddString(
                    platformMetadata.GetString(platformAssembly.Culture)),
            platformAssembly.PublicKey.IsNil
                ? default
                : metadata.GetOrAddBlob(
                    platformMetadata.GetBlobBytes(platformAssembly.PublicKey)),
            platformAssembly.Flags,
            platformAssembly.HashAlgorithm);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildConfusableInterfaceAssemblyImage(
        string platformPath,
        string namespaceName,
        string typeName,
        string methodName)
    {
        using var stream = File.OpenRead(platformPath);
        using var reader = new PEReader(stream);
        var platformMetadata = reader.GetMetadataReader();
        var platformAssembly = platformMetadata.GetAssemblyDefinition();

        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                platformMetadata.GetString(platformAssembly.Name) + ".dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                platformMetadata.GetString(platformAssembly.Name)),
            platformAssembly.Version,
            platformAssembly.Culture.IsNil
                ? default
                : metadata.GetOrAddString(
                    platformMetadata.GetString(platformAssembly.Culture)),
            platformAssembly.PublicKey.IsNil
                ? default
                : metadata.GetOrAddBlob(
                    platformMetadata.GetBlobBytes(platformAssembly.PublicKey)),
            platformAssembly.Flags,
            platformAssembly.HashAlgorithm);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract,
            metadata.GetOrAddString(namespaceName),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x20); // HASTHIS, default calling convention
        methodSignature.WriteCompressedInteger(0);
        methodSignature.WriteByte(0x01); // void
        metadata.AddMethodDefinition(
            MethodAttributes.Public
                | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(methodName),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildSpoofedDefinitionAddressAssemblyImage(
        string platformPath,
        string namespaceName,
        string typeName)
    {
        using var stream = File.OpenRead(platformPath);
        using var reader = new PEReader(stream);
        var platformMetadata = reader.GetMetadataReader();
        var platformAssembly = platformMetadata.GetAssemblyDefinition();
        TypeDefinitionHandle target = platformMetadata.TypeDefinitions.Single(
            handle =>
            {
                var definition = platformMetadata.GetTypeDefinition(handle);
                return platformMetadata.GetString(definition.Namespace) == namespaceName
                    && platformMetadata.GetString(definition.Name) == typeName;
            });

        var metadata = new MetadataBuilder();
        var platformModule = platformMetadata.GetModuleDefinition();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString(
                platformMetadata.GetString(platformModule.Name)),
            mvid: metadata.GetOrAddGuid(
                platformMetadata.GetGuid(platformModule.Mvid)),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString(
                platformMetadata.GetString(platformAssembly.Name)),
            platformAssembly.Version,
            platformAssembly.Culture.IsNil
                ? default
                : metadata.GetOrAddString(
                    platformMetadata.GetString(platformAssembly.Culture)),
            platformAssembly.PublicKey.IsNil
                ? default
                : metadata.GetOrAddBlob(
                    platformMetadata.GetBlobBytes(platformAssembly.PublicKey)),
            platformAssembly.Flags,
            platformAssembly.HashAlgorithm);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        int targetRow = MetadataTokens.GetRowNumber(target);
        for (int row = 2; row < targetRow; row++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.NotPublic,
                metadata.GetOrAddString("Spoof"),
                metadata.GetOrAddString($"Padding{row}"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
        }

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Sealed,
            metadata.GetOrAddString(namespaceName),
            metadata.GetOrAddString(typeName),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    // Hand-authored IL exercising the shadowing-sibling regression: a class with a clean
    // explicit-interface metadata name for System.Collections.IEnumerable.GetEnumerator plus
    // a sibling type `N.System` in the same namespace. Assembled with ilasm because no C#
    // compiler can produce a clean explicit-override name alongside an in-scope shadow.
    const string ShadowingSiblingIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly shadowrepro { }
        .module shadowrepro.dll

        .namespace N
        {
          .class public auto ansi sealed beforefieldinit Seq
              extends [System.Runtime]System.Object
              implements [System.Runtime]System.Collections.IEnumerable
          {
            .method private hidebysig newslot virtual final
                instance class [System.Runtime]System.Collections.IEnumerator
                'System.Collections.IEnumerable.GetEnumerator'() cil managed
            {
              .override [System.Runtime]System.Collections.IEnumerable::GetEnumerator
              ldnull
              throw
            }
            .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
            {
              ldarg.0
              call instance void [System.Runtime]System.Object::.ctor()
              ret
            }
          }

          .class public auto ansi sealed beforefieldinit System
              extends [System.Runtime]System.Object
          {
            .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
            {
              ldarg.0
              call instance void [System.Runtime]System.Object::.ctor()
              ret
            }
          }
        }
        """;

    // External contract for the keyword-namespace shadow regression: an interface in a
    // namespace whose segment is the C# keyword `class` (raw metadata `class.IProbe`).
    const string KeywordContractsIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly KeywordContracts { }
        .module KeywordContracts.dll

        .class interface public abstract auto ansi 'class'.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void M() cil managed {}
        }
        """;

    // Target for the keyword-namespace shadow regression: N.Seq explicitly implements the
    // external `class.IProbe` with a clean metadata override name (`class.IProbe.M`), and a
    // sibling type N.'class' shadows the `class` root of the spelling once reconstructed.
    const string KeywordShadowFixtureIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly extern KeywordContracts { }
        .assembly keywordfixture { }
        .module keywordfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [KeywordContracts]'class'.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'class.IProbe.M'() cil managed
          {
            .override [KeywordContracts]'class'.IProbe::M
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }

        .class public auto ansi sealed beforefieldinit N.'class'
            extends [System.Runtime]System.Object
        {
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the unrepresentable-name regression: an interface whose namespace
    // segment is a compiler-unspeakable name (`<Bad>`) — legal in metadata, not a legal C#
    // identifier — so Clean() sanitizes it lossily to a different name (`__Bad_`).
    const string UnrepresentableContractsIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly GeneratedContracts { }
        .module GeneratedContracts.dll

        .class interface public abstract auto ansi '<Bad>'.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void M() cil managed {}
        }
        """;

    // Target for the unrepresentable-name regression: N.Seq explicitly implements the external
    // `<Bad>.IProbe`. The reconstruction would emit the sanitized `__Bad_.IProbe`, which names
    // no real type (CS0246) — the gate must decline to the sanitized ContextFail floor instead.
    const string UnrepresentableFixtureIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly extern GeneratedContracts { }
        .assembly badfixture { }
        .module badfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [GeneratedContracts]'<Bad>'.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void '<Bad>.IProbe.M'() cil managed
          {
            .override [GeneratedContracts]'<Bad>'.IProbe::M
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the unspeakable-member regression: an interface with a legal name
    // (`Good.IProbe`) but a method whose metadata name is compiler-unspeakable (`<Bad>`).
    const string UnspeakableMemberContractsIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly BadMethodContracts { }
        .module BadMethodContracts.dll

        .class interface public abstract auto ansi Good.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void '<Bad>'() cil managed {}
        }
        """;

    // Target for the unspeakable-member regression: N.Seq explicitly implements the external
    // `Good.IProbe.<Bad>`. The reconstruction would emit `Good.IProbe.__Bad_()`, which binds
    // to no interface member (CS0539) — the gate must decline to the sanitized ContextFail
    // floor instead.
    const string UnspeakableMemberFixtureIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly extern BadMethodContracts { }
        .assembly badmethodfixture { }
        .module badmethodfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [BadMethodContracts]Good.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'Good.IProbe.<Bad>'() cil managed
          {
            .override [BadMethodContracts]Good.IProbe::'<Bad>'
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the format-character regressions: the `%ZWNJ%` placeholder is
    // replaced with U+200C (a Unicode format character) at test time. Roslyn strips format
    // characters when binding identifiers, so a member name `M\u200C` binds as `M` — a name
    // that is identifier-like yet does not round-trip. Interface variant: namespace `G\u200Cood`.
    const string CfMemberContractsIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly CfContracts { }
        .module CfContracts.dll

        .class interface public abstract auto ansi Good.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void 'M%ZWNJ%'() cil managed {}
        }
        """;

    const string CfMemberFixtureIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly extern CfContracts { }
        .assembly cffixture { }
        .module cffixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [CfContracts]Good.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'Good.IProbe.M%ZWNJ%'() cil managed
          {
            .override [CfContracts]Good.IProbe::'M%ZWNJ%'
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    const string CfNamespaceContractsIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly CfNsContracts { }
        .module CfNsContracts.dll

        .class interface public abstract auto ansi 'G%ZWNJ%ood'.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void M() cil managed {}
        }
        """;

    const string CfNamespaceFixtureIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly extern CfNsContracts { }
        .assembly cfnsfixture { }
        .module cfnsfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [CfNsContracts]'G%ZWNJ%ood'.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'G%ZWNJ%ood.IProbe.M'() cil managed
          {
            .override [CfNsContracts]'G%ZWNJ%ood'.IProbe::M
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // External contract for the decomposed-identifier (non-NFC) regression: the `%COMB%`
    // placeholder is replaced with U+0301 (combining acute accent) at test time, so the member
    // name is `e` + U+0301 — identifier-like, format-character-free, and NOT in NFC. Roslyn binds
    // it verbatim (no normalization), so it round-trips Exact and must NOT be declined.
    const string NfcMemberContractsIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly NfcContracts { }
        .module NfcContracts.dll

        .class interface public abstract auto ansi Good.IProbe
        {
          .method public hidebysig newslot abstract virtual instance void 'e%COMB%'() cil managed {}
        }
        """;

    const string NfcMemberFixtureIl = """
        .assembly extern System.Runtime {
          .publickeytoken = (B0 3F 5F 7F 11 D5 0A 3A)
          .ver 0:0:0:0
        }
        .assembly extern NfcContracts { }
        .assembly nfcfixture { }
        .module nfcfixture.dll

        .class public auto ansi sealed beforefieldinit N.Seq
            extends [System.Runtime]System.Object
            implements [NfcContracts]Good.IProbe
        {
          .method private final hidebysig newslot virtual
              instance void 'Good.IProbe.e%COMB%'() cil managed
          {
            .override [NfcContracts]Good.IProbe::'e%COMB%'
            ret
          }
          .method public hidebysig specialname rtspecialname instance void .ctor() cil managed
          {
            ldarg.0
            call instance void [System.Runtime]System.Object::.ctor()
            ret
          }
        }
        """;

    // Locates a usable ilasm: an ILASM_PATH override, then PATH, then the restored
    // runtime.<rid>.microsoft.netcore.ilasm NuGet package cache. Returns null when none is
    // available so the caller can skip.
    static string? TryLocateIlasm()
    {
        string exe = OperatingSystem.IsWindows() ? "ilasm.exe" : "ilasm";

        var overridePath = Environment.GetEnvironmentVariable("ILASM_PATH");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            return overridePath;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0)
                continue;
            var candidate = Path.Combine(dir, exe);
            if (File.Exists(candidate))
                return candidate;
        }

        var nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        if (Directory.Exists(nuget))
        {
            foreach (var pkg in Directory.EnumerateDirectories(nuget)
                .Where(d => Path.GetFileName(d).Contains("microsoft.netcore.ilasm", StringComparison.OrdinalIgnoreCase)))
            {
                var hit = Directory.EnumerateFiles(pkg, exe, SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null)
                    return hit;
            }
        }

        return null;
    }

    static string AssembleIlFixture(string ilasm, string il, string directory, string assemblyName)
    {
        Directory.CreateDirectory(directory);
        var ilPath = Path.Combine(directory, assemblyName + ".il");
        var dllPath = Path.Combine(directory, assemblyName + ".dll");
        File.WriteAllText(ilPath, il);

        var psi = new System.Diagnostics.ProcessStartInfo(ilasm)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = directory,
        };
        psi.ArgumentList.Add(ilPath);
        psi.ArgumentList.Add("-dll");
        psi.ArgumentList.Add("-output=" + dllPath);

        using var process = System.Diagnostics.Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            File.Exists(dllPath),
            $"ilasm did not produce an assembly:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        return dllPath;
    }

    static string CompileFixture(
        string source,
        string? directory = null,
        string assemblyName = "fixture",
        IReadOnlyList<MetadataReference>? additionalReferences = null,
        bool allowUnsafe = false)
    {
        directory ??= Path.Combine(Path.GetTempPath(), $"return-to-sender-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{assemblyName}.dll");
        var references = RoslynTestReferences.TrustedPlatform
            .Concat(additionalReferences ?? []);
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview))],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                allowUnsafe: allowUnsafe));

        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    static void DeleteFixture(string assemblyPath)
    {
        var directory = Path.GetDirectoryName(assemblyPath);
        File.Delete(assemblyPath);
        if (directory is not null && Path.GetFileName(directory).StartsWith("return-to-sender-", StringComparison.Ordinal))
            Directory.Delete(directory, recursive: true);
    }
}
