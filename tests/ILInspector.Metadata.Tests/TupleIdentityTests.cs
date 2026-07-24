using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

#nullable enable

/// <summary>
/// Canary tests for the tuple / member-identity separation: the C# tuple
/// <em>display</em> spelling (<c>(int count, string name)</c>) must never leak
/// into the <em>identity</em> spelling used by the Member Index digest,
/// XML-doc lookup, and extension/instance correspondence, which must remain the
/// presentation-independent <c>System.ValueTuple&lt;...&gt;</c> form with element
/// names erased. Other facets (nullability, <c>ref readonly</c>) must survive
/// canonicalization unchanged.
/// </summary>
public sealed class TupleIdentityTests
{
    private static readonly ApiSurface Surface;

    static TupleIdentityTests()
    {
        var assemblyPath = typeof(TupleIdentityTests).Assembly.Location;
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        Surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
    }

    private static ApiType SampleType =>
        Surface.Types.First(t => t.Name == nameof(TupleSampleClass));

    private static ApiMember Method(string name) =>
        SampleType.Members.First(m => m.Name == name && m.Kind == "method");

    private static string Canonical(string methodName) =>
        ApiMemberIdentity.GetCanonicalSignature(SampleType, Method(methodName));

    // --- TypeNode.RenderCanonical: structural, name-insensitive spelling -----

    private static GenericTypeNode Tuple(params TypeNode[] args) =>
        new("System.ValueTuple", isReferenceType: false, [.. args]);

    private static PrimitiveTypeNode Int() => new("int", isReferenceType: false);
    private static PrimitiveTypeNode Str() => new("string", isReferenceType: true);

    [Fact]
    public void RenderCanonical_NamedTuple_DropsSyntaxAndNames()
    {
        var node = Tuple(Int(), Str());
        node.ApplyTupleNames(["count", "name"]);
        Assert.Equal("System.ValueTuple<int, string>", node.RenderCanonical());
    }

    [Fact]
    public void RenderCanonical_IsElementNameInsensitive()
    {
        var named = Tuple(Int(), Str());
        named.ApplyTupleNames(["count", "name"]);
        var renamed = Tuple(Int(), Str());
        renamed.ApplyTupleNames(["total", "label"]);
        var unnamed = Tuple(Int(), Str());
        unnamed.ApplyTupleNames(null);

        Assert.Equal(named.RenderCanonical(), renamed.RenderCanonical());
        Assert.Equal(named.RenderCanonical(), unnamed.RenderCanonical());
    }

    [Fact]
    public void RenderCanonical_PreservesNullability()
    {
        // A canonical spelling erases tuple presentation but nothing else: the
        // NRT annotation that distinguishes string from string? must survive.
        var node = Str();
        int pos = 0;
        node.ApplyNullability([2], ref pos, 0);
        Assert.Equal("string?", node.Render());
        Assert.Equal("string?", node.RenderCanonical());
    }

    [Fact]
    public void RenderCanonical_NonTuple_EqualsDisplay()
    {
        var node = new GenericTypeNode(
            "System.Collections.Generic.List", isReferenceType: true, [Int()]);
        Assert.Equal(node.Render(), node.RenderCanonical());
    }

    [Fact]
    public void RenderCanonical_NestedTuple_FullyStructural()
    {
        var inner = Tuple(Int(), Int());
        var node = Tuple(inner, Str());
        node.ApplyTupleNames(["pair", "c", "a", "b"]);
        Assert.Equal(
            "System.ValueTuple<System.ValueTuple<int, int>, string>",
            node.RenderCanonical());
    }

    // --- Member Index digest (GetCanonicalSignature) -------------------------

    [Fact]
    public void Canonical_TupleReturn_DoesNotCrash()
    {
        // A tuple return puts '(' at signature position 0; the identity parsers
        // must not assume the first '(' opens the parameter list.
        var canonical = Canonical(nameof(TupleSampleClass.NamedReturn));
        Assert.Contains("NamedReturn", canonical);
        Assert.DoesNotContain("count", canonical);
    }

    [Fact]
    public void Canonical_TupleParam_ErasesNamesToValueTuple()
    {
        var canonical = Canonical(nameof(TupleSampleClass.NamedParam));
        Assert.Contains("System.ValueTuple<int,int>", canonical);
        Assert.DoesNotContain("point", canonical);
        Assert.DoesNotContain("(int x", canonical);
    }

    [Fact]
    public void Canonical_TupleParam_IsElementNameInsensitive()
    {
        // NamedParam((int x, int y)) and NamedParamAlt((int a, int b)) differ only
        // in tuple element names; their canonical parameter lists must be identical.
        var a = Canonical(nameof(TupleSampleClass.NamedParam));
        var b = Canonical(nameof(TupleSampleClass.NamedParamAlt));
        Assert.EndsWith("(System.ValueTuple<int,int>)", a);
        Assert.EndsWith("(System.ValueTuple<int,int>)", b);
    }

    [Fact]
    public void Canonical_PreservesNullability_DistinctDigests()
    {
        // string vs string? is a real API difference and must remain a distinct
        // Member Index identity: canonicalization erases tuples, not nullability.
        var nonNull = Canonical(nameof(TupleSampleClass.NonNullRef));
        var nullable = Canonical(nameof(TupleSampleClass.NullRef));
        Assert.NotEqual(nonNull, nullable);
    }

    // --- Canonical return spelling preserves ref readonly --------------------

    [Fact]
    public void CanonicalReturn_PreservesRefReadonly()
    {
        var member = Method(nameof(TupleSampleClass.RefReadonlyReturn));
        Assert.Equal("ref readonly int", member.SignatureModel!.ReturnType);
        Assert.Equal("ref readonly int", member.SignatureModel!.EffectiveCanonicalReturnType);
    }
}
