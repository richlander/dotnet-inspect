using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.ILDiff.Tests;

public class IlAssemblyDiffMetadataGraphSafetyTests
{
    const string WorkerVariable = "DOTNET_INSPECT_INSTRUCTIONS_METADATA_GRAPH_WORKER";
    const int DeepGraphLength = 100_000;

    [Fact]
    public void CyclicAndDeepMetadataGraphs_ThroughPublicAssemblyDiffPaths_AreContainedInChildProcess()
        => RunWorker(nameof(MetadataGraphWorker));

    [Fact]
    public void MetadataGraphWorker()
    {
        if (!IsSelectedWorker(nameof(MetadataGraphWorker)))
            return;

        AssertRejected(
            BuildTypeDefinitionImage(depth: 1, cyclic: true),
            "Cycle");
        AssertRejected(
            BuildTypeDefinitionImage(DeepGraphLength, cyclic: false),
            "NodeBudget");
        AssertRejected(
            BuildTypeReferenceImage(depth: 1, cyclic: true),
            "Cycle");
        AssertRejected(
            BuildTypeReferenceImage(DeepGraphLength, cyclic: false),
            "NodeBudget");
        AssertOperandRejected(
            BuildTypeDefinitionOperandImage(depth: 1, cyclic: true),
            "Cycle");
        AssertOperandRejected(
            BuildTypeDefinitionOperandImage(DeepGraphLength, cyclic: false),
            "NodeBudget");
        AssertOperandRejected(
            BuildTypeReferenceOperandImage(depth: 1, cyclic: true),
            "Cycle");
        AssertOperandRejected(
            BuildTypeReferenceOperandImage(DeepGraphLength, cyclic: false),
            "NodeBudget");
        AssertOperandRejected(
            BuildTypeDefinitionSignatureOperandImage(),
            "Cycle");
        AssertOperandRejected(
            BuildTypeReferenceSignatureOperandImage(),
            "Cycle");
    }

    [Fact]
    public void ValidNestedTypeDefinitionIdentity_IsPreserved()
    {
        string identity = MemberIdentity(BuildTypeDefinitionImage(depth: 3, cyclic: false));

        Assert.Equal("N.T+T+T::M#static void([Synthetic]N.T+T+T)", identity);
    }

    [Fact]
    public void ValidCoreLibTypeReferenceIdentity_IsPreserved()
    {
        string identity = MemberIdentity(BuildTypeReferenceImage(depth: 1, cyclic: false));

        Assert.Equal("C::M#static void([System.Private.CoreLib]System.String)", identity);
    }

    [Fact]
    public void ValidNestedTypeDefinitionOperandIdentity_DetectsRootChange()
    {
        var result = CompareOperand(
            BuildTypeDefinitionOperandImage(depth: 3, cyclic: false, rootName: "OldRoot"),
            BuildTypeDefinitionOperandImage(depth: 3, cyclic: false, rootName: "NewRoot"));

        Assert.Equal(IlBodyDiffOutcome.OperandDiff, result.Diff.Outcome);
        Assert.True(result.Diff.FailureRows.IsDefaultOrEmpty);
    }

    [Fact]
    public void ValidNestedTypeReferenceOperandIdentity_DetectsRootChange()
    {
        var result = CompareOperand(
            BuildTypeReferenceOperandImage(depth: 3, cyclic: false, rootName: "OldRoot"),
            BuildTypeReferenceOperandImage(depth: 3, cyclic: false, rootName: "NewRoot"));

        Assert.Equal(IlBodyDiffOutcome.OperandDiff, result.Diff.Outcome);
        Assert.True(result.Diff.FailureRows.IsDefaultOrEmpty);
    }

    static bool IsSelectedWorker(string methodName)
        => Environment.GetEnvironmentVariable(WorkerVariable) == methodName;

    static void RunWorker(string workerMethod)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(IlAssemblyDiffMetadataGraphSafetyTests).Assembly.Location);
        startInfo.ArgumentList.Add("--filter-method");
        startInfo.ArgumentList.Add($"*{workerMethod}*");
        startInfo.Environment[WorkerVariable] = workerMethod;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string standardOutput = standardOutputTask.GetAwaiter().GetResult();
        string standardError = standardErrorTask.GetAwaiter().GetResult();

        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
    }

    static void AssertRejected(byte[] image, string expectedKind)
    {
        using (var oldStream = new MemoryStream(image))
        using (var newStream = new MemoryStream(image))
        {
            var assemblyResult = IlAssemblyDiff.CompareStreams(
                oldStream,
                "old.dll",
                newStream,
                "new.dll");

            Assert.Equal(0, assemblyResult.Diff.ComparedBodyCount);
            Assert.Equal(0, assemblyResult.Diff.PairExactCount);
            Assert.Equal(2, assemblyResult.Diff.FailureCount);
            Assert.Equal(2, assemblyResult.Diff.IdentityFailures.Length);
            Assert.All(
                assemblyResult.Diff.IdentityFailures,
                failure =>
                {
                    Assert.Equal(MetadataTypeNameFailureMechanism.Relationship, failure.Mechanism);
                    Assert.Equal(expectedKind, failure.Kind);
                    Assert.StartsWith("The Type", failure.Detail, StringComparison.Ordinal);
                });
        }

        using var pe = new PEReader(new MemoryStream(image));
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader);
        var memberResult = IlAssemblyDiff.CompareMembers(
            pe,
            reader,
            method,
            pe,
            reader,
            method);

        Assert.False(memberResult.Diff.IsExact);
        Assert.Equal("method identity resolution failed", memberResult.Diff.Failure);
        Assert.Equal(2, memberResult.IdentityFailures.Length);
        Assert.All(
            memberResult.IdentityFailures,
            failure => Assert.Equal(expectedKind, failure.Kind));
        Assert.Equal(memberResult.Old.Identity, memberResult.New.Identity);
        Assert.StartsWith("token 0x0600", memberResult.Old.Identity, StringComparison.Ordinal);
    }

    static void AssertOperandRejected(byte[] image, string expectedKind)
    {
        var result = CompareOperand(image, image);

        Assert.False(result.Diff.IsAvailable);
        Assert.Contains(expectedKind, result.Diff.Failure, StringComparison.Ordinal);
        var failure = Assert.Single(result.Diff.FailureRows);
        Assert.Equal(IlDiffFailureKind.TokenResolutionFailure, failure.Kind);
        Assert.Equal("old", failure.Side);
        Assert.Contains(expectedKind, failure.Message, StringComparison.Ordinal);
        Assert.Empty(result.IdentityFailures);
    }

    static IlMemberDiffResult CompareOperand(byte[] oldImage, byte[] newImage)
    {
        using var oldPe = new PEReader(new MemoryStream(oldImage));
        using var newPe = new PEReader(new MemoryStream(newImage));
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        return IlAssemblyDiff.CompareMembers(
            oldPe,
            oldReader,
            FindMethod(oldReader),
            newPe,
            newReader,
            FindMethod(newReader));
    }

    static string MemberIdentity(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image));
        var reader = pe.GetMetadataReader();
        var method = FindMethod(reader);
        return IlAssemblyDiff.CompareMembers(pe, reader, method, pe, reader, method).Old.Identity;
    }

    static byte[] BuildTypeDefinitionImage(int depth, bool cyclic)
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);

        TypeDefinitionHandle parent = default;
        for (int i = 0; i < depth; i++)
        {
            var current = metadata.AddTypeDefinition(
                i == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                i == 0 ? metadata.GetOrAddString("N") : default,
                metadata.GetOrAddString("T"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(1));
            if (!parent.IsNil)
                metadata.AddNestedType(current, parent);
            parent = current;
        }

        if (cyclic)
            metadata.AddNestedType(parent, parent);

        var methodBodies = AddMethod(metadata, MethodSignature(metadata, parent));
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildTypeReferenceImage(int depth, bool cyclic)
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        TypeReferenceHandle typeReference;
        if (cyclic)
        {
            typeReference = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop"));
        }
        else
        {
            var coreLib = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            typeReference = metadata.AddTypeReference(
                coreLib,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("String"));
            for (int i = 1; i < depth; i++)
            {
                typeReference = metadata.AddTypeReference(
                    typeReference,
                    default,
                    metadata.GetOrAddString("T"));
            }
        }

        var methodBodies = AddMethod(metadata, MethodSignature(metadata, typeReference));
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildTypeDefinitionOperandImage(
        int depth,
        bool cyclic,
        string rootName = "T")
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        TypeDefinitionHandle parent = default;
        for (int i = 0; i < depth; i++)
        {
            var current = metadata.AddTypeDefinition(
                i == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                i == 0 ? metadata.GetOrAddString("N") : default,
                metadata.GetOrAddString(i == 0 ? rootName : "T"),
                baseType: default,
                fieldList: MetadataTokens.FieldDefinitionHandle(1),
                methodList: MetadataTokens.MethodDefinitionHandle(2));
            if (!parent.IsNil)
                metadata.AddNestedType(current, parent);
            parent = current;
        }

        if (cyclic)
            metadata.AddNestedType(parent, parent);

        var methodBodies = AddMethod(
            metadata,
            MethodSignature(metadata, default),
            parent);
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildTypeReferenceOperandImage(
        int depth,
        bool cyclic,
        string rootName = "String")
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        TypeReferenceHandle typeReference;
        if (cyclic)
        {
            typeReference = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop"));
        }
        else
        {
            var coreLib = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Private.CoreLib"),
                new Version(11, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
            typeReference = metadata.AddTypeReference(
                coreLib,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString(rootName));
            for (int i = 1; i < depth; i++)
            {
                typeReference = metadata.AddTypeReference(
                    typeReference,
                    default,
                    metadata.GetOrAddString("T"));
            }
        }

        var methodBodies = AddMethod(
            metadata,
            MethodSignature(metadata, default),
            typeReference);
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildTypeDefinitionSignatureOperandImage()
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var cyclicType = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Loop"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(3));
        metadata.AddNestedType(cyclicType, cyclicType);

        var methodBodies = AddSignatureOperandMethods(
            metadata,
            MethodSignature(metadata, cyclicType));
        return Serialize(metadata, methodBodies);
    }

    static byte[] BuildTypeReferenceSignatureOperandImage()
    {
        var metadata = CreateMetadata();
        AddModuleType(metadata);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            default,
            metadata.GetOrAddString("C"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        var cyclicType = metadata.AddTypeReference(
            MetadataTokens.TypeReferenceHandle(1),
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("Loop"));

        var methodBodies = AddSignatureOperandMethods(
            metadata,
            MethodSignature(metadata, cyclicType));
        return Serialize(metadata, methodBodies);
    }

    static BlobBuilder AddSignatureOperandMethods(
        MetadataBuilder metadata,
        BlobHandle targetSignature)
    {
        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        var targetIl = new BlobBuilder();
        var targetInstructions = new InstructionEncoder(
            targetIl,
            new ControlFlowBuilder());
        targetInstructions.OpCode(ILOpCode.Ret);
        int targetBodyOffset = bodyEncoder.AddMethodBody(
            targetInstructions,
            maxStack: 0);
        var target = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Target"),
            targetSignature,
            targetBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var entryIl = new BlobBuilder();
        var entryInstructions = new InstructionEncoder(
            entryIl,
            new ControlFlowBuilder());
        entryInstructions.OpCode(ILOpCode.Ldtoken);
        entryInstructions.Token(target);
        entryInstructions.OpCode(ILOpCode.Pop);
        entryInstructions.OpCode(ILOpCode.Ret);
        int entryBodyOffset = bodyEncoder.AddMethodBody(
            entryInstructions,
            maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            MethodSignature(metadata, default),
            entryBodyOffset,
            MetadataTokens.ParameterHandle(1));
        return methodBodies;
    }

    static MetadataBuilder CreateMetadata()
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
        return metadata;
    }

    static void AddModuleType(MetadataBuilder metadata)
        => metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

    static BlobHandle MethodSignature(MetadataBuilder metadata, EntityHandle parameterType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default, static
        signature.WriteCompressedInteger(parameterType.IsNil ? 0 : 1);
        signature.WriteByte(0x01); // void
        if (!parameterType.IsNil)
        {
            signature.WriteByte(0x12); // class
            int tag = parameterType.Kind switch
            {
                HandleKind.TypeDefinition => 0,
                HandleKind.TypeReference => 1,
                _ => throw new ArgumentException($"Unsupported signature type {parameterType.Kind}.", nameof(parameterType)),
            };
            signature.WriteCompressedInteger((MetadataTokens.GetRowNumber(parameterType) << 2) | tag);
        }
        return metadata.GetOrAddBlob(signature);
    }

    static BlobBuilder AddMethod(
        MetadataBuilder metadata,
        BlobHandle signature,
        EntityHandle operand = default)
    {
        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(il, new ControlFlowBuilder());
        if (!operand.IsNil)
        {
            instructions.OpCode(ILOpCode.Ldtoken);
            instructions.Token(operand);
            instructions.OpCode(ILOpCode.Pop);
        }
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(
            instructions,
            maxStack: operand.IsNil ? 0 : 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            signature,
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        return methodBodies;
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

    static MethodDefinitionHandle FindMethod(MetadataReader reader)
    {
        foreach (var handle in reader.MethodDefinitions)
        {
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == "M")
                return handle;
        }

        throw new InvalidOperationException("Method M not found.");
    }

}
