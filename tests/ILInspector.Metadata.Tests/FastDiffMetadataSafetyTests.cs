using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Cyclic and deep metadata graphs must make the affected Type Indeterminate
/// rather than exhaust the stack. The checks run in a child process so a
/// regression fails the test instead of terminating the test host.
/// </summary>
public sealed class FastDiffMetadataSafetyTests
{
    const string WorkerVariable = "DOTNET_INSPECT_FAST_DIFF_METADATA_SAFETY_WORKER";
    const int DeepGraphLength = 100_000;

    [Fact]
    public void CyclicAndDeepMetadataGraphs_AreIndeterminateInChildProcess()
        => RunWorker(nameof(MetadataSafetyWorker));

    [Fact]
    public void MetadataSafetyWorker()
    {
        if (Environment.GetEnvironmentVariable(WorkerVariable) != nameof(MetadataSafetyWorker))
            return;

        AssertIndeterminate(BaseTypeReferenceImage(depth: 1, cyclic: true), "N.C");
        AssertIndeterminate(BaseTypeReferenceImage(DeepGraphLength, cyclic: false), "N.C");
        AssertIndeterminate(BaseTypeSpecificationImage(), "N.C");
        AssertIndeterminate(DeepFieldSignatureImage(), "N.C");
        AssertAllIndeterminateOrAbsent(NestedTypeImage(depth: 1, cyclic: true));
        AssertDeepNestingContained(NestedTypeImage(DeepGraphLength, cyclic: false));
    }

    static void AssertIndeterminate(byte[] image, string fullName)
    {
        FastDiffTypeState state = Assert.Single(Compare(image).Types, type => type.FullName == fullName);
        Assert.Equal(FastDiffState.Indeterminate, state.Api);
        Assert.Equal(FastDiffState.Indeterminate, state.Body);
    }

    static void AssertAllIndeterminateOrAbsent(byte[] image)
    {
        Assert.All(Compare(image).Types, type =>
        {
            Assert.Equal(FastDiffState.Indeterminate, type.Api);
            Assert.Equal(FastDiffState.Indeterminate, type.Body);
        });
    }

    static void AssertDeepNestingContained(byte[] image)
    {
        FastDiffResult result = Compare(image);
        Assert.Contains(result.Types, type => type.Api == FastDiffState.Indeterminate);
        Assert.DoesNotContain(
            result.Types,
            type => type.Api == FastDiffState.Changed || type.Body == FastDiffState.Changed);
    }

    static FastDiffResult Compare(byte[] image)
    {
        using var before = new PEReader(new MemoryStream(image, writable: false));
        using var after = new PEReader(new MemoryStream(image, writable: false));
        return FastDiff.Compare(before, after);
    }

    static void RunWorker(string workerMethod)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(typeof(FastDiffMetadataSafetyTests).Assembly.Location);
        startInfo.ArgumentList.Add("--filter-method");
        startInfo.ArgumentList.Add($"*{workerMethod}*");
        startInfo.Environment[WorkerVariable] = workerMethod;

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\n"
                + $"stdout:\n{standardOutput.GetAwaiter().GetResult()}\n"
                + $"stderr:\n{standardError.GetAwaiter().GetResult()}");
    }

    /// <summary>A public <c>N.C</c> whose base Type is a TypeRef chain.</summary>
    static byte[] BaseTypeReferenceImage(int depth, bool cyclic)
    {
        MetadataBuilder metadata = CreateMetadata();
        EntityHandle baseType;
        if (cyclic)
        {
            baseType = metadata.AddTypeReference(
                MetadataTokens.TypeReferenceHandle(1),
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("Loop"));
        }
        else
        {
            var runtime = metadata.AddAssemblyReference(
                metadata.GetOrAddString("System.Runtime"),
                new Version(11, 0, 0, 0),
                default,
                default,
                default,
                default);
            TypeReferenceHandle reference = metadata.AddTypeReference(
                runtime,
                metadata.GetOrAddString("System"),
                metadata.GetOrAddString("Object"));
            for (int i = 1; i < depth; i++)
                reference = metadata.AddTypeReference(reference, default, metadata.GetOrAddString("T"));
            baseType = reference;
        }
        AddPublicType(metadata, baseType);
        return Serialize(metadata);
    }

    /// <summary>A public <c>N.C</c> whose base Type is a TypeSpec naming itself.</summary>
    static byte[] BaseTypeSpecificationImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        // ELEMENT_TYPE_CLASS followed by the coded TypeDefOrRefOrSpec index of TypeSpec row 1.
        var specification = metadata.AddTypeSpecification(
            metadata.GetOrAddBlob(new byte[] { 0x12, (1 << 2) | 2 }));
        AddPublicType(metadata, specification);
        return Serialize(metadata);
    }

    /// <summary>A public <c>N.C</c> with a public field whose Type nests arrays far too deep.</summary>
    static byte[] DeepFieldSignatureImage()
    {
        MetadataBuilder metadata = CreateMetadata();
        var signature = new BlobBuilder();
        signature.WriteByte(0x06);
        for (int i = 0; i < DeepGraphLength; i++)
            signature.WriteByte(0x1D);
        signature.WriteByte(0x08);
        var field = metadata.AddFieldDefinition(
            FieldAttributes.Public,
            metadata.GetOrAddString("Deep"),
            metadata.GetOrAddBlob(signature));
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            field,
            MetadataTokens.MethodDefinitionHandle(1));
        return Serialize(metadata);
    }

    static byte[] NestedTypeImage(int depth, bool cyclic)
    {
        MetadataBuilder metadata = CreateMetadata();
        TypeDefinitionHandle parent = default;
        for (int i = 0; i < depth; i++)
        {
            TypeDefinitionHandle current = metadata.AddTypeDefinition(
                i == 0 ? TypeAttributes.Public : TypeAttributes.NestedPublic,
                i == 0 ? metadata.GetOrAddString("N") : default,
                metadata.GetOrAddString("T"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1));
            if (!parent.IsNil)
                metadata.AddNestedType(current, parent);
            parent = current;
        }
        if (cyclic)
            metadata.AddNestedType(parent, parent);
        return Serialize(metadata);
    }

    static MetadataBuilder CreateMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Safety.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Safety"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        return metadata;
    }

    static void AddPublicType(MetadataBuilder metadata, EntityHandle baseType)
        => metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Class,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            baseType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

    static byte[] Serialize(MetadataBuilder metadata)
    {
        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }
}
