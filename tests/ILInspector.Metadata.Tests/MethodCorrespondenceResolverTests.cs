using System.Reflection.Metadata;
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
