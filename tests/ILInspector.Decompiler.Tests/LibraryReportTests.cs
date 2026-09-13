using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class LibraryReportTests
{
    [Fact]
    public void Evaluate_ReportsUnavailableModesWithoutRunningCompilerLanes()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"library-report-unavailable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "UnsupportedRules.dll");
            File.WriteAllBytes(
                path,
                WithMemorySafetyRulesVersion(
                    File.ReadAllBytes(
                        FixtureCatalog.DecompilerUnsafeNew.AssemblyPath()),
                    version: 99));

            LibraryPortfolioReport portfolio = LibraryReport.Evaluate(
                [path],
                compileCap: 5,
                maxExamples: 5,
                topPatterns: 10,
                topLibraries: null,
                methodCap: 5);

            AssemblyReport report = Assert.Single(portfolio.Libraries);
            Assert.Equal(5, report.TotalMethods);
            Assert.Equal(5, report.CompileBackUnavailableMethods);
            Assert.Equal(5, portfolio.TotalCompileBackUnavailableMethods);
            Assert.Equal(0, report.FullMethods);
            Assert.Equal(0, report.PartialMethods);
            Assert.Equal(0, report.FullyRaisedMethods);
            Assert.Equal(0, report.RenderedMethods);
            Assert.Equal(0, report.SemanticChecked);
            Assert.Equal(0, report.PassBugs);
            PatternReport unavailable = Assert.Single(
                report.Patterns,
                pattern => pattern.Name == "compile-back-unavailable");
            Assert.Equal(5, unavailable.Count);
            Assert.All(
                unavailable.Examples,
                example => Assert.Contains(
                    "module memory-safety rules are Unsupported",
                    example,
                    StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
        Assert.Equal("fidelity: unsupported-node", Assert.Single(portfolio.TopPatterns).Name);
        var candidate = Assert.Single(portfolio.PromotionCandidates);
        Assert.Equal("Defective", candidate.Assembly);
        Assert.Equal(
            [
                "1 pass bug method(s)",
                "2 malformed Full method(s)",
                "1 bound Full defect method(s)",
                "defect classes: validity: malformed:CS1002, pass-bug: InvalidOperationException",
            ],
            candidate.Reasons);
    }

    [Fact]
    public void BuildPortfolio_KeepsGlobalEvidenceOutsideDisplayedLibraries()
    {
        var hiddenDefect = Report(
            "HiddenDefect",
            new PatternReport("pass-bug: InvalidOperationException", 1, ["HiddenDefect::M"]))
            with
            {
                PassBugs = 1,
            };
        var displayed = Report(
            "Displayed",
            new PatternReport("fidelity: unsupported-node", 10, ["Displayed::M"]));

        var portfolio = LibraryReport.BuildPortfolio(
            [hiddenDefect, displayed],
            topPatterns: 10,
            maxExamples: 2,
            displayedLibraries: [displayed]);

        Assert.Equal(1, portfolio.TotalPassBugs);
        Assert.Equal("pass-bug: InvalidOperationException", Assert.Single(portfolio.DefectClasses).Name);
        Assert.Equal("HiddenDefect", Assert.Single(portfolio.PromotionCandidates).Assembly);
        Assert.Equal("Displayed", Assert.Single(portfolio.Libraries).Assembly);
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

    static byte[] WithMemorySafetyRulesVersion(byte[] image, int version)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MemorySafetyRulesObservation observation = Assert.Single(
            MemorySafetyMetadataIndex.Create(reader)
                .Rules
                .Observations);
        CustomAttribute attribute = reader.GetCustomAttribute(
            (CustomAttributeHandle)MetadataTokens.EntityHandle(
                observation.AttributeToken));
        byte[] original = reader.GetBlobBytes(attribute.Value);
        int valueOffset = Assert.Single(
            Enumerable.Range(0, image.Length - original.Length + 1),
            offset => image
                .AsSpan(offset, original.Length)
                .SequenceEqual(original));
        byte[] rewritten = [.. image];
        BitConverter.TryWriteBytes(
            rewritten.AsSpan(valueOffset + 2, sizeof(int)),
            version);
        return rewritten;
    }
}
