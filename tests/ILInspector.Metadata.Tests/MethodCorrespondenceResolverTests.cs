using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public sealed class MethodCorrespondenceResolverTests
{
    static string AssemblyPath => typeof(MethodCorrespondenceResolverTests).Assembly.Location;

    [Fact]
    public void Resolve_ReturnsExactAddressAcrossReadersOfSameArtifact()
    {
        using var oldImage = Open(AssemblyPath);
        using var newImage = Open(AssemblyPath);
        var source = FindMethod(oldImage.Reader, nameof(CorrespondenceFixture), nameof(CorrespondenceFixture.Transform));

        var result = MethodCorrespondenceResolver.Resolve(
            oldImage.Reader,
            MetadataMethodAddress.Create(oldImage.Reader, source),
            newImage.Reader);

        Assert.Equal(MethodCorrespondenceStatus.Exact, result.Status);
        Assert.True(result.IsExact);
        Assert.NotNull(result.Anchor);
        var target = Assert.IsType<MetadataMethodAddress>(result.Target);
        Assert.True(target.BelongsTo(newImage.Reader));
        Assert.Single(result.Candidates);
        Assert.Null(result.Failure);
    }

    [Fact]
    public void Resolve_ReturnsAbsentForNearMissInAnotherModule()
    {
        using var sourceImage = Open(AssemblyPath);
        using var targetImage = Open(typeof(object).Assembly.Location);
        var source = FindMethod(sourceImage.Reader, nameof(CorrespondenceFixture), nameof(CorrespondenceFixture.Transform));

        var result = MethodCorrespondenceResolver.Resolve(
            sourceImage.Reader,
            MetadataMethodAddress.Create(sourceImage.Reader, source),
            targetImage.Reader);

        Assert.Equal(MethodCorrespondenceStatus.Absent, result.Status);
        Assert.Null(result.Target);
        Assert.Empty(result.Candidates);
        Assert.NotNull(result.Anchor);
    }

    [Fact]
    public void Resolve_ReturnsFailedForSourceAddressFromWrongModule()
    {
        using var sourceImage = Open(AssemblyPath);
        using var otherImage = Open(typeof(object).Assembly.Location);
        var otherMethod = otherImage.Reader.MethodDefinitions.First();

        var result = MethodCorrespondenceResolver.Resolve(
            sourceImage.Reader,
            MetadataMethodAddress.Create(otherImage.Reader, otherMethod),
            sourceImage.Reader);

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Null(result.Target);
        Assert.Contains("different metadata module", result.Failure);
    }

    [Fact]
    public void Resolve_ReturnsFailedWithinBudgetForDeepOversizedStructuralSignature()
    {
        byte[] image = BuildConstrainedMethodImage(
            constraintCopies: 500,
            typeSpecificationDepth: 400);
        using var sourcePe = new PEReader(new MemoryStream(image));
        using var targetPe = new PEReader(new MemoryStream(image));
        MetadataReader sourceReader = sourcePe.GetMetadataReader();
        MetadataReader targetReader = targetPe.GetMetadataReader();
        MethodDefinitionHandle sourceMethod =
            sourceReader.MethodDefinitions.Single();

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        MethodCorrespondenceResult result =
            MethodCorrespondenceResolver.Resolve(
                sourceReader,
                MetadataMethodAddress.Create(sourceReader, sourceMethod),
                targetReader);
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Equal(MethodCorrespondenceStatus.Failed, result.Status);
        Assert.Null(result.Target);
        Assert.Empty(result.Candidates);
        Assert.Contains("BadImageFormatException", result.Failure);
        Assert.True(
            allocated < 64 * 1024 * 1024,
            $"Deep TypeSpec rejection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void BuildMethodKey_CumulativeWorkBudgetFailsBeforeRepeatingDecode()
    {
        byte[] image = BuildConstrainedMethodImage(
            constraintCopies: 380,
            methodCount: 10,
            constraintTypeNameLength: 2048);
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        var methods = reader.MethodDefinitions.ToArray();
        var builder = new StructuralSignatureBuilder(reader);

        int firstFailure = -1;
        for (int i = 0; i < methods.Length; i++)
        {
            try
            {
                _ = builder.BuildMethodKey(
                    reader.GetMethodDefinition(methods[i]));
            }
            catch (BadImageFormatException)
            {
                firstFailure = i;
                break;
            }
        }

        Assert.InRange(firstFailure, 1, methods.Length - 2);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<BadImageFormatException>(
            () => builder.BuildMethodKey(
                reader.GetMethodDefinition(methods[firstFailure + 1])));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.True(
            allocated < 1024 * 1024,
            $"A repeated exhausted-budget call allocated {allocated:N0} bytes.");
    }

    static byte[] BuildConstrainedMethodImage(
        int constraintCopies,
        int methodCount = 1,
        int typeSpecificationDepth = 0,
        int constraintTypeNameLength = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Probe.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Probe"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        var disposable = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString(
                constraintTypeNameLength == 0
                    ? "IDisposable"
                    : new string('X', constraintTypeNameLength)));
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddTypeDefinition(
                TypeAttributes.Public,
                default,
                metadata.GetOrAddString($"C{i}"),
                default,
                MetadataTokens.FieldDefinitionHandle(1),
                MetadataTokens.MethodDefinitionHandle(1 + i));
        }

        BlobHandle signature = metadata.GetOrAddBlob(
            new byte[] { 0x10, 0x01, 0x00, 0x01 });
        var methods = new List<MethodDefinitionHandle>(methodCount);
        for (int i = 0; i < methodCount; i++)
        {
            methods.Add(metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("M"),
                signature,
                bodyOffset: 0,
                MetadataTokens.ParameterHandle(1)));
        }

        BlobHandle typeSpecification = default;
        if (typeSpecificationDepth > 0)
        {
            var type = new BlobBuilder();
            for (int i = 0; i < typeSpecificationDepth; i++)
                type.WriteByte(0x1D);
            type.WriteByte(0x08);
            typeSpecification = metadata.GetOrAddBlob(type);
        }

        foreach (MethodDefinitionHandle method in methods)
        {
            var parameter = metadata.AddGenericParameter(
                method,
                GenericParameterAttributes.None,
                metadata.GetOrAddString("T"),
                index: 0);
            for (int i = 0; i < constraintCopies; i++)
            {
                EntityHandle constraint = typeSpecificationDepth == 0
                    ? disposable
                    : metadata.AddTypeSpecification(typeSpecification);
                metadata.AddGenericParameterConstraint(parameter, constraint);
            }
        }

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static MethodDefinitionHandle FindMethod(MetadataReader reader, string typeName, string methodName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != typeName)
                continue;
            foreach (var methodHandle in type.GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) == methodName)
                    return methodHandle;
            }
        }

        throw new InvalidOperationException($"Method '{typeName}::{methodName}' was not found.");
    }

    static MetadataImage Open(string path) => new(path);

    sealed class MetadataImage : IDisposable
    {
        readonly Stream _stream;
        readonly PEReader _pe;

        public MetadataImage(string path)
        {
            _stream = File.OpenRead(path);
            _pe = new PEReader(_stream);
            Reader = _pe.GetMetadataReader();
        }

        public MetadataReader Reader { get; }

        public void Dispose()
        {
            _pe.Dispose();
            _stream.Dispose();
        }
    }
}

public sealed class CorrespondenceFixture
{
    public int Transform(string value) => value.Length;
}
