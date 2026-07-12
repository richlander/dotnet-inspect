using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

public class SignatureDecoderSafetyTests
{
    const string WorkerVariable = "DOTNET_INSPECT_SIGNATURE_DECODER_WORKER";

    [Fact]
    public void SelfReferentialTypeSpec_IsContainedInChildProcess()
        => RunWorker(nameof(SelfReferentialTypeSpecWorker));

    [Fact]
    public void DeepTypeSpec_IsContainedInChildProcess()
        => RunWorker(nameof(DeepTypeSpecWorker));

    [Fact]
    public void DeepMethodSignatureGateway_IsContainedInChildProcess()
        => RunWorker(nameof(DeepMethodSignatureGatewayWorker));

    [Fact]
    public void SignatureBlobGuard_OldAssemblyIdentity_IsForwarded()
        => Assert.Equal(
            typeof(SignatureBlobGuard),
            Type.GetType("ILInspector.Metadata.SignatureBlobGuard, ILInspector.Metadata"));

    [Fact]
    public void SelfReferentialTypeSpecWorker()
    {
        if (!IsSelectedWorker(nameof(SelfReferentialTypeSpecWorker)))
            return;

        var reader = BuildTypeSpec(signature =>
        {
            signature.WriteByte(0x1f); // CMOD_REQD
            signature.WriteByte(0x06); // TypeDefOrRefOrSpec: TypeSpec row 1
            signature.WriteByte(0x08); // I4
        });

        Assert.Equal(
            "int",
            TypeResolver.GetTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)));
    }

    [Fact]
    public void DeepTypeSpecWorker()
    {
        if (!IsSelectedWorker(nameof(DeepTypeSpecWorker)))
            return;

        var reader = BuildTypeSpec(signature =>
        {
            for (int i = 0; i < 100_000; i++)
                signature.WriteByte(0x1d); // SZARRAY
            signature.WriteByte(0x08);     // I4
        });

        Assert.Equal(
            "object",
            TypeResolver.GetTypeNameFromSpecification(
                reader,
                MetadataTokens.TypeSpecificationHandle(1)));
    }

    [Fact]
    public void DeepMethodSignatureGatewayWorker()
    {
        if (!IsSelectedWorker(nameof(DeepMethodSignatureGatewayWorker)))
            return;

        var signature = new BlobBuilder();
        signature.WriteByte(0x00); // default method signature
        signature.WriteByte(0x00); // zero parameters
        for (int i = 0; i < 100_000; i++)
            signature.WriteByte(0x1d); // SZARRAY return type
        signature.WriteByte(0x08);     // I4

        MethodDefinitionHandle methodHandle = default;
        TypeDefinitionHandle typeHandle = default;
        var reader = BuildAssembly(metadata =>
        {
            methodHandle = metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                metadata.GetOrAddBlob(signature),
                bodyOffset: -1,
                parameterList: MetadataTokens.ParameterHandle(1));
            typeHandle = metadata.AddTypeDefinition(
                TypeAttributes.Public,
                metadata.GetOrAddString("N"),
                metadata.GetOrAddString("C"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                methodHandle);
        });

        var decoded = MetadataDeclarationQuery.GetMethodReturnType(
            reader,
            reader.GetTypeDefinition(typeHandle),
            reader.GetMethodDefinition(methodHandle));

        Assert.Equal("object", decoded);
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
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\nstdout:\n{standardOutput}\nstderr:\n{standardError}");
    }

    static MetadataReader BuildTypeSpec(Action<BlobBuilder> writeSignature)
    {
        var signature = new BlobBuilder();
        writeSignature(signature);
        return BuildAssembly(metadata => metadata.AddTypeSpecification(metadata.GetOrAddBlob(signature)));
    }

    static MetadataReader BuildAssembly(Action<MetadataBuilder> addRows)
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

        addRows(metadata);

        var root = new MetadataRootBuilder(metadata, suppressValidation: true);
        var image = new BlobBuilder();
        root.Serialize(image, 0, 0);
        return MetadataReaderProvider
            .FromMetadataImage(ImmutableArray.Create(image.ToArray()))
            .GetMetadataReader();
    }
}
