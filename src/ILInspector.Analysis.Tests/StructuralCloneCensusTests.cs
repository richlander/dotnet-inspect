using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneCensusTests
{
    static string FixturePath =>
        typeof(StructuralCloneFixture).Assembly.Location;

    static string FixtureType =>
        typeof(StructuralCloneFixture).FullName!;

    [Fact]
    public void Run_FixtureAssembly_ReportsExactFamiliesAndSeedFamily()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.ExactPositiveA)}");

        Assert.True(report.Success);
        Assert.Equal(
            StructuralCloneDiscoveryDisposition.Completed,
            report.Disposition);
        Assert.True(report.Clusters >= 5);
        Assert.True(report.ClusteredMethods >= 10);
        Assert.True(report.ExactSingletonMethods > 0);
        Assert.True(report.Receipt.UnsupportedMethods >= 2);
        Assert.True(report.Receipt.ExactComparisons >= 5);
        Assert.True(report.Receipt.DifferentComparisons >= 1);

        StructuralCloneCensusSeed seed = Assert.IsType<
            StructuralCloneCensusSeed>(report.Seed);
        Assert.Equal(
            StructuralCloneCensusSeedStatus.Clustered,
            seed.Status);
        StructuralCloneCensusCluster family = Assert.IsType<
            StructuralCloneCensusCluster>(seed.Cluster);
        Assert.Equal(
            [
                nameof(StructuralCloneFixture.ExactPositiveA),
                nameof(StructuralCloneFixture.ExactPositiveB),
            ],
            family.Members.Select(static member => member.Name));
        Assert.All(
            family.Members,
            static member => Assert.Equal(
                0x06000000,
                member.Token & unchecked((int)0xFF000000)));
    }

    [Fact]
    public void Run_CloseNegativeSeed_IsSingletonOnlyAfterCompleteDiscovery()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.EdgeRoleNegativeA)}");

        Assert.Equal(
            StructuralCloneCensusSeedStatus.Singleton,
            report.Seed!.Status);
        Assert.Null(report.Seed.Cluster);
        Assert.Equal(
            StructuralCloneDisposition.Completed,
            report.Seed.ProductionDisposition);
    }

    [Fact]
    public void Run_UnsupportedSeed_RemainsExplicit()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.ExceptionHandlingA)}");

        Assert.Equal(
            StructuralCloneCensusSeedStatus.Unsupported,
            report.Seed!.Status);
        Assert.Equal(
            StructuralCloneDisposition.Unsupported,
            report.Seed.ProductionDisposition);
        Assert.Contains(
            report.Seed.Blockers,
            static blocker =>
                blocker.Kind
                    == StructuralCloneDiscoveryBlockerKind.MethodUnsupported);
    }

    [Fact]
    public void Run_MethodLimitMakesSeedUnresolvedNotSingleton()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.EdgeRoleNegativeA)}",
            maximumMethods: 1);

        Assert.False(report.Success);
        Assert.Equal(
            StructuralCloneDiscoveryDisposition.LimitReached,
            report.Disposition);
        Assert.Null(report.ExactSingletonMethods);
        Assert.Equal(
            StructuralCloneCensusSeedStatus.Unresolved,
            report.Seed!.Status);
        Assert.Equal(0, report.Receipt.ProcessedMethods);
        string text = StructuralCloneCensus.Format(report);
        Assert.Contains("suppressed=57", text);
        Assert.Contains(
            "eligible-without-emitted-family=0",
            text);
    }

    [Fact]
    public void Run_ComparisonLimitProjectsSuppressedBuckets()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.MetadataOperandsA)}",
            maximumCandidateComparisons: 1);

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.LimitReached,
            report.Disposition);
        Assert.NotEmpty(report.SuppressedBuckets);
        Assert.Equal(
            StructuralCloneCensusSeedStatus.Unresolved,
            report.Seed!.Status);
        Assert.Null(report.ExactSingletonMethods);
        Assert.Contains(
            "suppressed buckets",
            StructuralCloneCensus.Format(report, top: 1));
    }

    [Fact]
    public void Run_ClusteredSeedSurvivesUnrelatedPartialWork()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.ExactPositiveA)}",
            maximumCandidateComparisons: 1);

        Assert.Equal(
            StructuralCloneDiscoveryDisposition.LimitReached,
            report.Disposition);
        Assert.Equal(
            StructuralCloneCensusSeedStatus.Clustered,
            report.Seed!.Status);
        Assert.Equal(2, report.Seed.Cluster!.Members.Length);
        Assert.NotEmpty(report.SuppressedBuckets);
        Assert.Null(report.ExactSingletonMethods);
    }

    [Fact]
    public void Run_MethodDefTokenRoundTripsToSameSeed()
    {
        StructuralCloneCensusReport named = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.MetadataOperandsA)}");
        int token = named.Seed!.Method.Token;

        StructuralCloneCensusReport tokenSelected =
            StructuralCloneCensus.Run(
                FixturePath,
                $"0x{token:X8}");

        Assert.Equal(token, tokenSelected.Seed!.Method.Token);
        Assert.Equal(
            StructuralCloneCensusSeedStatus.Clustered,
            tokenSelected.Seed.Status);
    }

    [Fact]
    public void Run_AmbiguousNameSelectorRequiresMethodDefToken()
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => StructuralCloneCensus.Run(
                typeof(object).Assembly.Location,
                "System.String::Concat"));

        Assert.Contains("is ambiguous", error.Message);
        Assert.Contains("0x06", error.Message);
    }

    [Fact]
    public void Run_InvalidLimitsAreCallerErrors()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StructuralCloneCensus.Run(
                FixturePath,
                maximumMethods: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StructuralCloneCensus.Run(
                FixturePath,
                maximumCandidateComparisons: 0));
    }

    [Fact]
    public void Format_PinsSeedAndPeerWhenTopIsOne()
    {
        StructuralCloneCensusReport report = StructuralCloneCensus.Run(
            FixturePath,
            $"{FixtureType}::{nameof(StructuralCloneFixture.MetadataOperandsB)}");

        string text = StructuralCloneCensus.Format(report, top: 1);

        Assert.Contains(
            nameof(StructuralCloneFixture.MetadataOperandsA),
            text);
        Assert.Contains(
            nameof(StructuralCloneFixture.MetadataOperandsB),
            text);
        Assert.Contains("more families omitted", text);
        Assert.Contains("more members omitted", text);
    }

    [Fact]
    public void Json_ContainsAllFamiliesAndTypedReceipt()
    {
        StructuralCloneCensusReport report =
            StructuralCloneCensus.Run(FixturePath);

        using JsonDocument json = JsonDocument.Parse(
            StructuralCloneCensus.ToJson(report));
        JsonElement root = json.RootElement;

        Assert.Equal(
            report.Clusters,
            root.GetProperty("families").GetArrayLength());
        Assert.Equal(
            "Completed",
            root.GetProperty("disposition").GetString());
        Assert.Equal(
            report.Receipt.BodyProductions,
            root.GetProperty("receipt")
                .GetProperty("bodyProductions")
                .GetInt32());
        Assert.Equal(
            report.Receipt.UnsupportedMethods
                + report.Receipt.LimitReachedMethods
                + report.Receipt.FailedMethods,
            root.GetProperty("nonCompletedMethods").GetArrayLength());
        Assert.Equal(
            report.Receipt.UnresolvedComparisons,
            root.GetProperty("unresolvedComparisons").GetArrayLength());
    }

    [Fact]
    public void Run_RejectsMalformedManagedMetadata()
    {
        byte[] bytes = File.ReadAllBytes(FixturePath);
        using (var image = new PEReader(new MemoryStream(bytes)))
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(
                    image.PEHeaders.CorHeaderStartOffset
                        + 3 * sizeof(uint)),
                uint.MaxValue);
        }
        string path = Path.Combine(
            Path.GetTempPath(),
            $"structural-clone-census-malformed-{Guid.NewGuid():N}.dll");
        try
        {
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidDataException>(
                () => StructuralCloneCensus.Run(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Command_RejectsMissingAndInvalidCloneCensusArguments()
    {
        (int exitCode, string output, string error) missing =
            await RunHarness("--clone-census");
        Assert.Equal(2, missing.exitCode);
        Assert.Equal("", missing.output);
        Assert.Contains(
            "--clone-census requires an assembly path.",
            missing.error);

        (int exitCode, string output, string error) invalid =
            await RunHarness(
                "--clone-census",
                FixturePath,
                "--max-comparisons",
                "0");
        Assert.Equal(2, invalid.exitCode);
        Assert.Equal("", invalid.output);
        Assert.Contains(
            "--max-comparisons requires a positive integer.",
            invalid.error);

        (int exitCode, string output, string error) invalidTop =
            await RunHarness(
                "--clone-census",
                FixturePath,
                "--top",
                "0");
        Assert.Equal(2, invalidTop.exitCode);
        Assert.Equal("", invalidTop.output);
        Assert.Contains(
            "--top requires a positive integer.",
            invalidTop.error);

        (int exitCode, string output, string error) invalidTopOtherMode =
            await RunHarness(
                "--precision-sample",
                FixturePath,
                "--top",
                "invalid");
        Assert.Equal(2, invalidTopOtherMode.exitCode);
        Assert.Equal("", invalidTopOtherMode.output);
        Assert.Contains(
            "--top requires a positive integer.",
            invalidTopOtherMode.error);

        (int exitCode, string output, string error) orphan =
            await RunHarness("--seed", "System.String::Concat");
        Assert.Equal(2, orphan.exitCode);
        Assert.Equal("", orphan.output);
        Assert.Contains("require --clone-census", orphan.error);
    }

    [Fact]
    public async Task Command_EmitsCompletedJsonDemo()
    {
        (int exitCode, string output, string error) result =
            await RunHarness(
                "--clone-census",
                FixturePath,
                "--seed",
                $"{FixtureType}::{nameof(StructuralCloneFixture.ExactPositiveA)}",
                "--top",
                "1",
                "--json");

        Assert.Equal(0, result.exitCode);
        Assert.Equal("", result.error);
        using JsonDocument json = JsonDocument.Parse(result.output);
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            "Clustered",
            json.RootElement
                .GetProperty("seed")
                .GetProperty("status")
                .GetString());
    }

    [Theory]
    [InlineData("--diff-corpus-baseline")]
    [InlineData("--emit-corpus-snapshot")]
    [InlineData("--reference")]
    public async Task Command_RejectsMissingSharedOptionValues(
        string option)
    {
        (int exitCode, string output, string error) result =
            await RunHarness(option, "--json");

        Assert.Equal(2, result.exitCode);
        Assert.Equal("", result.output);
        Assert.Contains($"{option} requires a file path.", result.error);
    }

    [Theory]
    [InlineData(
        "--diff-corpus-baseline",
        "--corpus-list")]
    [InlineData(
        "--emit-corpus-snapshot",
        "--corpus-list")]
    [InlineData("--reference", "--paydirt-recall")]
    [InlineData("--max-depth", "--caller-loop-census")]
    [InlineData("--top", "does not apply")]
    public async Task Command_RejectsOptionsOutsideOwningMode(
        string option,
        string expectedError)
    {
        (int exitCode, string output, string error) result =
            await RunHarness(
                "--clone-corpus",
                FixturePath,
                option,
                option is "--max-depth" or "--top"
                    ? "1"
                    : FixturePath);

        Assert.Equal(2, result.exitCode);
        Assert.Equal("", result.output);
        Assert.Contains(expectedError, result.error);
    }

    [Fact]
    public async Task Command_RejectsKeepOutsideGeneratedFixtures()
    {
        (int exitCode, string output, string error) result =
            await RunHarness(
                "--clone-corpus",
                FixturePath,
                "--keep");

        Assert.Equal(2, result.exitCode);
        Assert.Equal("", result.output);
        Assert.Contains(
            "--keep requires --generated-fixtures.",
            result.error);
    }

    [Fact]
    public async Task Command_RejectsEarlierMissingDuplicateValues()
    {
        (int exitCode, string output, string error) reference =
            await RunHarness(
                "--paydirt-recall",
                FixturePath,
                "--reference",
                "--reference",
                FixturePath);
        Assert.Equal(2, reference.exitCode);
        Assert.Equal("", reference.output);
        Assert.Contains(
            "--reference requires a file path.",
            reference.error);

        (int exitCode, string output, string error) mode =
            await RunHarness(
                "--clone-census",
                "--clone-census",
                FixturePath);
        Assert.Equal(2, mode.exitCode);
        Assert.Equal("", mode.output);
        Assert.Contains(
            "--clone-census requires an assembly path.",
            mode.error);

        (int exitCode, string output, string error) seed =
            await RunHarness(
                "--clone-census",
                FixturePath,
                "--seed",
                "--seed",
                $"{FixtureType}::"
                    + nameof(StructuralCloneFixture.ExactPositiveA));
        Assert.Equal(2, seed.exitCode);
        Assert.Equal("", seed.output);
        Assert.Contains("--seed requires a selector.", seed.error);
    }

    [Fact]
    public async Task Command_RejectsMultipleTopLevelModes()
    {
        (int exitCode, string output, string error) cloneConflict =
            await RunHarness(
                "--clone-corpus",
                FixturePath,
                "--clone-census",
                FixturePath);
        Assert.Equal(2, cloneConflict.exitCode);
        Assert.Equal("", cloneConflict.output);
        Assert.Contains("--clone-corpus", cloneConflict.error);
        Assert.Contains("--clone-census", cloneConflict.error);

        (int exitCode, string output, string error) historicalConflict =
            await RunHarness(
                "--clone-corpus",
                FixturePath,
                "--historical-performance-recall");
        Assert.Equal(2, historicalConflict.exitCode);
        Assert.Equal("", historicalConflict.output);
        Assert.Contains("--clone-corpus", historicalConflict.error);
        Assert.Contains(
            "--historical-performance-recall",
            historicalConflict.error);

        (int exitCode, string output, string error) existingConflict =
            await RunHarness(
                "--corpus-list",
                FixturePath,
                "--leak-triage",
                FixturePath);
        Assert.Equal(2, existingConflict.exitCode);
        Assert.Equal("", existingConflict.output);
        Assert.Contains("--corpus-list", existingConflict.error);
        Assert.Contains("--leak-triage", existingConflict.error);

        (int exitCode, string output, string error) missingOperandConflict =
            await RunHarness(
                "--leak-triage",
                "--clone-census",
                FixturePath);
        Assert.Equal(2, missingOperandConflict.exitCode);
        Assert.Equal("", missingOperandConflict.output);
        Assert.Contains("--leak-triage", missingOperandConflict.error);
        Assert.Contains("--clone-census", missingOperandConflict.error);

        (int exitCode, string output, string error) consumedModeConflict =
            await RunHarness(
                "--corpus-list",
                "--historical-performance-recall");
        Assert.Equal(2, consumedModeConflict.exitCode);
        Assert.Equal("", consumedModeConflict.output);
        Assert.Contains("--corpus-list", consumedModeConflict.error);
        Assert.Contains(
            "--historical-performance-recall",
            consumedModeConflict.error);

        (int exitCode, string output, string error) validationOrderConflict =
            await RunHarness(
                "--clone-census",
                "--leak-triage",
                FixturePath);
        Assert.Equal(2, validationOrderConflict.exitCode);
        Assert.Equal("", validationOrderConflict.output);
        Assert.Contains("--clone-census", validationOrderConflict.error);
        Assert.Contains("--leak-triage", validationOrderConflict.error);

        foreach (string numericOption in new[]
                 {
                     "--max-methods",
                     "--max-comparisons",
                     "--max-depth",
                 })
        {
            (int exitCode, string output, string error) numericConflict =
                await RunHarness(
                    "--clone-census",
                    FixturePath,
                    "--leak-triage",
                    FixturePath,
                    numericOption);
            Assert.Equal(2, numericConflict.exitCode);
            Assert.Equal("", numericConflict.output);
            Assert.Contains("--clone-census", numericConflict.error);
            Assert.Contains("--leak-triage", numericConflict.error);
        }
    }

    static async Task<(int ExitCode, string Output, string Error)> RunHarness(
        params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneCensus).Assembly.Location);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (
            process.ExitCode,
            await standardOutput,
            await standardError);
    }
}
