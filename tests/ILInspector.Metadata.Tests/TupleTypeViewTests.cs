using System.Collections.Immutable;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Tests for <c>TupleElementNamesAttribute</c> reading and C# tuple-syntax
/// rendering in API signatures. Uses the test assembly itself (whose fixture
/// types below are compiled by Roslyn, the authoritative encoder of the
/// tuple-element-names array) as the subject, so the decoder is validated
/// against real compiler-emitted metadata rather than synthetic input.
/// </summary>
public sealed class TupleTypeViewTests
{
    private static readonly ApiSurface Surface;

    static TupleTypeViewTests()
    {
        var assemblyPath = typeof(TupleTypeViewTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    private static ApiType GetType(string name) =>
        Surface.Types.First(t => t.Name == name);

    private static ApiMember GetMethod(string methodName) =>
        GetType(nameof(TupleSampleClass)).Members.First(m => m.Name == methodName && m.Kind == "method");

    private static ApiMember GetProperty(string propName) =>
        GetType(nameof(TupleSampleClass)).Members.First(m => m.Name == propName && m.Kind == "property");

    private static ApiMember GetField(string fieldName) =>
        GetType(nameof(TupleSampleClass)).Members.First(m => m.Name == fieldName && m.Kind == "field");

    // --- Direct TypeNode unit tests (constructed nodes, no metadata) ---------

    private static GenericTypeNode Tuple(params TypeNode[] args) =>
        new("System.ValueTuple", isReferenceType: false, [.. args]);

    private static PrimitiveTypeNode Int() => new("int", isReferenceType: false);
    private static PrimitiveTypeNode Str() => new("string", isReferenceType: true);

    [Fact]
    public void TwoTuple_Named_RendersElementNames()
    {
        var node = Tuple(Int(), Str());
        node.ApplyTupleNames(["count", "name"]);
        Assert.Equal("(int count, string name)", node.Render());
    }

    [Fact]
    public void TwoTuple_NoNames_RendersPositional()
    {
        var node = Tuple(Int(), Str());
        node.ApplyTupleNames(null);
        Assert.Equal("(int, string)", node.Render());
    }

    [Fact]
    public void TwoTuple_PartialNames_RendersMixed()
    {
        var node = Tuple(Int(), Str());
        node.ApplyTupleNames([null, "name"]);
        Assert.Equal("(int, string name)", node.Render());
    }

    [Fact]
    public void OneArityValueTuple_StaysGeneric()
    {
        // C# (T) is parenthesization, not a one-tuple, so ValueTuple<int> keeps
        // its generic spelling and must never render as "(int)".
        var node = Tuple(Int());
        node.ApplyTupleNames(null);
        Assert.Equal("System.ValueTuple<int>", node.Render());
    }

    [Fact]
    public void NestedTuple_NamesFlowBreadthFirst()
    {
        // ((int a, int b) pair, string c): outer element names first, then inner.
        var inner = Tuple(Int(), Int());
        var node = Tuple(inner, Str());
        node.ApplyTupleNames(["pair", "c", "a", "b"]);
        Assert.Equal("((int a, int b) pair, string c)", node.Render());
    }

    [Fact]
    public void TupleInsideGeneric_NamesApply_NoSlotForContainer()
    {
        // List<(int x, int y)>: the container consumes no name slot.
        var tuple = Tuple(Int(), Int());
        var node = new GenericTypeNode("System.Collections.Generic.List", isReferenceType: true, [tuple]);
        node.ApplyTupleNames(["x", "y"]);
        Assert.Equal("System.Collections.Generic.List<(int x, int y)>", node.Render());
    }

    // --- End-to-end: signatures extracted from Roslyn-compiled fixtures ------

    [Fact]
    public void Method_NamedTupleReturn()
    {
        var member = GetMethod(nameof(TupleSampleClass.NamedReturn));
        Assert.StartsWith("(int count, string name) ", member.Signature);
    }

    [Fact]
    public void Method_NamedTupleParam()
    {
        var member = GetMethod(nameof(TupleSampleClass.NamedParam));
        Assert.Contains("(int x, int y) point", member.Signature);
    }

    [Fact]
    public void Method_UnnamedTuple_RendersPositional()
    {
        // No TupleElementNamesAttribute is emitted; still spelled with (...) syntax.
        var member = GetMethod(nameof(TupleSampleClass.UnnamedReturn));
        Assert.StartsWith("(int, string) ", member.Signature);
        Assert.DoesNotContain("ValueTuple", member.Signature);
    }

    [Fact]
    public void Method_PartialNames()
    {
        var member = GetMethod(nameof(TupleSampleClass.PartialNames));
        Assert.StartsWith("(int, string second) ", member.Signature);
    }

    [Fact]
    public void Method_NestedTuple()
    {
        var member = GetMethod(nameof(TupleSampleClass.NestedTuple));
        Assert.StartsWith("((int a, int b) pair, string c) ", member.Signature);
    }

    [Fact]
    public void Method_MidNestedTuple()
    {
        var member = GetMethod(nameof(TupleSampleClass.MidNested));
        Assert.StartsWith("(int a, (int b, int c) inner, int d) ", member.Signature);
    }

    [Fact]
    public void Method_TupleInsideFunc()
    {
        var member = GetMethod(nameof(TupleSampleClass.TupleInsideFunc));
        Assert.Contains("Func<(int fa, string fb)>", member.Signature);
    }

    [Fact]
    public void Method_TupleArray()
    {
        var member = GetMethod(nameof(TupleSampleClass.TupleArray));
        Assert.StartsWith("(int ea, string eb)[] ", member.Signature);
    }

    [Fact]
    public void Method_DictionaryTupleKey()
    {
        var member = GetMethod(nameof(TupleSampleClass.DictionaryTupleKey));
        Assert.Contains("Dictionary<(int ka, int kb), string>", member.Signature);
    }

    [Fact]
    public void Method_RefTupleParam()
    {
        var member = GetMethod(nameof(TupleSampleClass.RefTuple));
        Assert.Contains("ref (int a, int b) value", member.Signature);
    }

    [Fact]
    public void Method_BigTuple_NineElements_NamesAlign()
    {
        // A 9-tuple lowers to ValueTuple<T1..T7, ValueTuple<T8,T9>>; the flat name
        // stream is [a1..a9, null, null]. Names must land on the flattened
        // elements and the trailing null padding must be skipped, not misapplied.
        var member = GetMethod(nameof(TupleSampleClass.BigTuple));
        Assert.StartsWith(
            "(int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9) ",
            member.Signature);
    }

    [Fact]
    public void Field_NamedTuple()
    {
        var member = GetField(nameof(TupleSampleClass.NamedTupleField));
        Assert.Equal("(int id, string label)", member.ReturnType);
    }

    [Fact]
    public void Property_NamedTuple()
    {
        var member = GetProperty(nameof(TupleSampleClass.NamedTupleProp));
        Assert.Contains("(int width, int height)", member.Signature);
    }

    [Fact]
    public void Event_NamedTupleHandler()
    {
        var member = GetType(nameof(TupleSampleClass)).Members
            .First(m => m.Name == nameof(TupleSampleClass.TupleEvent) && m.Kind == "event");
        Assert.Contains("Action<(int code, string message)>", member.Signature);
        Assert.DoesNotContain("ValueTuple", member.Signature);
    }

    // --- Negative controls: non-tuple members are unaffected -----------------

    [Fact]
    public void Method_NoTuple_Unaffected()
    {
        var member = GetMethod(nameof(TupleSampleClass.NoTuple));
        Assert.StartsWith("int ", member.Signature);
        Assert.DoesNotContain("(int", member.Signature);
    }

    [Fact]
    public void Method_GenericNonTuple_Unaffected()
    {
        var member = GetMethod(nameof(TupleSampleClass.GenericNonTuple));
        Assert.Contains("Dictionary<string, int>", member.Signature);
        Assert.DoesNotContain("(int", member.Signature);
    }
}

// ===== Test fixture types with a spread of tuple shapes =====

/// <summary>Sample class exercising TupleElementNamesAttribute decoding.</summary>
public class TupleSampleClass
{
    public (int id, string label) NamedTupleField = default;

    public (int width, int height) NamedTupleProp { get; set; }

    public (int count, string name) NamedReturn() => default;
    public void NamedParam((int x, int y) point) { }
    public (int, string) UnnamedReturn() => default;
    public (int, string second) PartialNames() => default;
    public ((int a, int b) pair, string c) NestedTuple() => default;
    public (int a, (int b, int c) inner, int d) MidNested() => default;
    public Func<(int fa, string fb)> TupleInsideFunc() => default!;
    public (int ea, string eb)[] TupleArray() => default!;
    public Dictionary<(int ka, int kb), string> DictionaryTupleKey() => default!;
    public void RefTuple(ref (int a, int b) value) { value = default; }

    public (int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9) BigTuple() => default;

    public int NoTuple() => 0;
    public Dictionary<string, int> GenericNonTuple() => default!;

    public event Action<(int code, string message)> TupleEvent = null!;

    // --- Identity/canonical-spelling fixtures --------------------------------
    public void NamedParamAlt((int a, int b) q) { }
    public string NonNullRef(string s) => s;
    public string? NullRef(string? s) => s;
    private int _slot;
    public ref readonly int RefReadonlyReturn() => ref _slot;
}
