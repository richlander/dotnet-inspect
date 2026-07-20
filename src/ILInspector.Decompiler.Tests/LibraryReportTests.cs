using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class LibraryReportTests
{
    [Fact]
    public void BuildPortfolio_SeparatesCorrectnessDefectsAndPromotionCandidates()
    {
        var clean = Report(
            "Clean",
            new PatternReport("fidelity: unsupported-node", 3, ["Clean::M"]));
        var defective = Report(
            "Defective",
            new PatternReport("validity: malformed:CS1002", 2, ["Defective::M"]),
            new PatternReport("pass-bug: InvalidOperationException", 1, ["Defective::N"]))
            with
            {
                FullMalformed = 2,
                SemanticDefectMethods = 1,
                PassBugs = 1,
            };

        var portfolio = LibraryReport.BuildPortfolio(
            [clean, defective],
            topPatterns: 10,
            maxExamples: 2);

        Assert.Equal(2, portfolio.DefectClasses.Count);
        Assert.DoesNotContain(
            portfolio.DefectClasses,
            pattern => pattern.Name == "fidelity: unsupported-node");
        var candidate = Assert.Single(portfolio.PromotionCandidates);
        Assert.Equal("Defective", candidate.Assembly);
        Assert.Equal(
            [
                "1 pass bug method(s)",
                "2 malformed Full method(s)",
                "1 bound Full defect method(s)",
            ],
            candidate.Reasons);
    }

    static AssemblyReport Report(string assembly, params PatternReport[] patterns)
        => new(
            assembly,
            $"/tmp/{assembly}.dll",
            AvailableMethods: 10,
            TotalMethods: 10,
            FullMethods: 8,
            PartialMethods: 2,
            FullyRaisedMethods: 7,
            RenderedMethods: 10,
            FullMalformed: 0,
            PartialMalformed: 0,
            SemanticChecked: 0,
            SemanticDefectMethods: 0,
            PassBugs: 0,
            patterns);
}
