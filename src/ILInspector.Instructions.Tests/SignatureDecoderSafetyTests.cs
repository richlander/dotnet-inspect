using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Instructions.Tests;

public class SignatureDecoderSafetyTests
{
    const string WorkerVariable = "DOTNET_INSPECT_INSTRUCTIONS_SIGNATURE_WORKER";

    [Fact]
    public void DeepAndCyclicSignatures_ThroughAssemblyDiff_AreContainedInChildProcess()
        => RunWorker(nameof(AssemblyDiffWorker));

    [Fact]
    public void DeepAndCyclicSignatures_ThroughStackResolver_AreContainedInChildProcess()
        => RunWorker(nameof(StackResolverWorker));

    [Fact]
    public void AssemblyDiffWorker()
    {
        if (!IsSelectedWorker(nameof(AssemblyDiffWorker)))
            return;

        var image = BuildAdversarialImage();
        using var oldStream = new MemoryStream(image);
        using var newStream = new MemoryStream(image);

        var result = IlAssemblyDiff.CompareStreams(
            oldStream,
            "old.dll",
            newStream,
            "new.dll");

        Assert.True(result.Diff.ComparedBodyCount > 0);
        Assert.Equal(2, result.Diff.FailureCount);
        Assert.Equal(2, result.Diff.IdentityFailures.Length);

        using var oneOverload = new MemoryStream(BuildRejectedOverloadImage(methodCount: 1));
        using var twoOverloads = new MemoryStream(BuildRejectedOverloadImage(methodCount: 2));
        var overloadResult = IlAssemblyDiff.CompareStreams(
            oneOverload,
            "one.dll",
            twoOverloads,
            "two.dll");

        Assert.Equal(3, overloadResult.Diff.FailureCount);
        Assert.Equal(3, overloadResult.Diff.IdentityFailures.Length);

        var unshifted = BuildShiftedGuardedMethodImage(addPrefixMethod: false);
        var shifted = BuildShiftedGuardedMethodImage(addPrefixMethod: true);
        using var unshiftedStream = new MemoryStream(unshifted);
        using var shiftedStream = new MemoryStream(shifted);
        var shiftedResult = IlAssemblyDiff.CompareStreams(
            unshiftedStream,
            "unshifted.dll",
            shiftedStream,
            "shifted.dll");

        Assert.Equal(3, shiftedResult.Diff.FailureCount);
        Assert.Equal(2, shiftedResult.Diff.IdentityFailures.Length);
        Assert.Equal(1, shiftedResult.Diff.PairExactCount);

        using var oldPe = new PEReader(new MemoryStream(unshifted));
        using var newPe = new PEReader(new MemoryStream(shifted));
        var oldReader = oldPe.GetMetadataReader();
        var newReader = newPe.GetMetadataReader();
        var memberResult = IlAssemblyDiff.CompareMembers(
            oldPe,
            oldReader,
            FindMethod(oldReader, "Caller"),
            newPe,
            newReader,
            FindMethod(newReader, "Caller"));

        Assert.True(memberResult.Diff.IsExact);
    }

    [Fact]
    public void StackResolverWorker()
    {
        if (!IsSelectedWorker(nameof(StackResolverWorker)))
            return;

        var image = BuildAdversarialImage();
        using var pe = new PEReader(new MemoryStream(image));
        var reader = pe.GetMetadataReader();
        var entryHandle = FindMethod(reader, "Entry");
        var entry = reader.GetMethodDefinition(entryHandle);
        var body = pe.GetMethodBody(entry.RelativeVirtualAddress);
        var resolver = MetadataStackTypeResolver.Create(reader, entryHandle, body);

        Assert.Equal(StackType.Unknown, resolver.Field(MetadataTokens.GetToken(MetadataTokens.FieldDefinitionHandle(1))));
        Assert.Equal(StackType.Unknown, resolver.Field(MetadataTokens.GetToken(MetadataTokens.MemberReferenceHandle(2))));
        _ = resolver.TypeOfToken(MetadataTokens.GetToken(MetadataTokens.TypeSpecificationHandle(1)));
        Assert.False(resolver.TryResolveCall(
            MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(1)),
            isNewObj: false,
            out _,
            out _,
            out _));
        Assert.False(resolver.TryResolveCall(
            MetadataTokens.GetToken(MetadataTokens.MemberReferenceHandle(1)),
            isNewObj: false,
            out _,
            out _,
            out _));
        Assert.False(resolver.TryResolveCalli(
            MetadataTokens.GetToken(MetadataTokens.StandaloneSignatureHandle(2)),
            out _,
            out _,
            out _));

        var stack = MethodInstructions.Decode(body).InterpretStack(reader, entryHandle, body);
        Assert.False(stack.IsComplete);
        Assert.Contains("Unresolved Call signature", stack.IncompleteReason, StringComparison.Ordinal);
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
        startInfo.ArgumentList.Add(typeof(SignatureDecoderSafetyTests).Assembly.Location);
        startInfo.ArgumentList.Add("-method");
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

    static byte[] BuildAdversarialImage()
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
        var type = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var cyclicTypeSpec = new BlobBuilder();
        cyclicTypeSpec.WriteByte(0x1f); // CMOD_REQD
        cyclicTypeSpec.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
        cyclicTypeSpec.WriteByte(0x08); // I4
        var cyclicTypeSpecHandle = metadata.AddTypeSpecification(metadata.GetOrAddBlob(cyclicTypeSpec));

        var deepFieldSignature = DeepSignature(header: 0x06, count: null);
        var field = metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("DeepField"),
            metadata.GetOrAddBlob(deepFieldSignature));

        var deepMethodSignature = DeepSignature(header: 0x00, count: 0);
        var deepMethod = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("DeepMethod"),
            metadata.GetOrAddBlob(deepMethodSignature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var cyclicMethodSignature = new BlobBuilder();
        cyclicMethodSignature.WriteByte(0x00); // default method signature
        cyclicMethodSignature.WriteByte(0x00); // zero parameters
        cyclicMethodSignature.WriteByte(0x1f); // CMOD_REQD
        cyclicMethodSignature.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
        cyclicMethodSignature.WriteByte(0x08); // I4
        var cyclicMethod = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("CyclicMethod"),
            metadata.GetOrAddBlob(cyclicMethodSignature),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var deepMember = metadata.AddMemberReference(
            type,
            metadata.GetOrAddString("DeepMember"),
            metadata.GetOrAddBlob(DeepSignature(header: 0x00, count: 0)));
        var deepFieldMember = metadata.AddMemberReference(
            type,
            metadata.GetOrAddString("DeepFieldMember"),
            metadata.GetOrAddBlob(DeepSignature(header: 0x06, count: null)));

        var deepMethodSpecSignature = DeepSignature(header: 0x0a, count: 1);
        var methodSpec = metadata.AddMethodSpecification(
            deepMethod,
            metadata.GetOrAddBlob(deepMethodSpecSignature));

        var deepLocalSignature = DeepSignature(header: 0x07, count: 1);
        var localSignature = metadata.AddStandaloneSignature(metadata.GetOrAddBlob(deepLocalSignature));
        var calliSignature = metadata.AddStandaloneSignature(metadata.GetOrAddBlob(DeepSignature(header: 0x00, count: 0)));

        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(il, new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ldsfld);
        instructions.Token(field);
        instructions.OpCode(ILOpCode.Pop);
        instructions.Call(deepMethod);
        instructions.OpCode(ILOpCode.Pop);
        instructions.Call(cyclicMethod);
        instructions.OpCode(ILOpCode.Pop);
        instructions.Call(deepMember);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ldsfld);
        instructions.Token(deepFieldMember);
        instructions.OpCode(ILOpCode.Pop);
        instructions.Call(methodSpec);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ldtoken);
        instructions.Token(cyclicTypeSpecHandle);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ldc_i4_0);
        instructions.OpCode(ILOpCode.Conv_i);
        instructions.OpCode(ILOpCode.Calli);
        instructions.Token(calliSignature);
        instructions.OpCode(ILOpCode.Ret);

        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int entryBodyOffset = bodyEncoder.AddMethodBody(
            instructions,
            maxStack: 8,
            localSignature,
            MethodBodyAttributes.InitLocals);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Entry"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            entryBodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobBuilder DeepSignature(byte header, int? count)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(header);
        if (count is int value)
            signature.WriteCompressedInteger(value);
        for (int i = 0; i < 100_000; i++)
            signature.WriteByte(0x1d); // SZARRAY
        signature.WriteByte(0x08); // I4
        return signature;
    }

    static byte[] BuildRejectedOverloadImage(int methodCount)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Overloads.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Overloads"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(DeepSignature(header: 0x00, count: 0)),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildShiftedGuardedMethodImage(bool addPrefixMethod)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Shifted.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Shifted"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            default,
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        if (addPrefixMethod)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Dummy"),
                metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
        }

        var guarded = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Guarded"),
            metadata.GetOrAddBlob(DeepSignature(header: 0x00, count: 0)),
            bodyOffset: -1,
            parameterList: MetadataTokens.ParameterHandle(1));

        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(il, new ControlFlowBuilder());
        instructions.Call(guarded);
        instructions.OpCode(ILOpCode.Pop);
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(instructions, maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Caller"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x01 }),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string name)
    {
        foreach (var handle in reader.MethodDefinitions)
        {
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == name)
                return handle;
        }

        throw new InvalidOperationException($"Method {name} not found.");
    }
}
