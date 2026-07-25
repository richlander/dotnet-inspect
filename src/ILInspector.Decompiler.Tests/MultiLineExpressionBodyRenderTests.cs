using System.Reflection.PortableExecutable;

using ILInspector.Decompiler;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

// #3084: a single-`return` member whose one expression wraps across lines renders
// as an expression-bodied member rather than a brace block wrapping a lone
// `return`. Block body and expression body are IL-identical; the oracle
// (dotnet/runtime source and its .editorconfig) prefers the expression-bodied
// form, and the arrow trails the signature line with the expression's opening
// token after it — the same shape #3125 shipped for switch returns and the
// natural multi-line extension of the single-line `head => expr;` default.
[Trait("Area", "RoundTrip")]
public sealed class MultiLineExpressionBodyRenderTests
{
    static string AssemblyPath => typeof(MultiLineExpressionBodyRenderTests).Assembly.Location;

    static ApiType Specimen()
    {
        using var pe = new PEReader(File.OpenRead(AssemblyPath));
        var api = ApiSurfaceExtractor.Extract(pe);
        return Assert.Single(api.Types, t => t.FullName == typeof(MultiLineExpressionBodySamples).FullName);
    }

    [Fact]
    public void WrappedSingleReturn_RendersStyleBExpressionBody()
    {
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == nameof(MultiLineExpressionBodySamples.Pipeline));

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Equal(
            "    public static string Pipeline(StringBuilder builder) => builder\n" +
            "        .Append(\"alphabet\")\n" +
            "        .Append(\"bravissimo\")\n" +
            "        .Append(\"charlateral\")\n" +
            "        .Append(\"deltatango\")\n" +
            "        .Append(\"echolocation\")\n" +
            "        .Append(\"foxtrotter\")\n" +
            "        .ToString();",
            rendered.Text!.Replace("\r\n", "\n"));
    }

    [Fact]
    public void WrappedSingleExpressionStatement_RendersStyleBExpressionBody()
    {
        // #3084 (this slice): a void member whose one statement is a wide
        // expression statement (no `return`) folds to an expression-bodied member
        // too — the whole first line trails the arrow, chained calls one level
        // deeper. Block body and expression body are IL-identical.
        var type = Specimen();
        var member = Assert.Single(type.Members, m => m.Name == nameof(MultiLineExpressionBodySamples.Drain));

        var rendered = MemberBodyProducer.ProduceMember(type, member, AssemblyPath, pdbPath: null);

        Assert.Equal(MemberBodyProductionStatus.Complete, rendered.Status);
        Assert.Equal(
            "    public static void Drain(StringBuilder builder) => builder\n" +
            "        .Append(\"alphabet\")\n" +
            "        .Append(\"bravissimo\")\n" +
            "        .Append(\"charlateral\")\n" +
            "        .Append(\"deltatango\")\n" +
            "        .Append(\"echolocation\")\n" +
            "        .Append(\"foxtrotter\")\n" +
            "        .Clear();",
            rendered.Text!.Replace("\r\n", "\n"));
    }
}
