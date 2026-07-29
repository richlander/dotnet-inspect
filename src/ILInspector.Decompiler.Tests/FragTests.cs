using System.Reflection;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates on <see cref="Frag"/>, the carrier that lets the printer report where a
/// node's characters landed instead of searching for them afterwards.
/// </summary>
public class FragTests
{
    static LoadLocal Local(int index) => new(index, TypeRef.CoreLib("System", "Int32"));

    static Frag Printed(IrNode node, string text) => new Frag(text).Attribute(node);

    [Fact]
    public void InterpolationRecordsWhereTheChildLanded()
    {
        var node = Local(0);
        Frag child = Printed(node, "value");

        Frag composed = Frag.Of($"({child})");

        Assert.Equal("(value)", composed.Text);
        var span = Assert.Single(composed.Spans!);
        Assert.Same(node, span.Node);
        Assert.Equal(1, span.Start);
        Assert.Equal(5, span.Length);
        Assert.Equal("value", composed.Text.Substring(span.Start, span.Length));
    }

    /// <summary>
    /// The case the search-based approach cannot resolve: two operands that print
    /// identically. Searching finds the first spelling twice (or refuses); the
    /// composer knows it wrote them at two different offsets.
    /// </summary>
    [Fact]
    public void IdenticalSpellingsGetDistinctPositions()
    {
        var left = Local(0);
        var right = Local(1);
        Frag a = Printed(left, "count");
        Frag b = Printed(right, "count");

        Frag composed = Frag.Of($"{a} + {b}");

        Assert.Equal("count + count", composed.Text);
        Assert.Equal(2, composed.Spans!.Count);

        var first = composed.Spans!.Single(s => ReferenceEquals(s.Node, left));
        var second = composed.Spans!.Single(s => ReferenceEquals(s.Node, right));
        Assert.Equal(0, first.Start);
        Assert.Equal(8, second.Start);
        Assert.NotEqual(first.Start, second.Start);
    }

    [Fact]
    public void NestedCompositionReportsAbsoluteOffsets()
    {
        var inner = Local(0);
        var outer = Local(1);

        Frag innerFrag = Frag.Of($"new {Printed(inner, "Widget")}()");
        Frag composed = Frag.Of($"sink.Add({innerFrag.Attribute(outer)});");

        Assert.Equal("sink.Add(new Widget());", composed.Text);

        var innerSpan = composed.Spans!.Single(s => ReferenceEquals(s.Node, inner));
        var outerSpan = composed.Spans!.Single(s => ReferenceEquals(s.Node, outer));

        Assert.Equal("Widget", composed.Text.Substring(innerSpan.Start, innerSpan.Length));
        Assert.Equal("new Widget()", composed.Text.Substring(outerSpan.Start, outerSpan.Length));
        Assert.True(innerSpan.Start > outerSpan.Start, "the child must sit inside the parent window");
    }

    [Fact]
    public void PlainTextCarriesNoPositions()
    {
        Frag fragment = new("literal");

        Assert.Equal("literal", fragment.Text);
        Assert.Null(fragment.Spans);
    }

    [Fact]
    public void EmptyTextRecordsNothing()
    {
        Frag fragment = new Frag("").Attribute(Local(0));

        Assert.Null(fragment.Spans);
    }

    /// <summary>
    /// An interpolated string is converted by a handler only when the target is a
    /// parameter of the handler type. If a <c>string</c>-to-<see cref="Frag"/>
    /// conversion existed, <c>Frag M() =&gt; $"..."</c> would bind through
    /// <c>string</c> and compile happily while dropping every interior position --
    /// correct text, no positions, no diagnostic. Its absence is what makes an
    /// unconverted composer a build error instead of a silent reversion to
    /// searching. This gate fails if someone re-adds it for convenience.
    /// </summary>
    [Fact]
    public void NoImplicitConversionFromString()
    {
        var conversions = typeof(Frag)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Where(m => m.GetParameters() is [{ ParameterType.FullName: "System.String" }])
            .ToList();

        Assert.Empty(conversions);
    }
}
