using System.Text;

namespace ILInspector.Decompiler.Tests;

// #3084 witness: a single-`return` method whose one expression is wide enough to
// wrap (the always-on fluent-chain wrapper breaks it one call per line). The
// member layout must render the wrapped single expression as an
// expression-bodied member (the arrow trails the signature line and the
// expression's receiver follows it, continuations one level deeper), not a brace
// block wrapping a lone `return`.
public static class MultiLineExpressionBodySamples
{
    public static string Pipeline(StringBuilder builder)
        => builder
            .Append("alphabet")
            .Append("bravissimo")
            .Append("charlateral")
            .Append("deltatango")
            .Append("echolocation")
            .Append("foxtrotter")
            .ToString();

    // #3084 (this slice) witness: a void method whose one statement is an
    // expression statement (not a `return`) wide enough to wrap. The member
    // layout must fold it to an expression-bodied member too — the whole first
    // line trails the arrow with the chained calls one level deeper — even though
    // there is no `return` keyword to strip.
    public static void Drain(StringBuilder builder)
        => builder
            .Append("alphabet")
            .Append("bravissimo")
            .Append("charlateral")
            .Append("deltatango")
            .Append("echolocation")
            .Append("foxtrotter")
            .Clear();
}
