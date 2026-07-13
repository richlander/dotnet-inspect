using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.Instructions.Tests;

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

        AssertContained(BuildTypeDefinitionImage(depth: 1, cyclic: true));
        AssertContained(BuildTypeDefinitionImage(DeepGraphLength, cyclic: false));
        AssertContained(BuildTypeReferenceImage(depth: 1, cyclic: true));
        AssertContained(BuildTypeReferenceImage(DeepGraphLength, cyclic: false));
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
    public void MetadataGraphEdgeCensus_MatchesBoundedImplementations()
    {
        string sourceDirectory = Path.Combine(FindRepositoryRoot(), "src", "ILInspector.Instructions");
        var actual = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Select(path =>
            {
                var root = CSharpSyntaxTree.ParseText(
                    File.ReadAllText(path),
                    path: path,
                    cancellationToken: TestContext.Current.CancellationToken)
                    .GetRoot(TestContext.Current.CancellationToken);
                return new GraphEdgeCount(
                    Path.GetRelativePath(sourceDirectory, path),
                    root.DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Count(invocation =>
                            invocation.Expression is MemberAccessExpressionSyntax member
                            && member.Name.Identifier.ValueText == "GetDeclaringType"),
                    root.DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Count(member => member.Name.Identifier.ValueText == "ResolutionScope"));
            })
            .Where(count => count.DeclaringTypeEdges > 0 || count.TypeReferenceEdges > 0)
            .OrderBy(count => count.File, StringComparer.Ordinal)
            .ToArray();

        GraphEdgeCount[] expected =
        [
            new("BoundedMetadataName.cs", DeclaringTypeEdges: 2, TypeReferenceEdges: 2),
            new("IlAssemblyDiff.cs", DeclaringTypeEdges: 1, TypeReferenceEdges: 0),
            new("IlBodyDiff.cs", DeclaringTypeEdges: 5, TypeReferenceEdges: 8),
            new("MetadataStackTypeResolver.cs", DeclaringTypeEdges: 2, TypeReferenceEdges: 0),
        ];
        Assert.Equal(expected, actual);
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

    static void AssertContained(byte[] image)
    {
        using (var oldStream = new MemoryStream(image))
        using (var newStream = new MemoryStream(image))
        {
            var assemblyResult = IlAssemblyDiff.CompareStreams(
                oldStream,
                "old.dll",
                newStream,
                "new.dll");

            Assert.Equal(1, assemblyResult.Diff.ComparedBodyCount);
            Assert.Equal(1, assemblyResult.Diff.PairExactCount);
            Assert.Equal(0, assemblyResult.Diff.FailureCount);
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

        Assert.True(memberResult.Diff.IsExact);
        Assert.Equal(memberResult.Old.Identity, memberResult.New.Identity);
        Assert.InRange(memberResult.Old.Identity.Length, 1, 4096);
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

    static BlobBuilder AddMethod(MetadataBuilder metadata, BlobHandle signature)
    {
        var il = new BlobBuilder();
        var instructions = new InstructionEncoder(il, new ControlFlowBuilder());
        instructions.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(methodBodies).AddMethodBody(instructions, maxStack: 0);
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

    static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string sourceDirectory = Path.Combine(directory.FullName, "src", "ILInspector.Instructions");
            if (Directory.Exists(sourceDirectory))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing src/ILInspector.Instructions.");
    }

    readonly record struct GraphEdgeCount(
        string File,
        int DeclaringTypeEdges,
        int TypeReferenceEdges);
}
