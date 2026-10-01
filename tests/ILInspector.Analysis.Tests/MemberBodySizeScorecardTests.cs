using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.PerformanceOracles;
using ILInspector.Analysis.ImplementationProfileFixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public sealed class MemberBodySizeScorecardTests
{
    [Fact]
    public void FixtureColumnsAgreeAndRetainGeneratedBodies()
    {
        MemberBodySizeScorecardScenario scenario = new(
            "generated overload family",
            typeof(ImplementationHeatLambdaSample)
                .Assembly.Location,
            "ILInspector.Analysis.ImplementationProfileFixtures",
            nameof(ImplementationHeatLambdaSample),
            nameof(ImplementationHeatLambdaSample.Scale),
            MemberBodySizeScope.PublicFamily,
            ExpectedRows: 2);

        MemberBodySizeScorecardCheck check =
            MemberBodySizeScorecard.Check([scenario]);

        Assert.True(
            check.Agrees,
            string.Join(
                Environment.NewLine,
                check.Mismatches));
        ScorecardAnswer<MemberBodySizeProjectionRow> rows =
            MemberBodySizeScorecard.Execute(
                scenario,
                "NLinq",
                ScorecardClosing.Rows);
        Assert.Equal(2, rows.Rows!.Count);
        Assert.All(
            rows.Rows,
            static row =>
                Assert.True(row.PhysicalBodyCount > 1));
    }

    [Fact]
    public void BodylessFamilyProducesNoMetricRows()
    {
        MemberBodySizeScorecardScenario scenario = new(
            "bodyless overload family",
            typeof(IImplementationProfileBodylessSample)
                .Assembly.Location,
            "ILInspector.Analysis.ImplementationProfileFixtures",
            nameof(IImplementationProfileBodylessSample),
            nameof(IImplementationProfileBodylessSample.Route),
            MemberBodySizeScope.PublicFamily,
            ExpectedRows: 0);

        MemberBodySizeScorecardCheck check =
            MemberBodySizeScorecard.Check([scenario]);

        Assert.True(check.Agrees);
        ScorecardAnswer<MemberBodySizeProjectionRow> rows =
            MemberBodySizeScorecard.Execute(
                scenario,
                "Planner (experimental)",
                ScorecardClosing.Rows);
        Assert.Empty(rows.Rows!);
    }

    [Fact]
    public void DefaultScenariosDisambiguateRepeatedAssetPaths()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts",
            "packages",
            "System.Text.Json.10.0.0.dll");

        IReadOnlyList<MemberBodySizeScorecardScenario>
            scenarios =
                MemberBodySizeScorecard.DefaultScenarios(
                    [path, path]);

        Assert.Equal(4, scenarios.Count);
        Assert.Equal(
            4,
            scenarios
                .Select(static scenario =>
                    scenario.Name)
                .Distinct()
                .Count());
        Assert.Contains(
            scenarios,
            static scenario =>
                scenario.Name.StartsWith(
                    "System.Text.Json.10.0.0 #1 ",
                    StringComparison.Ordinal));
        Assert.Contains(
            scenarios,
            static scenario =>
                scenario.Name.StartsWith(
                    "System.Text.Json.10.0.0 #2 ",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ReportUsesScenarioIdentityAndPublishesRatioRanges()
    {
        IReadOnlyList<MemberBodySizeScorecardCell> cells =
        [
            .. Scorecard.Closings.SelectMany(
                closing =>
                    new[]
                    {
                        Cell(0, closing, "Old", 20),
                        Cell(0, closing, "LINQ", 10),
                        Cell(0, closing, "NLinq", 10),
                        Cell(
                            0,
                            closing,
                            "Planner (experimental)",
                            5),
                        Cell(1, closing, "Old", 40),
                        Cell(1, closing, "LINQ", 20),
                        Cell(1, closing, "NLinq", 10),
                        Cell(
                            1,
                            closing,
                            "Planner (experimental)",
                            5),
                    }),
        ];
        var result = new MemberBodySizeScorecardResult(
            new(0, [], []),
            cells,
            []);

        string report =
            MemberBodySizeScorecard.Report(result);

        Assert.Contains(
            "2.83x (2.00-4.00x)",
            report);
        Assert.Contains(
            "| Closing | Old/Planner geo mean (range) |",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Planner (experimental) | Time | "
                + "1.00x (1.00-1.00x) | 1.00x | "
                + "1.00x (1.00-1.00x) |",
            report,
            StringComparison.Ordinal);
        Assert.Contains(
            "| Planner (experimental) | Allocation | "
                + "- | - | - |",
            report,
            StringComparison.Ordinal);

        static MemberBodySizeScorecardCell Cell(
            int scenarioIndex,
            ScorecardClosing closing,
            string column,
            double microseconds) =>
            new(
                scenarioIndex,
                "duplicate display name",
                closing,
                column,
                microseconds,
                AllocatedBytes: 0,
                WindowFailed: false);
    }

    [Fact]
    public void DirectColumnsContainMalformedPhysicalBody()
    {
        MemberBodySizeScorecardScenario scenario = new(
            "generated overload family",
            typeof(ImplementationHeatLambdaSample)
                .Assembly.Location,
            "ILInspector.Analysis.ImplementationProfileFixtures",
            nameof(ImplementationHeatLambdaSample),
            nameof(ImplementationHeatLambdaSample.Scale),
            MemberBodySizeScope.PublicFamily,
            ExpectedRows: 2);
        MemberBodySizePhysicalBody generated =
            MemberBodySizeScorecard
                .PhysicalBodies(scenario)
                .First(static body =>
                    body.LogicalMethodToken
                        != body.PhysicalMethodToken);
        byte[] image =
            File.ReadAllBytes(scenario.AssemblyPath);
        using (var peReader =
            new PEReader(
                new MemoryStream(
                    image,
                    writable: false)))
        {
            MetadataReader reader =
                peReader.GetMetadataReader();
            var handle =
                (MethodDefinitionHandle)
                MetadataTokens.EntityHandle(
                    generated.PhysicalMethodToken);
            MethodDefinition method =
                reader.GetMethodDefinition(handle);
            int offset = RvaToFileOffset(
                peReader.PEHeaders,
                method.RelativeVirtualAddress);
            image[offset] = 0;
        }

        IReadOnlyList<MemberBodySizeSafetyResult> results =
            MemberBodySizeScorecard.CheckDirectSafety(
                scenario,
                image);

        Assert.All(
            results,
            static result =>
            {
                Assert.True(result.Incomplete);
                Assert.Equal(
                    nameof(BadImageFormatException),
                    result.Failure);
            });
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void PinnedRealAssetColumnsAgree()
    {
        string artifacts = Path.Combine(
            AppContext.BaseDirectory,
            "PinnedArtifacts");
        IReadOnlyList<MemberBodySizeScorecardScenario>
            scenarios =
                MemberBodySizeScorecard.DefaultScenarios(
                [
                    Path.Combine(
                        artifacts,
                        "System.Private.CoreLib.dll"),
                    Path.Combine(
                        artifacts,
                        "packages",
                        "System.Text.Json.10.0.0.dll"),
                ]);

        MemberBodySizeScorecardCheck check =
            MemberBodySizeScorecard.Check(scenarios);

        Assert.True(
            check.Agrees,
            string.Join(
                Environment.NewLine,
                check.Mismatches));
    }

    static int RvaToFileOffset(
        PEHeaders headers,
        int rva)
    {
        SectionHeader section =
            headers.SectionHeaders.Single(header =>
                rva >= header.VirtualAddress
                && rva < header.VirtualAddress
                    + Math.Max(
                        header.VirtualSize,
                        header.SizeOfRawData));
        return checked(
            rva
            - section.VirtualAddress
            + section.PointerToRawData);
    }
}
