using System.Collections.Immutable;

using ILInspector.CSharp;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Harness-owned span attribution for ReturnToSender recompile failures.
///
/// The substitution experiment in <see cref="ReturnToSender.TryIsolateRecompileFailure"/>
/// can only credit a body defect when the authored body compiles in the failing
/// row's shell. When the shell is also broken the experiment goes blind and the
/// row is filed as <c>ShellOrClosureDefect</c>, masking any decompiler body
/// defect that co-occurs with a broken shell.
///
/// This helper recovers the additional signal without a further compile: it
/// takes the target body's product-issued range in both the decompiled and the
/// authored artifacts (which the isolation pass already compiled) and compares
/// where each compiler error lands. The attribution is intentionally
/// conservative so the resulting count stays a sound lower bound — it never
/// converts a shell fault into a body defect (see
/// <see cref="DecompiledBodyIsolatedUnderBrokenShell"/>).
///
/// Attribution is a harness measurement concern; it reads compiler diagnostics
/// (an independent oracle) and the source the pipeline already produced. It does
/// not construct or rewrite the C# artifact, and it does not rediscover the body:
/// the decompiled body range and the authored replacement range are both issued
/// by the product's frozen <see cref="CSharpSourceArtifact"/>.
/// </summary>
internal static class SpanAttribution
{
    // Error codes that are provably intrinsic to the decompiled body itself and
    // cannot be induced by a broken or incomplete reconstructed shell: they
    // concern only the body's own local declarations, never a shell-provided
    // member, type, or reference. Resolution errors
    // (CS0103/CS0246/CS1061/CS0234/CS1069/...) and conversion/overload/shape
    // errors are excluded because a shell-reconstruction miss produces them
    // identically to a genuine decompiler body defect.
    //
    // CS0165 (use of unassigned local) is deliberately NOT included: definite
    // assignment can hinge on a compile-time const whose value the shell
    // reconstructor may fail to preserve (e.g. emitting a mutable field instead
    // of `const`), so a shell miss can induce an in-body CS0165 with a clean
    // authored body. That would break the lower-bound guarantee (PR #3231
    // adversarial review). CS0128 has no such dependency: it requires two local
    // declarations sharing a name inside the body, which no shell state can
    // create.
    // Pinned by AuthoredCorpusMethodology.Version via
    // SpanAttributionTests.BodyIntrinsicAllowlist_IsPinnedToCurrentMethodologyVersion.
    // Adding an ID here without bumping AuthoredCorpusMethodology.Version fails that
    // gate: the allow list defines the invalid-attribution lineage carried by the
    // global methodology stamp, so a silent change would make rows sharing a lineage
    // incomparable.
    internal static readonly ImmutableHashSet<string> BodyIntrinsicSemanticErrorIds =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "CS0128"); // duplicate local variable name (strictly body-internal)

    /// <summary>
    /// Returns the decompiled in-body diagnostic that soundly attributes the
    /// recompile failure to the decompiler, or null when no sound attribution is
    /// possible (the caller then keeps <c>ShellOrClosureDefect</c>).
    ///
    /// Soundness rule (default-deny). The authored body must be error-free within
    /// its own range (the substitution control), and the decompiled body must
    /// carry at least one in-body error that is provably <em>shell-independent</em>:
    /// either a syntax/parser error — the decompiler emitted body text that does
    /// not parse, which no shell state can cause — or a body-intrinsic semantic
    /// error over the body's own local declarations (see
    /// <see cref="BodyIntrinsicSemanticErrorIds"/>). Context-dependent errors
    /// (unresolved names/types/members, conversions, overloads) are never credited
    /// because a broken shell reconstructor produces them identically to a real
    /// body defect.
    /// </summary>
    internal static Diagnostic? IsolatingBodyError(
        string decompiledSource,
        CSharpSourceRange decompiledBody,
        ImmutableArray<Diagnostic> decompiledDiagnostics,
        CSharpSourceRange authoredBody,
        ImmutableArray<Diagnostic> authoredDiagnostics,
        CSharpParseOptions? parseOptions = null)
    {
        var decompiledSpan = ToTextSpan(decompiledBody);
        var authoredSpan = ToTextSpan(authoredBody);

        // Substitution control: the authored body must be clean within its own
        // range. If a broken shell also breaks the authored body we decline
        // (a conservative false negative that keeps the count a lower bound).
        if (CountErrorsInSpan(authoredDiagnostics, authoredSpan) != 0)
            return null;

        // Shell-independent syntax error: the decompiled body text does not parse.
        if (FirstSyntaxErrorInSpan(decompiledSource, decompiledSpan, parseOptions) is { } syntaxError)
            return syntaxError;

        // Shell-independent body-intrinsic semantic error (locals/control flow).
        foreach (var diagnostic in decompiledDiagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;
            if (!BodyIntrinsicSemanticErrorIds.Contains(diagnostic.Id))
                continue;
            var location = diagnostic.Location;
            if (location.Kind != LocationKind.SourceFile)
                continue;
            if (decompiledSpan.IntersectsWith(location.SourceSpan))
                return diagnostic;
        }

        return null;
    }

    static TextSpan ToTextSpan(CSharpSourceRange range) => new(range.Start, range.Length);

    /// <summary>
    /// Returns the first Error-severity <em>syntactic</em> diagnostic whose span
    /// intersects <paramref name="bodySpan"/>, or null. Uses
    /// <see cref="SyntaxTree.GetDiagnostics()"/>, which reports only parser/lexer
    /// diagnostics, so a hit proves the decompiled body text is unparseable
    /// regardless of any shell state.
    ///
    /// <paramref name="parseOptions"/> must match the options the pipeline
    /// compiled with. Language-version gating is a binding diagnostic rather than
    /// a parser one today, but re-parsing under different options than the
    /// compile is a latent source of phantom syntax errors, which would inflate
    /// the metric.
    /// </summary>
    static Diagnostic? FirstSyntaxErrorInSpan(string source, TextSpan bodySpan, CSharpParseOptions? parseOptions)
    {
        SyntaxTree tree;
        try
        {
            tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var diagnostic in tree.GetDiagnostics())
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;
            var location = diagnostic.Location;
            if (location.Kind != LocationKind.SourceFile)
                continue;
            if (bodySpan.IntersectsWith(location.SourceSpan))
                return diagnostic;
        }

        return null;
    }

    static int CountErrorsInSpan(ImmutableArray<Diagnostic> diagnostics, TextSpan bodySpan)
    {
        int count = 0;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity != DiagnosticSeverity.Error)
                continue;
            var location = diagnostic.Location;
            if (location.Kind != LocationKind.SourceFile)
                continue;
            if (bodySpan.IntersectsWith(location.SourceSpan))
                count++;
        }

        return count;
    }
}
