using System.Reflection.PortableExecutable;
using System.Text.Json;
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

    // --- Custom-modifier alignment: `in`/`ref readonly` carry a modreq(In) ---

    [Fact]
    public void Method_InDynamic_ConsumesModifierSlot()
    {
        // `in dynamic` decodes as ByRef(Modified(modreq In, object)). The
        // DynamicAttribute transform-flags reserve a slot for the by-ref AND a
        // slot for the custom modifier, so the walk must consume both before the
        // object position or the `true` flag lands on the wrong node.
        var member = GetMethod(nameof(DynamicSampleClass.TakesInDynamic));
        Assert.Contains("dynamic value", member.Signature);
        Assert.DoesNotContain("object", member.Signature);
    }

    [Fact]
    public void Method_RefReadonlyDynamicReturn_ConsumesModifierSlot()
    {
        var member = GetMethod(nameof(DynamicSampleClass.RefReadonlyDynamic));
        Assert.Contains("dynamic", member.Signature);
        Assert.DoesNotContain("object", member.Signature);
    }

    // --- Extractor coverage: events and base types ---

    [Fact]
    public void Event_DynamicHandler()
    {
        var member = GetType(nameof(DynamicSampleClass)).Members
            .First(m => m.Name == nameof(DynamicSampleClass.DynamicEvent) && m.Kind == "event");
        Assert.Contains("EventHandler<dynamic>", member.Signature);
        Assert.DoesNotContain("object", member.Signature);
    }

    [Fact]
    public void BaseType_Dynamic()
    {
        var type = GetType(nameof(DynamicDerived));
        Assert.NotNull(type.BaseType);
        Assert.Contains("dynamic", type.BaseType!);
        Assert.DoesNotContain("object", type.BaseType!);
    }

    // --- Identity isolation: canonical/XML-doc identity must stay `object` ---

    [Fact]
    public void Identity_DynamicParam_CanonicalUsesObjectNotDynamic()
    {
        var type = GetType(nameof(DynamicSampleClass));
        var member = GetMethod(nameof(DynamicSampleClass.TakesDynamic));
        var canonical = ApiMemberIdentity.GetCanonicalSignature(type, member);
        Assert.Contains("object", canonical);
        Assert.DoesNotContain("dynamic", canonical);
    }

    [Fact]
    public void Identity_DynamicParam_XmlDocUsesObject()
    {
        var type = GetType(nameof(DynamicSampleClass));
        var member = GetMethod(nameof(DynamicSampleClass.TakesDynamic));
        Assert.True(ApiMemberIdentity.TryGetXmlDocMemberIdentity(type, member, out var identity));
        Assert.Contains(identity.NormalizedParameters, p => p.Contains("System.Object"));
        Assert.DoesNotContain(identity.NormalizedParameters, p => p.Contains("dynamic"));
    }

    [Fact]
    public void Identity_NestedDynamic_CanonicalUsesObjectNotDynamic()
    {
        var type = GetType(nameof(DynamicSampleClass));
        var member = GetMethod(nameof(DynamicSampleClass.TakesListOfDynamic));
        var canonical = ApiMemberIdentity.GetCanonicalSignature(type, member);
        Assert.DoesNotContain("dynamic", canonical);
    }

    // --- Identity must survive a JSON round-trip (SignatureModel is [JsonIgnore],
    //     so identity falls back to parsing the raw display signature, which carries
    //     `dynamic`; the fallback must scrub it to `object` exactly like the live path). ---

    [Fact]
    public void Identity_DynamicParam_CanonicalStableAcrossJsonRoundTrip()
    {
        var liveType = GetType(nameof(DynamicSampleClass));
        var liveMember = GetMethod(nameof(DynamicSampleClass.TakesDynamic));
        var liveCanonical = ApiMemberIdentity.GetCanonicalSignature(liveType, liveMember);

        var json = JsonSerializer.Serialize(Surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;
        var rtType = roundTripped.Types.First(t => t.Name == nameof(DynamicSampleClass));
        var rtMember = rtType.Members.First(m =>
            m.Name == nameof(DynamicSampleClass.TakesDynamic) && m.Kind == "method");
        Assert.Null(rtMember.SignatureModel);

        var rtCanonical = ApiMemberIdentity.GetCanonicalSignature(rtType, rtMember);
        Assert.DoesNotContain("dynamic", rtCanonical);
        Assert.Contains("object", rtCanonical);
        Assert.Equal(liveCanonical, rtCanonical);
    }

    [Fact]
    public void Identity_DynamicIndexer_CanonicalStableAcrossJsonRoundTrip()
    {
        var liveType = GetType(nameof(DynamicSampleClass));
        var liveIndexer = liveType.Members.First(m =>
            m.Kind == "property" && m.SignatureModel is { Parameters.Count: > 0 });
        var liveCanonical = ApiMemberIdentity.GetCanonicalSignature(liveType, liveIndexer);
        Assert.DoesNotContain("dynamic", liveCanonical);

        var json = JsonSerializer.Serialize(Surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;
        var rtType = roundTripped.Types.First(t => t.Name == nameof(DynamicSampleClass));
        var rtIndexer = rtType.Members.First(m =>
            m.Kind == "property" && m.Signature != null
            && m.Signature.Contains("this[", StringComparison.Ordinal));
        Assert.Null(rtIndexer.SignatureModel);

        var rtCanonical = ApiMemberIdentity.GetCanonicalSignature(rtType, rtIndexer);
        Assert.DoesNotContain("dynamic", rtCanonical);
        Assert.Equal(liveCanonical, rtCanonical);
    }

    // --- Marker-form flag must not broadcast into inner nodes ---

    [Fact]
    public void MarkerFlag_OnArray_DoesNotBroadcastToElement()
    {
        // A single-element (marker-form) flag array applies only to the first
        // position; it must NOT broadcast `true` to inner element nodes. Only a
        // bare top-level object ever carries the marker form in real metadata,
        // but adversarial metadata can attach it to a composite.
        var node = new SZArrayTypeNode(new PrimitiveTypeNode("object", true));
        byte[] flags = [1];
        int pos = 0;
        node.ApplyDynamic(flags, ref pos);
        Assert.Equal("object[]", node.Render());
    }

    // --- IsTopLevelDynamic predicate: only the outermost (index-0) position ---

    [Theory]
    [InlineData(null, false)]              // attribute absent -> plain object
    [InlineData(new byte[] { }, false)]    // empty flags -> not dynamic
    [InlineData(new byte[] { 0 }, false)]  // object at the top-level position
    [InlineData(new byte[] { 1 }, true)]   // bare `dynamic` (or marker form)
    [InlineData(new byte[] { 0, 1 }, false)] // `Func<dynamic>` -> nested, not top-level
    [InlineData(new byte[] { 1, 0 }, true)]  // `dynamic` outer with a non-dynamic arg
    public void IsTopLevelDynamic_ReadsOutermostPositionOnly(byte[]? flags, bool expected)
    {
        Assert.Equal(expected, DynamicReader.IsTopLevelDynamic(flags));
    }

    // --- IsByRefElementDynamic predicate: the element sits at index 1 ---------

    [Theory]
    [InlineData(null, false)]                 // attribute absent -> object element
    [InlineData(new byte[] { }, false)]       // empty flags -> not dynamic
    [InlineData(new byte[] { 0 }, false)]     // ByRef modifier only, no element flag
    [InlineData(new byte[] { 1 }, false)]     // single flag is the top-level form, not by-ref element
    [InlineData(new byte[] { 0, 1 }, true)]   // `ref dynamic` -> ByRef at 0, dynamic element at 1
    [InlineData(new byte[] { 0, 0 }, false)]  // `ref object` (unusual explicit form) -> object element
    public void IsByRefElementDynamic_ReadsElementPositionOnly(byte[]? flags, bool expected)
    {
        Assert.Equal(expected, DynamicReader.IsByRefElementDynamic(flags));
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
    public void TakesInDynamic(in dynamic value) { }

    private static dynamic _refDynamic = null!;
    public ref readonly dynamic RefReadonlyDynamic() => ref _refDynamic;

    public dynamic this[dynamic key] { get => null!; set { } }

    public event EventHandler<dynamic> DynamicEvent = null!;
}

/// <summary>Generic base used to exercise dynamic decoding of base types.</summary>
public class DynamicBase<T>
{
}

/// <summary>Derives from a generic base instantiated with <c>dynamic</c>.</summary>
public class DynamicDerived : DynamicBase<dynamic>
{
}
