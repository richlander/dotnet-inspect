using System.Collections.Immutable;

using ILInspector.DecompilerHarness;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the annotated compile-back failure render (#3238) and its cause/fix
/// classification (#3256). Layers 1-2: the emitted C# is shown with a caret under
/// the exact diagnostic span, invisible runes revealed. Layer 3: a classified
/// <c>cause</c> + paired <c>fix</c>. The concrete-fix causes are held to their
/// claim - apply the fix, recompile, and the diagnostic must be gone - so a
/// green suite verifies the <c>fix:</c> line, not merely its wording.
/// </summary>
[Trait("Area", "Fidelity")]
[Collection(FidelityGateCollection.Name)]
public class AnnotatedCompileBackFailureTests
{
    const char Zwnj = '\u200C';

    // The one dynamic compilation site in this file (registered in the
    // DynamicCompilationSite manifest); every test routes through it.
    static ImmutableArray<Diagnostic> Compile(string source)
    {
        var comp = CSharpCompilation.Create("annot",
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        return comp.Emit(ms).Diagnostics;
    }

    static Diagnostic FirstError(string source)
        => Compile(source).First(d => d.Severity == DiagnosticSeverity.Error);

    static bool CompilesClean(string source)
        => !Compile(source).Any(d => d.Severity == DiagnosticSeverity.Error);

    // Classify a real diagnostic the way the render site does: widen to the
    // diagnostic's failing line + span, then hand it to the classifier.
    static FidelityCheck.CauseFix? Classify(string source, Diagnostic d)
    {
        var span = d.Location.GetLineSpan().Span;
        string line = source.Replace("\r\n", "\n").Split('\n')[span.Start.Line];
        int start = span.Start.Character;
        int end = span.End.Line == span.Start.Line ? span.End.Character : line.Length;
        return FidelityCheck.ClassifyCause(line, start, end, d.Id);
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

        // Layers 1-2: the code line reveals the ZWNJ; the caret is a // comment
        // carrying the diagnostic, aligned under the revealed identifier.
        Assert.Contains("Missing\u2039ZWNJ\u203AType", lines[0]);
        Assert.DoesNotContain('\u200C', render);
        Assert.StartsWith("    //", lines[1]);
        Assert.Contains(new string('^', 17), lines[1]);
        Assert.Contains("CS0246", lines[1]);

        int caretColumn = lines[1].IndexOf('^');
        int revealedIdentColumn = lines[0].IndexOf("Missing", StringComparison.Ordinal);
        Assert.Equal(revealedIdentColumn, caretColumn);

        // Layer 3: an interior Cf that lexed but failed to *bind* is unspeakable -
        // Roslyn strips Cf from metadata names, so the name came from a non-C#
        // producer and no source spelling reaches it. Policy fix, no A -> B delta.
        Assert.StartsWith("    //  cause: unspeakable-name", lines[2]);
        Assert.Contains("no exact source form", lines[3]);
    }

    [Fact]
    public void ReturnsNullWhenDiagnosticHasNoInSourceLocation()
        => Assert.Null(FidelityCheck.RenderAnnotatedFailure("class C {}", Diagnostic.Create(
            new DiagnosticDescriptor("XX000", "t", "no location", "c", DiagnosticSeverity.Error, true),
            Location.None)));

    // ---- Concrete fixes, held to their claim (apply -> recompile -> clean) ----

    [Theory]
    // ordinary stem — dropping the rune is sufficient
    [InlineData("class \u200CFoo { }\n")]
    // keyword stem — dropping alone would expose the bare keyword 'class', which
    // is *worse* than the original lex error, so the fix must also escape it
    [InlineData("class \u200Cclass { }\n")]
    public void InvisibleRuneFixIsVerifiedByRecompile(string source)
    {
        // A leading Cf is a valid identifier-part but not an identifier-start, so
        // it breaks *lexing* (CS1001). Dropping it is a real fix.
        Diagnostic err = FirstError(source);
        Assert.Contains(err.Id, new[] { "CS1001", "CS1056" });

        FidelityCheck.CauseFix? cf = Classify(source, err);
        Assert.NotNull(cf);
        Assert.StartsWith("invisible-rune", cf!.Value.Cause);
        Assert.NotNull(cf.Value.From);
        Assert.NotNull(cf.Value.To);

        // The proposal must never be a bare reserved keyword. IsValidIdentifier is
        // a character-rules check and returns true for 'class', so it cannot serve
        // as the keyword filter.
        Assert.False(
            SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(cf.Value.To!)),
            $"fix proposed a bare keyword: '{cf.Value.To}'");

        string patched = source.Replace(cf.Value.From!, cf.Value.To!);
        Assert.DoesNotContain(Zwnj, patched);
        Assert.True(CompilesClean(patched), "applying the invisible-rune fix must clear the diagnostic");
    }

    [Fact]
    public void KeywordEscapeFixIsVerifiedByRecompile()
    {
        // 'event' written bare is a keyword; '@event' is the fix.
        string source = "class C { int event; }\n";
        Diagnostic err = FirstError(source);

        FidelityCheck.CauseFix? cf = Classify(source, err);
        Assert.NotNull(cf);
        Assert.StartsWith("keyword-escape", cf!.Value.Cause);
        Assert.Equal("event", cf.Value.From);
        Assert.Equal("@event", cf.Value.To);

        string patched = source.Replace(cf.Value.From!, cf.Value.To!);
        Assert.True(CompilesClean(patched), "applying the keyword-escape fix must clear the diagnostic");
    }

    // ---- Interior Cf -> unspeakable (not a droppable rune) ----

    [Fact]
    public void InteriorCfThatFailsToBindIsUnspeakableNotDroppable()
    {
        // Interior Cf on a name the compiler cannot resolve is CS0246 (binding).
        // The honest classification is unspeakable - dropping the rune would be
        // inert, since the compiler already treats the names as equal.
        string source = "class C { Missing" + Zwnj + "Type f; }\n";
        FidelityCheck.CauseFix? cf = Classify(source, FirstError(source));

        Assert.NotNull(cf);
        Assert.StartsWith("unspeakable-name", cf!.Value.Cause);
        Assert.Null(cf.Value.From);   // policy fix - no concrete delta claimed
        Assert.Null(cf.Value.To);
        Assert.Contains("no exact source form", cf.Value.Fix);
    }

    // ---- The former harmful precedence case, now correct ----

    [Fact]
    public void LeadingCfOverAKeywordStemProposesTheEscapedForm()
    {
        // Stripping the rune from '\u200Cclass' leaves 'class'. SyntaxFacts
        // .IsValidIdentifier("class") is true - it checks character rules only and
        // knows nothing about keywords - so the proposal must be composed from both
        // causes: drop the rune *and* escape the keyword it exposes.
        string source = "class " + Zwnj + "class { }\n";

        FidelityCheck.CauseFix? cf = Classify(source, FirstError(source));

        Assert.NotNull(cf);
        Assert.Equal(Zwnj + "class", cf!.Value.From);
        Assert.Equal("@class", cf.Value.To);
        Assert.Contains("escape", cf.Value.Fix);
    }

    // ---- Fall-through: not every span is a classifiable identifier ----

    [Theory]
    // generic argument list - the '<'/'>' are not a generated-name marker
    [InlineData("var x = new List<int>();", 12, 21, "CS0246")]
    // a bare '<' span (e.g. CS1526 on `new <>c()`) is not an identifier
    [InlineData("var x = new <>c();", 12, 13, "CS1526")]
    // a lambda arrow is not an identifier
    [InlineData("f = x => x;", 6, 8, "CS0119")]
    // an ordinary identifier with no defect
    [InlineData("Ordinary y = null;", 0, 8, "CS0246")]
    public void FallsThroughForSpansThatAreNotAClassifiableCause(
        string line, int start, int end, string id)
        => Assert.Null(FidelityCheck.ClassifyCause(line, start, end, id));
}
