using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the annotated compile-back failure render (#3238): a RecompileFail's
/// emitted C# is shown with a caret underline under the exact diagnostic span,
/// and invisible/format runes on that line are revealed so the caret points at
/// something the reader can see. The caret sits on a <c>//</c> comment so the
/// block stays valid C# inside a code fence.
/// </summary>
[Trait("Area", "Fidelity")]
public class AnnotatedCompileBackFailureTests
{
    // The "killer" shape: an identifier carrying a zero-width non-joiner
    // (U+200C). Roslyn cannot bind `Missing<ZWNJ>Type`, so the CS0246 span
    // covers the whole identifier — including the invisible rune.
    const char Zwnj = '\u200C';

    static Diagnostic FirstError(string source)
    {
        var comp = CSharpCompilation.Create("annot",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        return comp.Emit(ms).Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void RevealsInvisibleRuneAndAlignsCaretUnderTheSpan()
    {
        string source =
            "class Seq\n" +
            "{\n" +
            "    void M() { Missing" + Zwnj + "Type x = null; }\n" +
            "}\n";

        string render = FidelityCheck.RenderAnnotatedFailure(source, FirstError(source))!;
        string[] lines = render.Split('\n');

        // The code line reveals the ZWNJ as a visible token.
        Assert.Contains("Missing\u2039ZWNJ\u203AType", lines[0]);
        Assert.DoesNotContain('\u200C', render);

        // The caret line is a // comment carrying the diagnostic, with carets
        // aligned under the revealed identifier (7 + 6 for <ZWNJ> + 4 = 17).
        Assert.StartsWith("    //", lines[1]);
        Assert.Contains(new string('^', 17), lines[1]);
        Assert.Contains("CS0246", lines[1]);

        int caretColumn = lines[1].IndexOf('^');
        int revealedIdentColumn = lines[0].IndexOf("Missing", StringComparison.Ordinal);
        Assert.Equal(revealedIdentColumn, caretColumn);

        // Layer 3 (#3256): a classified cause + paired fix follow on the gutter,
        // deriving the fix from the emitted token itself (reveal → stripped).
        Assert.StartsWith("    //  cause: invisible-rune", lines[2]);
        Assert.StartsWith("    //  fix:", lines[3]);
        Assert.Contains("Missing\u2039ZWNJ\u203AType \u2192 MissingType", lines[3]);
    }

    [Theory]
    [InlineData("M\u200C", "invisible-rune", "M\u2039ZWNJ\u203A \u2192 M")]      // Cf stripping
    [InlineData("\u200DName", "invisible-rune", "\u2039ZWJ\u203AName \u2192 Name")]
    public void ClassifiesInvisibleRuneWithStrippedFix(string token, string cause, string fixDelta)
    {
        var result = FidelityCheck.ClassifyCause(token);
        Assert.NotNull(result);
        Assert.StartsWith(cause, result!.Value.Cause);
        Assert.Contains(fixDelta, result.Value.Fix);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("int")]
    [InlineData("return")]
    public void ClassifiesBareKeywordWithVerbatimFix(string keyword)
    {
        var result = FidelityCheck.ClassifyCause(keyword);
        Assert.NotNull(result);
        Assert.StartsWith("keyword-escape", result!.Value.Cause);
        Assert.Equal($"emit  {keyword} \u2192 @{keyword}", result.Value.Fix);
    }

    [Theory]
    [InlineData("<>c")]
    [InlineData("<M>b__0")]
    public void ClassifiesUnspeakableNameWithPolicyFix(string token)
    {
        var result = FidelityCheck.ClassifyCause(token);
        Assert.NotNull(result);
        Assert.StartsWith("unspeakable-name", result!.Value.Cause);
        Assert.Contains("no exact source form", result.Value.Fix);
    }

    [Theory]
    [InlineData("Ordinary")]     // plain identifier — nothing to classify
    [InlineData("MyType")]
    [InlineData("")]
    [InlineData("@class")]        // already escaped — not a bare keyword
    public void FallsThroughForUnrecognizedToken(string token)
        => Assert.Null(FidelityCheck.ClassifyCause(token));

    // invisible-rune is checked before keyword-escape: a keyword-looking token
    // carrying a format rune is classified by the rune, since that is the real
    // reason binding diverged.
    [Fact]
    public void InvisibleRuneTakesPrecedenceOverKeyword()
    {
        var result = FidelityCheck.ClassifyCause("class\u200C");
        Assert.NotNull(result);
        Assert.StartsWith("invisible-rune", result!.Value.Cause);
    }

    [Fact]
    public void ReturnsNullWhenDiagnosticHasNoInSourceLocation()
        => Assert.Null(FidelityCheck.RenderAnnotatedFailure("class C {}", Diagnostic.Create(
            new DiagnosticDescriptor("XX000", "t", "no location", "c", DiagnosticSeverity.Error, true),
            Location.None)));
}
