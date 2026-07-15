using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.CSharp.Tests;

public sealed class TypeShellProducerTests
{
    [Fact]
    public void RequiresAsyncBodyModifier_UsesDefiningMethodMetadata()
    {
        using var pe = new PEReader(File.OpenRead(typeof(TypeShellProducerTests).Assembly.Location));
        var reader = pe.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions
            .Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == nameof(TypeShellProducerTests));
        var type = reader.GetTypeDefinition(typeHandle);
        var methods = type.GetMethods()
            .ToDictionary(
                handle => reader.GetString(reader.GetMethodDefinition(handle).Name),
                StringComparer.Ordinal);

        Assert.True(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(RuntimeAsyncFixture)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(AsyncIteratorFixture)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(IsUnsupportedSurfaceSignature_AllowsOrdinarySignatures)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            methods[nameof(IteratorFixture)]));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            MetadataTokens.GetToken(typeHandle)));
        Assert.False(TypeShellProducer.RequiresAsyncBodyModifier(
            reader,
            0x0600FFFF));
    }

    [Theory]
    [InlineData("delegate*<int, void>")]
    [InlineData("@delegate*<int, void>")]
    [InlineData("<>c__DisplayClass0_0")]
    [InlineData("(int, string){")]
    public void IsUnsupportedSurfaceSignature_FlagsUnrepresentableShapes(string signature)
    {
        Assert.True(TypeShellProducer.IsUnsupportedSurfaceSignature(signature));
    }

    [Theory]
    [InlineData("System.Int32")]
    [InlineData("Samples.Widget")]
    [InlineData("System.Collections.Generic.List<int>")]
    [InlineData("int[]")]
    public void IsUnsupportedSurfaceSignature_AllowsOrdinarySignatures(string signature)
    {
        Assert.False(TypeShellProducer.IsUnsupportedSurfaceSignature(signature));
    }

    [Fact]
    public void IsStaticType_DistinguishesStaticClassesFromOtherKinds()
    {
        using var pe = new PEReader(File.OpenRead(typeof(TypeShellProducerTests).Assembly.Location));
        var reader = pe.GetMetadataReader();

        TypeDefinition Type(string name) => reader.GetTypeDefinition(reader.TypeDefinitions
            .Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == name));

        Assert.True(TypeShellProducer.IsStaticType(Type(nameof(StaticFixture))));
        Assert.False(TypeShellProducer.IsStaticType(Type(nameof(InstanceFixture))));
        Assert.False(TypeShellProducer.IsStaticType(Type(nameof(IInterfaceFixture))));
    }

    static async Task RuntimeAsyncFixture()
        => await Task.Yield();

    static async IAsyncEnumerable<int> AsyncIteratorFixture()
    {
        await Task.Yield();
        yield return 1;
    }

    static IEnumerable<int> IteratorFixture()
    {
        yield return 1;
    }

    static class StaticFixture
    {
    }

    sealed class InstanceFixture
    {
    }

    interface IInterfaceFixture
    {
    }
}
