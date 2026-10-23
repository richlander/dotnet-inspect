using DotnetInspect.Web.Interop.Source;
using Inspector.Findings;

namespace DotnetInspect.Web.Tests;

/// <summary>
/// The Browser method-body projection never shows a success-shaped view for a
/// Findings comparison that did not run: a comparison that refused two
/// complete inspections for a stated reason, such as a pair beyond the
/// ordered matcher's bound, is Unavailable with its reason as a diagnostic
/// (finding-census.md#exhaustive), as an inspection failure is.
/// </summary>
public sealed class BrowserMethodBodyProjectionTests
{
    static readonly FindingSubject Subject = new("N.C::M", "M");
    static readonly FindingDescriptor Descriptor = new("test.atom", "Test atom");

    [Fact]
    public void Verdict_ComparisonFailureOverCompleteInspections_IsUnavailableWithADiagnostic()
    {
        FindingInspection<string> complete = new FindingInspection<string>.Complete([Atom("a")]);
        FindingComparison<string> refused = FindingComparison.Failed(complete, complete, "IL comparison skipped: bounded");
        var diagnostics = new List<BrowserMethodBodyDiagnostic>();

        string verdict = BrowserMethodBodyProjection.Verdict(refused, nativeVerdict: null, diagnostics);

        Assert.Equal("Unavailable", verdict);
        BrowserMethodBodyDiagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("comparison", diagnostic.Kind);
        Assert.Equal("IL comparison skipped: bounded", diagnostic.Message);
    }

    [Fact]
    public void Verdict_InspectionFailure_IsUnavailableWithoutAComparisonDiagnostic()
    {
        FindingInspection<string> failed = new FindingInspection<string>.Failed(new InspectionError(Subject, Descriptor, "undecodable"));
        FindingInspection<string> complete = new FindingInspection<string>.Complete([Atom("a")]);
        var diagnostics = new List<BrowserMethodBodyDiagnostic>();

        Assert.Equal("Unavailable", BrowserMethodBodyProjection.Verdict(FindingComparison.Compare(failed, complete), null, diagnostics));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Verdict_CompleteComparisonWithoutNativeEvidence_IsNotApplicable_AndNativeEvidenceWins()
    {
        FindingInspection<string> absent = new FindingInspection<string>.Absent(FindingInspectionAbsenceKind.NoApplicableInput);
        var diagnostics = new List<BrowserMethodBodyDiagnostic>();
        FindingComparison<string> comparison = FindingComparison.Compare(absent, absent);

        Assert.Equal("NotApplicable", BrowserMethodBodyProjection.Verdict(comparison, null, diagnostics));
        Assert.Equal("Exact", BrowserMethodBodyProjection.Verdict(comparison, "Exact", diagnostics));
        Assert.Empty(diagnostics);
    }

    static Finding<string> Atom(string value)
        => new(Subject, Descriptor, new FindingKey(value), value);
}
