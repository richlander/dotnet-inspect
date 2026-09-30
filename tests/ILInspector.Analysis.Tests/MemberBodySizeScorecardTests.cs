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
