using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Tests for nullability annotation reading and rendering in API signatures.
/// Uses the test assembly itself (compiled with nullable enabled) as the test subject.
/// </summary>
public sealed class NullabilityTests
{
    private static readonly ApiSurface Surface;

    static NullabilityTests()
    {
        var assemblyPath = typeof(NullabilityTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    private static ApiType GetType(string name) =>
        Surface.Types.First(t => t.Name == name);

    private static ApiType GetType(Type type)
    {
        string fullName = type.FullName!.Replace('+', '.');
        return Surface.Types.Single(candidate => candidate.FullName == fullName);
    }

    private static ApiMember GetMethod(string typeName, string methodName) =>
        GetType(typeName).Members.First(m => m.Name == methodName);

    private static ApiMember GetProperty(string typeName, string propName) =>
        GetType(typeName).Members.First(m => m.Name == propName && m.Kind == "property");

    private static ApiMember GetField(string typeName, string fieldName) =>
        GetType(typeName).Members.First(m => m.Name == fieldName && m.Kind == "field");

    // --- NullabilityReader unit tests ---

    [Fact]
    public void GetNullableContext_ReturnsNull_WhenNotPresent()
    {
        // NullableObliviousClass is defined below in a #nullable disable region,
        // so it has no NullableContextAttribute in this assembly.
        // PEReader/stream are inlined (not shared) because MetadataReader borrows memory
        // owned by PEReader — sharing a static reader would leak the file handle.
        var assemblyPath = typeof(NullabilityTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(typeDef.Name) == nameof(NullableObliviousClass))
            {
                var context = NullabilityReader.GetNullableContext(reader, typeDef.GetCustomAttributes());
                Assert.Null(context);
                return;
            }
        }
        Assert.Fail($"{nameof(NullableObliviousClass)} type definition not found in test assembly");
    }

    [Fact]
    public void GetNullableContext_ReadsValue_WhenPresent()
    {
        // See comment in GetNullableContext_ReturnsNull_WhenNotPresent for why PEReader is inlined.
        var assemblyPath = typeof(NullabilityTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(typeDef.Name) == nameof(NullableSampleClass))
            {
                var context = NullabilityReader.GetNullableContext(reader, typeDef.GetCustomAttributes());
                Assert.True(context is 1 or 2);
                return;
            }
        }
        Assert.Fail($"{nameof(NullableSampleClass)} type definition not found in test assembly");
    }

    [Fact]
    public void NullableContextScopeFixture_HasExpectedCompilerMetadata()
    {
        var assemblyPath = typeof(NullabilityTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var outer = FindType(reader, nameof(NullableContextScopeFixture));
        var inherited = FindType(
            reader,
            nameof(NullableContextScopeFixture.InheritedNullableContext));
        var obliviousNested = FindType(
            reader,
            nameof(NullableContextScopeFixture.ObliviousNullableContext));
        var enabled = FindMethod(reader, outer, nameof(NullableContextScopeFixture.Enabled));
        var oblivious = FindMethod(reader, outer, nameof(NullableContextScopeFixture.Oblivious));
        var inheritedIndexer = FindMethod(reader, inherited, "get_Item");

        Assert.Equal((byte)2, NullabilityReader.GetNullableContext(reader, outer.GetCustomAttributes()));
        Assert.Null(NullabilityReader.GetNullableContext(reader, inherited.GetCustomAttributes()));
        Assert.Equal((byte)0, NullabilityReader.GetNullableContext(reader, obliviousNested.GetCustomAttributes()));
        Assert.Null(NullabilityReader.GetNullableContext(reader, enabled.GetCustomAttributes()));
        Assert.Equal((byte)0, NullabilityReader.GetNullableContext(reader, oblivious.GetCustomAttributes()));
        Assert.Equal((byte)0, NullabilityReader.GetNullableContext(reader, inheritedIndexer.GetCustomAttributes()));
    }

    [Fact]
    public void Extract_UsesNearestNullableContextIncludingExplicitZero()
    {
        var outer = GetType(nameof(NullableContextScopeFixture));
        var enabled = Assert.Single(
            outer.Members,
            member => member.Name == nameof(NullableContextScopeFixture.Enabled));
        var oblivious = Assert.Single(
            outer.Members,
            member => member.Name == nameof(NullableContextScopeFixture.Oblivious));
        var inherited = GetType(
            typeof(NullableContextScopeFixture.InheritedNullableContext));
        var maybe = Assert.Single(
            inherited.Members,
            member => member.Name == nameof(NullableContextScopeFixture.InheritedNullableContext.Maybe));
        var indexer = Assert.Single(
            inherited.Members,
            member => member.Name == "Item");
        var obliviousNested = GetType(
            typeof(NullableContextScopeFixture.ObliviousNullableContext));
        var value = Assert.Single(
            obliviousNested.Members,
            member => member.Name == nameof(NullableContextScopeFixture.ObliviousNullableContext.Value));

        Assert.Contains("string? value", enabled.Signature, StringComparison.Ordinal);
        Assert.Contains("string value", oblivious.Signature, StringComparison.Ordinal);
        Assert.DoesNotContain("string? value", oblivious.Signature, StringComparison.Ordinal);
        Assert.Equal("string?", maybe.ReturnType);
        Assert.Contains("string key", indexer.Signature, StringComparison.Ordinal);
        Assert.DoesNotContain("string? key", indexer.Signature, StringComparison.Ordinal);
        Assert.Equal("string", value.ReturnType);
    }

    // --- TypeNode tree + rendering tests ---

    [Fact]
    public void PrimitiveNode_String_RendersNullable()
    {
        var node = new PrimitiveTypeNode("string", isReferenceType: true);
        byte[] bytes = [2];
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("string?", node.Render());
        Assert.Equal(1, pos);
    }

    [Fact]
    public void PrimitiveNode_Int_IgnoresNullability()
    {
        var node = new PrimitiveTypeNode("int", isReferenceType: false);
        byte[] bytes = [2]; // even if byte says 2, value types don't render ?
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("int", node.Render());
        Assert.Equal(1, pos); // byte still consumed
    }

    [Fact]
    public void GenericNode_NullableArgs()
    {
        // Dictionary<string?, int> where Dictionary is not nullable
        var node = new GenericTypeNode("Dictionary", isReferenceType: true,
            [new PrimitiveTypeNode("string", true), new PrimitiveTypeNode("int", false)]);
        byte[] bytes = [1, 2, 0]; // Dict=not null, string=nullable, int=oblivious
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("Dictionary<string?, int>", node.Render());
    }

    [Fact]
    public void GenericNode_NullableOuter()
    {
        // Task<string>? where Task is nullable, string is not
        var node = new GenericTypeNode("Task", isReferenceType: true,
            [new PrimitiveTypeNode("string", true)]);
        byte[] bytes = [2, 1]; // Task=nullable, string=not null
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("Task<string>?", node.Render());
    }

    [Fact]
    public void ArrayNode_NullableElement()
    {
        // string?[] where array is not nullable, string is
        var node = new SZArrayTypeNode(new PrimitiveTypeNode("string", true));
        byte[] bytes = [1, 2]; // array=not null, string=nullable
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("string?[]", node.Render());
    }

    [Fact]
    public void ArrayNode_NullableArray()
    {
        // string[]? where array is nullable, string is not
        var node = new SZArrayTypeNode(new PrimitiveTypeNode("string", true));
        byte[] bytes = [2, 1]; // array=nullable, string=not null
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("string[]?", node.Render());
    }

    [Fact]
    public void ByRefNode_DoesNotConsumeByte()
    {
        // ref string? — ByRef doesn't consume a byte
        var inner = new PrimitiveTypeNode("string", true);
        var node = new ByRefTypeNode(inner);
        byte[] bytes = [2]; // only the string's byte
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("ref string?", node.Render());
        Assert.Equal(1, pos); // only inner consumed
    }

    [Fact]
    public void SingleByteAttribute_AppliesUniformly()
    {
        // NullableAttribute(byte) applies to all positions
        var node = new GenericTypeNode("Task", isReferenceType: true,
            [new PrimitiveTypeNode("string", true)]);
        byte[] bytes = [2]; // single byte = all positions nullable
        int pos = 0;
        node.ApplyNullability(bytes, ref pos, 0);
        Assert.Equal("Task<string?>?", node.Render());
    }

    [Fact]
    public void DefaultByte_UsedWhenNoAttribute()
    {
        var node = new PrimitiveTypeNode("string", isReferenceType: true);
        int pos = 0;
        node.ApplyNullability(null, ref pos, defaultByte: 2);
        Assert.Equal("string?", node.Render());
    }

    [Fact]
    public void DefaultByte_NotAnnotated()
    {
        var node = new PrimitiveTypeNode("string", isReferenceType: true);
        int pos = 0;
        node.ApplyNullability(null, ref pos, defaultByte: 1);
        Assert.Equal("string", node.Render());
    }

    // --- End-to-end: signatures extracted from this assembly ---

    [Fact]
    public void Method_NullableStringParam()
    {
        var member = GetMethod(nameof(NullableSampleClass), nameof(NullableSampleClass.TakesNullableString));
        Assert.Contains("string?", member.Signature);
        Assert.Contains("string? value", member.Signature);
    }

    [Fact]
    public void Method_NonNullableStringParam()
    {
        var member = GetMethod(nameof(NullableSampleClass), nameof(NullableSampleClass.TakesNonNullableString));
        // Should contain "string value" without ?
        Assert.Contains("string value", member.Signature);
        Assert.DoesNotContain("string?", member.Signature);
    }

    [Fact]
    public void Method_NullableReturnType()
    {
        var member = GetMethod(nameof(NullableSampleClass), nameof(NullableSampleClass.ReturnsNullableString));
        Assert.NotNull(member.Signature);
        Assert.StartsWith("string?", member.Signature);
    }

    [Fact]
    public void Method_NonNullableReturnType()
    {
        var member = GetMethod(nameof(NullableSampleClass), nameof(NullableSampleClass.ReturnsNonNullableString));
        Assert.NotNull(member.Signature);
        Assert.StartsWith("string ", member.Signature);
    }

    [Fact]
    public void Property_NullableType()
    {
        var member = GetProperty(nameof(NullableSampleClass), nameof(NullableSampleClass.NullableProp));
        Assert.Contains("string?", member.Signature);
    }

    [Fact]
    public void Property_NonNullableType()
    {
        var member = GetProperty(nameof(NullableSampleClass), nameof(NullableSampleClass.NonNullableProp));
        Assert.DoesNotContain("string?", member.Signature);
        Assert.Contains("string ", member.Signature);
    }

    [Fact]
    public void Field_NullableType()
    {
        var member = GetField(nameof(NullableSampleClass), nameof(NullableSampleClass.NullableField));
        Assert.Contains("?", member.ReturnType);
    }

    [Fact]
    public void Method_GenericNullableArg()
    {
        var member = GetMethod(nameof(NullableSampleClass), nameof(NullableSampleClass.TakesNullableList));
        // Should show List<string?> or similar nullable inner arg
        Assert.Contains("string?", member.Signature);
    }

    static TypeDefinition FindType(MetadataReader reader, string name)
        => reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(type => reader.GetString(type.Name) == name);

    static MethodDefinition FindMethod(
        MetadataReader reader,
        TypeDefinition type,
        string name)
        => type.GetMethods()
            .Select(reader.GetMethodDefinition)
            .Single(method => reader.GetString(method.Name) == name);

}

// ===== Test fixture types compiled with #nullable enable =====

/// <summary>Sample class for testing nullable signature extraction.</summary>
public class NullableSampleClass
{
    public string? NullableField;
    public string NonNullableField = "";

    public string? NullableProp { get; set; }
    public string NonNullableProp { get; set; } = "";

    public void TakesNullableString(string? value) { }
    public void TakesNonNullableString(string value) { }

    public string? ReturnsNullableString() => null;
    public string ReturnsNonNullableString() => "";

    public void TakesNullableList(List<string?> items) { }
    public void TakesNonNullableList(List<string> items) { }

    public Dictionary<string, object?> MixedGeneric() => new();
}

#nullable disable
// Fixture type with no NullableContextAttribute — used to test GetNullableContext returns 0
public class NullableObliviousClass
{
    public string Value;
}
#nullable restore
