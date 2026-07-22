using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MethodStructuralSignatureTests
{
    [Fact]
    public void Build_EncodesGenericParametersPositionally_NotByName()
    {
        string key = BuildFor(nameof(StructuralSignatureFixture), nameof(StructuralSignatureFixture.Generic));

        // A renamed generic parameter must not change the key, so the parameter
        // is encoded positionally (!!0), never by its source name.
        Assert.Contains("!!0", key);
        Assert.DoesNotContain("TItem", key);
        Assert.Contains("`1", key);
    }

    [Fact]
    public void Build_IncludesReturnType()
    {
        string key = BuildFor(nameof(StructuralSignatureFixture), nameof(StructuralSignatureFixture.Transform));

        Assert.EndsWith(":System.Int32", key);
        Assert.Contains("System.String", key);
    }

    [Fact]
    public void Build_PreservesByReferenceParameters()
    {
        string key = BuildFor(nameof(StructuralSignatureFixture), nameof(StructuralSignatureFixture.ByRef));

        Assert.Contains("System.Int32&", key);
    }

    [Fact]
    public void Build_PreservesCustomModifiersOnInParameters()
    {
        string key = BuildFor(nameof(IStructuralSignatureFixture), nameof(IStructuralSignatureFixture.Consume));

        // Interface/virtual 'in' parameters carry modreq(InAttribute); dropping
        // the modifier would erase a real signature distinction.
        Assert.Contains("modreq(", key);
        Assert.Contains("InAttribute", key);
    }

    [Fact]
    public void Build_PreservesNestedVersusNamespaceBoundary()
    {
        string key = BuildFor(nameof(StructuralSignatureFixture), nameof(StructuralSignatureFixture.Nested));

        // Nested types are joined with '+', so a nested 'Inner' never collapses
        // onto a same-named namespace member.
        Assert.Contains("StructuralSignatureFixture+Inner", key);
    }

    [Fact]
    public void Build_DistinguishesInstanceFromStatic()
    {
        string instance = BuildFor(nameof(StructuralSignatureFixture), nameof(StructuralSignatureFixture.Transform));
        string @static = BuildFor(nameof(StructuralSignatureFixture), nameof(StructuralSignatureFixture.StaticNoArgs));

        Assert.Contains("instance", instance);
        Assert.Contains("static", @static);
    }

    static string BuildFor(string typeName, string methodName)
    {
        using var image = new MetadataImage(typeof(MethodStructuralSignatureTests).Assembly.Location);
        var handle = FindMethod(image.Reader, typeName, methodName);
        return MethodStructuralSignature.Build(image.Reader, image.Reader.GetMethodDefinition(handle));
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

public sealed class StructuralSignatureFixture
{
    public sealed class Inner;

    public int Transform(string value) => value.Length;

    public void Generic<TItem>(TItem item) => _ = item;

    public void ByRef(ref int value) => value++;

    public void In(in int value) => _ = value;

    public void Nested(Inner inner) => _ = inner;

    public static int StaticNoArgs() => 0;
}

public interface IStructuralSignatureFixture
{
    void Consume(in int value);
}
