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
    }

    [Fact]
    public void ReturnsNullWhenDiagnosticHasNoInSourceLocation()
        => Assert.Null(FidelityCheck.RenderAnnotatedFailure("class C {}", Diagnostic.Create(
            new DiagnosticDescriptor("XX000", "t", "no location", "c", DiagnosticSeverity.Error, true),
            Location.None)));
}
