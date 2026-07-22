using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Tests for <c>DynamicAttribute</c> transform-flag reading and <c>dynamic</c>
/// rendering in API signatures. Uses the test assembly itself (whose fixture
/// types below are compiled by Roslyn, the authoritative encoder of the
/// transform-flags array) as the subject, so the decoder is validated against
/// real compiler-emitted metadata rather than synthetic flags.
/// </summary>
public sealed class DynamicTypeViewTests
{
    private static readonly ApiSurface Surface;

    static DynamicTypeViewTests()
    {
        var assemblyPath = typeof(DynamicTypeViewTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    private static ApiType GetType(string name) =>
        Surface.Types.First(t => t.Name == name);

    private static ApiMember GetMethod(string methodName) =>
        GetType(nameof(DynamicSampleClass)).Members.First(m => m.Name == methodName && m.Kind == "method");

    private static ApiMember GetProperty(string propName) =>
        GetType(nameof(DynamicSampleClass)).Members.First(m => m.Name == propName && m.Kind == "property");

    private static ApiMember GetField(string fieldName) =>
        GetType(nameof(DynamicSampleClass)).Members.First(m => m.Name == fieldName && m.Kind == "field");

    // --- Direct TypeNode.ApplyDynamic unit tests ---

    [Fact]
    public void ObjectNode_MarkerFlag_RendersDynamic()
    {
        var node = new PrimitiveTypeNode("object", isReferenceType: true);
        byte[] flags = [1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("dynamic", node.Render());
        Assert.Equal(1, pos);
    }

    [Fact]
    public void ObjectNode_ZeroFlag_StaysObject()
    {
        var node = new PrimitiveTypeNode("object", isReferenceType: true);
        byte[] flags = [0];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("object", node.Render());
        Assert.Equal(1, pos);
    }

    [Fact]
    public void NonObjectNode_FlagIgnored()
    {
        // A stray true flag on a non-object position must never render dynamic.
        var node = new PrimitiveTypeNode("string", isReferenceType: true);
        byte[] flags = [1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("string", node.Render());
        Assert.Equal(1, pos); // flag still consumed to keep the walk aligned
    }

    [Fact]
    public void NullFlags_LeaveObjectUnchanged()
    {
        var node = new PrimitiveTypeNode("object", isReferenceType: true);
        int pos = 0;
        node.ApplyDynamic(null, ref pos);
        Assert.Equal("object", node.Render());
    }

    [Fact]
    public void GenericNode_MixedArgs_AppliesToCorrectPosition()
    {
        // Dictionary<object, dynamic>: flags [Dict=false, object-key=false, dynamic-value=true]
        var node = new GenericTypeNode("Dictionary", isReferenceType: true,
            [new PrimitiveTypeNode("object", true), new PrimitiveTypeNode("object", true)]);
        byte[] flags = [0, 0, 1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("Dictionary<object, dynamic>", node.Render());
    }

    [Fact]
    public void GenericNode_DynamicKeyObjectValue_AppliesToCorrectPosition()
    {
        // Dictionary<dynamic, object>: flags [Dict=false, dynamic-key=true, object-value=false]
        var node = new GenericTypeNode("Dictionary", isReferenceType: true,
            [new PrimitiveTypeNode("object", true), new PrimitiveTypeNode("object", true)]);
        byte[] flags = [0, 1, 0];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("Dictionary<dynamic, object>", node.Render());
    }

    [Fact]
    public void ArrayNode_DynamicElement()
    {
        // dynamic[]: flags [array=false, object-element=true]
        var node = new SZArrayTypeNode(new PrimitiveTypeNode("object", true));
        byte[] flags = [0, 1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("dynamic[]", node.Render());
    }

    [Fact]
    public void ByRefNode_ConsumesLeadingFlagThenElement()
    {
        // ref dynamic: the transform-flags array reserves a false slot for the
        // by-ref, then the object element carries the true flag.
        var inner = new PrimitiveTypeNode("object", true);
        var node = new ByRefTypeNode(inner);
        byte[] flags = [0, 1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("ref dynamic", node.Render());
        Assert.Equal(2, pos);
    }

    [Fact]
    public void NullableAnnotatedDynamic_RendersDynamicQuestion()
    {
        var node = new PrimitiveTypeNode("object", isReferenceType: true) { IsNullableAnnotated = true };
        byte[] flags = [1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("dynamic?", node.Render());
    }

    // --- End-to-end: signatures extracted from Roslyn-compiled fixtures ---

    [Fact]
    public void Method_BareDynamicParam()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesDynamic));
        Assert.Contains("dynamic value", member.Signature);
        Assert.DoesNotContain("object", member.Signature);
    }

    [Fact]
    public void Method_DynamicReturn()
    {
        var member = GetMethod(nameof(DynamicSampleClass.ReturnsDynamic));
        Assert.StartsWith("dynamic ", member.Signature);
    }

    [Fact]
    public void Method_PlainObjectParam_StaysObject()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesObject));
        Assert.Contains("object value", member.Signature);
        Assert.DoesNotContain("dynamic", member.Signature);
    }

    [Fact]
    public void Method_DynamicArray()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesDynamicArray));
        Assert.Contains("dynamic[] values", member.Signature);
    }

    [Fact]
    public void Method_ListOfDynamic()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesListOfDynamic));
        Assert.Contains("List<dynamic>", member.Signature);
    }

    [Fact]
    public void Method_DictionaryObjectKeyDynamicValue()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesDictionaryObjectDynamic));
        Assert.Contains("Dictionary<object, dynamic>", member.Signature);
    }

    [Fact]
    public void Method_DictionaryDynamicKeyObjectValue()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesDictionaryDynamicObject));
        Assert.Contains("Dictionary<dynamic, object>", member.Signature);
    }

    [Fact]
    public void Method_NestedGenericDynamic()
    {
        // Raw ApiSurface renders full namespaces; assert the namespace-agnostic
        // innermost shape, which proves the dynamic lands at the deepest position.
        var member = GetMethod(nameof(DynamicSampleClass.TakesNestedGeneric));
        Assert.Contains("Dictionary<string, dynamic>>", member.Signature);
    }

    [Fact]
    public void Method_FuncObjectToDynamic_KeepsObjectArg()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesFuncObjectDynamic));
        Assert.Contains("Func<object, dynamic>", member.Signature);
    }

    [Fact]
    public void Method_MixedObjectAndDynamicParams()
    {
        var member = GetMethod(nameof(DynamicSampleClass.MixedParams));
        Assert.Contains("object plain", member.Signature);
        Assert.Contains("dynamic flexible", member.Signature);
    }

    [Fact]
    public void Method_RefDynamic()
    {
        var member = GetMethod(nameof(DynamicSampleClass.TakesRefDynamic));
        Assert.Contains("ref dynamic value", member.Signature);
    }

    [Fact]
    public void Property_Dynamic()
    {
        var member = GetProperty(nameof(DynamicSampleClass.DynamicProp));
        Assert.Contains("dynamic", member.Signature);
        Assert.DoesNotContain("object", member.Signature);
    }

    [Fact]
    public void Field_Dynamic()
    {
        var member = GetField(nameof(DynamicSampleClass.DynamicField));
        Assert.Equal("dynamic", member.ReturnType);
    }

    [Fact]
    public void Field_PlainObject_StaysObject()
    {
        var member = GetField(nameof(DynamicSampleClass.ObjectField));
        Assert.Equal("object", member.ReturnType);
    }
}

// ===== Test fixture types with a spread of dynamic type shapes =====

/// <summary>Sample class exercising DynamicAttribute transform-flag decoding.</summary>
public class DynamicSampleClass
{
    public dynamic DynamicField = null!;
    public object ObjectField = null!;

    public dynamic DynamicProp { get; set; } = null!;

    public void TakesDynamic(dynamic value) { }
    public void TakesObject(object value) { }
    public dynamic ReturnsDynamic() => null!;

    public void TakesDynamicArray(dynamic[] values) { }
    public void TakesListOfDynamic(List<dynamic> values) { }
    public void TakesDictionaryObjectDynamic(Dictionary<object, dynamic> map) { }
    public void TakesDictionaryDynamicObject(Dictionary<dynamic, object> map) { }
    public void TakesNestedGeneric(List<Dictionary<string, dynamic>> nested) { }
    public void TakesFuncObjectDynamic(Func<object, dynamic> projector) { }
    public void MixedParams(object plain, dynamic flexible) { }
    public void TakesRefDynamic(ref dynamic value) { value = null!; }
}
