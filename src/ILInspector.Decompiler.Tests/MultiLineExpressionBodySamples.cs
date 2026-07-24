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
}
