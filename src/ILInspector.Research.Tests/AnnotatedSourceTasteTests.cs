using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Research.Tests;

/// <summary>
/// The annotated projection renders the same member as the Source view, so it
/// must apply the same taste (#3191) — otherwise one tool shows two spellings of
/// one member. The interleaved IL is what makes this view worth having, so these
/// tests pin the two halves of the contract: a byte-preserving knob changes the
/// C# and leaves the IL untouched, while a byte-divergent style lens (whose
/// render no longer reproduces the member's opcodes) suppresses the IL rather
/// than asserting a correspondence that does not hold.
/// </summary>
public class AnnotatedSourceTasteTests
{
    static string Annotated(string method, PrinterOptions? options)
    {
        using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(AnnotatedTasteFixture).FullName!,
            method,
            AnnotatedSource: true,
            PrinterOptions: options));

        var result = Assert.IsType<DecompilerResult>(projection.AnnotatedSource);
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        return Assert.IsType<string>(result.Output);
    }

    // The interleaved IL lines, in order. Comparing these instead of the whole
    // render is what separates "the C# spelling changed" from "the IL changed".
    static string[] IlLines(string annotated) =>
        [.. annotated
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("// IL_", StringComparison.Ordinal))];

    static string[] CSharpLines(string annotated) =>
        [.. annotated
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal))];

    [Fact]
    public void ByteReservingKnob_ChangesCSharpSpelling_AndLeavesInterleavedIlIdentical()
    {
        string shipped = Annotated(nameof(AnnotatedTasteFixture.Compute), options: null);
        string qualified = Annotated(
            nameof(AnnotatedTasteFixture.Compute),
            new PrinterOptions { QualifyFieldAccess = true, QualifyPropertyAccess = true });

        // The knob reached the annotated projection at all (the #3191 gap).
        Assert.DoesNotContain("this._count", shipped);
        Assert.Contains("this._count", qualified);
        Assert.Contains("this.Extra", qualified);

        // ...and it changed only the spelling: the IL beneath is byte-identical,
        // which is the whole claim the annotated view makes.
        Assert.NotEmpty(IlLines(shipped));
        Assert.Equal(IlLines(shipped), IlLines(qualified));
    }

    [Fact]
    public void NoOptions_RendersIdenticallyToExplicitDefaults()
    {
        Assert.Equal(
            Annotated(nameof(AnnotatedTasteFixture.Compute), options: null),
            Annotated(nameof(AnnotatedTasteFixture.Compute), PrinterOptions.Default));
    }

    [Fact]
    public void ByteDivergentLens_WhenApplied_SuppressesInterleavedIl()
    {
        string lensed = Annotated(
            nameof(AnnotatedTasteFixture.GuardBothVariable),
            new PrinterOptions { PreferConditionalExpressionReturn = true });

        // The lens shaped the render...
        Assert.Contains("return a ? b : c;", lensed);
        // ...so the raw IL is gone, and the reader is told why and by which knob
        // rather than being left with a view that silently lost half its content.
        Assert.Empty(IlLines(lensed));
        Assert.Contains("Interleaved IL suppressed", lensed);
        Assert.Contains("style-lens.prefer-conditional-return", lensed);
        Assert.Contains("Applied Taste", lensed);
    }

    [Fact]
    public void ByteDivergentLens_WhenApplied_KeepsTheSuppressedRenderValidCSharp()
    {
        string lensed = Annotated(
            nameof(AnnotatedTasteFixture.GuardBothVariable),
            new PrinterOptions { PreferConditionalExpressionReturn = true });

        // The note is spelled as comments, so the body is still a C# block: no
        // stray prose leaks into the code the section presents as source.
        Assert.All(
            CSharpLines(lensed).Where(line => line.Length > 0),
            line => Assert.DoesNotContain("suppressed", line, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ByteDivergentLens_WhenRequestedButDeclined_KeepsInterleavedIl()
    {
        // The close negative case: the lens is requested, but this member has no
        // guarded boolean return for it to rewrite, so nothing byte-divergent was
        // applied. Suppression keys on what the render actually did, not on which
        // knobs the host asked for — otherwise merely enabling a lens would strip
        // the IL from every member in the assembly.
        string requested = Annotated(
            nameof(AnnotatedTasteFixture.Compute),
            new PrinterOptions { PreferConditionalExpressionReturn = true });

        Assert.Equal(
            IlLines(Annotated(nameof(AnnotatedTasteFixture.Compute), options: null)),
            IlLines(requested));
        Assert.DoesNotContain("Interleaved IL suppressed", requested);
    }

    [Fact]
    public void ByteDivergentLens_WhenApplied_StillReportsTheLensDecision()
    {
        using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
        var projection = ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
            source,
            typeof(AnnotatedTasteFixture).FullName!,
            nameof(AnnotatedTasteFixture.GuardBothVariable),
            AnnotatedSource: true,
            PrinterOptions: new PrinterOptions { PreferConditionalExpressionReturn = true }));

        // Suppressing the IL must not swallow the evidence that explains it: the
        // Applied Taste section reads these decisions off the same result.
        var decision = Assert.Single(
            Assert.IsType<DecompilerResult>(projection.AnnotatedSource).Decisions,
            d => d.RuleId == "style-lens.prefer-conditional-return");
        Assert.Equal(DecompilerDecisionCategories.StyleLens, decision.Category);
    }

    [Fact]
    public void ByteDivergentLens_DoesNotLeakIntoTheStyleInvariantOverlays()
    {
        // Printing raises and rewrites the IR in place, so an annotated render
        // that shares its function with the overlays would let a lens requested
        // for this one view reshape renders that never asked for it: selecting a
        // section would change a different section's output. Pin that the overlays
        // render the member's own control flow no matter which sections are also
        // requested in the same projection.
        static ResearchViews.MemberProjectionResult Project(bool annotated)
        {
            using var source = MetadataSource.Open(typeof(AnnotatedTasteFixture).Assembly.Location);
            return ResearchViews.ProjectMember(new ResearchViews.MemberProjectionRequest(
                source,
                typeof(AnnotatedTasteFixture).FullName!,
                nameof(AnnotatedTasteFixture.GuardBothVariable),
                AnnotatedSource: annotated,
                CostOverlay: true,
                SemanticsOverlay: true,
                PrinterOptions: annotated ? new PrinterOptions { PreferConditionalExpressionReturn = true } : null));
        }

        var overlaysOnly = Project(annotated: false);
        var withAnnotated = Project(annotated: true);

        // The lens did fire, so this is a live test and not a vacuous one.
        Assert.Contains("Interleaved IL suppressed", Assert.IsType<string>(withAnnotated.AnnotatedSource?.Output));

        Assert.Equal(overlaysOnly.CostOverlay?.Body.Output, withAnnotated.CostOverlay?.Body.Output);
        Assert.Equal(overlaysOnly.SemanticsOverlay?.Output, withAnnotated.SemanticsOverlay?.Output);
        Assert.DoesNotContain("a ? b : c", Assert.IsType<string>(withAnnotated.CostOverlay?.Body.Output));
        Assert.DoesNotContain("a ? b : c", Assert.IsType<string>(withAnnotated.SemanticsOverlay?.Output));
    }
}

public sealed class AnnotatedTasteFixture
{
    int _count = 1;

    public int Extra { get; set; }

    // Instance field and property reads on the enclosing type at its own
    // instantiation: the byte-preserving `this.` qualification knobs apply here.
    public int Compute(string s)
    {
        int len = s.Length;
        return _count + Extra + len;
    }

    // Both arms variable, so the default pipeline declines the short-circuit fold
    // (no such fold is opcode-faithful) and leaves the guarded return flat — the
    // shape the byte-divergent conditional-return lens rewrites.
    public static bool GuardBothVariable(bool a, bool b, bool c)
    {
        if (a)
        {
            return b;
        }

        return c;
    }
}
