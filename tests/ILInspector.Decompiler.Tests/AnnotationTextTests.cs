using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The gate <see cref="AnnotationText"/> names. Fact text is baked into
/// single-line trailing <c>//</c> comments by every consumer, and it is appended
/// to IL comment lines <em>after</em> the IL producer has folded them, so a
/// terminator arriving through a fact would escape a comment the producer
/// believed it had closed. <see cref="AnnotationText.Format(IAnnotation)"/> is
/// the one place that turns a fact into display text, so it is the one place
/// that has to fold.
/// </summary>
public class AnnotationTextTests
{
    static readonly AnnotationDescriptor Descriptor =
        new("call.direct", AnnotationCategory.Cost, "direct call");

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\f")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void Format_FoldsLineTerminatorsInDetail(string terminator)
    {
        const string marker = "public int Injected() => 42; //";
        var fact = new Annotation(Descriptor, 0, $"Callee{terminator}    {marker}");

        var text = AnnotationText.Format(fact);

        Assert.DoesNotContain(terminator, text, StringComparison.Ordinal);
        Assert.Contains(marker, text, StringComparison.Ordinal);
        Assert.Single(text.ReplaceLineEndings("\n").Split('\n'));
    }

    [Fact]
    public void Format_FoldsAcrossAListOfFacts()
    {
        var facts = new IAnnotation[]
        {
            new Annotation(Descriptor, 0, "Safe"),
            new Annotation(Descriptor, 0, "Hostile\n    public int Injected() => 42; //"),
        };

        var text = AnnotationText.Format(facts);

        Assert.Single(text.ReplaceLineEndings("\n").Split('\n'));
    }

    /// <summary>
    /// Non-vacuity: the fold is only load-bearing because a terminator survives
    /// composition unescaped. If <see cref="Annotation.Detail"/> ever started
    /// sanitizing on the way in, this test would fail and say so, rather than
    /// leaving the tests above passing for a reason that no longer holds.
    /// </summary>
    [Fact]
    public void Detail_IsNotSanitizedOnTheWayIn_SoTheFoldIsLoadBearing()
    {
        var fact = new Annotation(Descriptor, 0, "Callee\nInjected");

        Assert.Contains("\n", fact.Detail);
    }

    /// <summary>
    /// The fold is display-only. Typed consumers — JSON and TSV projections,
    /// identity diffing — read <see cref="IAnnotation.Detail"/> directly and
    /// must keep seeing the exact metadata text.
    /// </summary>
    [Fact]
    public void Format_DoesNotMutateTheUnderlyingDetail()
    {
        var fact = new Annotation(Descriptor, 0, "Callee\nInjected");

        _ = AnnotationText.Format(fact);

        Assert.Equal("Callee\nInjected", fact.Detail);
    }

    [Fact]
    public void Format_LeavesOrdinaryDetailByteIdentical()
    {
        var fact = new Annotation(Descriptor, 0, "System.Console::WriteLine");

        Assert.Equal("call.direct(System.Console::WriteLine)", AnnotationText.Format(fact));
    }
}
