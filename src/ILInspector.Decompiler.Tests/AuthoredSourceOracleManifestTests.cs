using ILInspector.DecompilerHarness;
using ILInspector.Metadata;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Corpus")]
public sealed class AuthoredSourceOracleManifestTests
{
    [Fact]
    public void PrinterComparison_IsExactBeforeNormalization()
    {
        Assert.Equal(
            PrinterExactOutcome.Exact,
            ReturnToSenderSourceProbe.ComparePrinterText(
                "if (value)\n{\n    return;\n}",
                "if (value)\n{\n    return;\n}\n"));
        Assert.Equal(
            PrinterExactOutcome.Different,
            ReturnToSenderSourceProbe.ComparePrinterText(
                "if (value)\n{\n    return;\n}",
                "if (value) { return; }"));
        Assert.Equal(
            PrinterExactOutcome.NotRecorded,
            ReturnToSenderSourceProbe.ComparePrinterText(
                expected: null,
                "if (value) { return; }"));
    }

    [Fact]
    public void SourceProbe_PurposeBuiltFixtureClearsAllThreeSourceLayers()
    {
        const string memberSource = """
            public static int PrinterExactFixture(int value)
            {
                return value + 1;
            }
            """;
        Assert.True(AuthoredRebuildFidelity.TryExtractTargetBodies(
            memberSource,
            nameof(PrinterExactFixture),
            expectedParameterCount: 1,
            out string expected,
            out string? printerBody));
        Assert.NotNull(printerBody);
        string assemblyPath = typeof(AuthoredSourceOracleManifestTests).Assembly.Location;
        using var pe = new PEReader(System.IO.File.OpenRead(assemblyPath));
        MetadataReader reader = pe.GetMetadataReader();
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetFullTypeName(reader.GetTypeDefinition(handle))
                == typeof(AuthoredSourceOracleManifestTests).FullName);
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        MethodDefinitionHandle methodHandle = type.GetMethods().Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name)
                == nameof(PrinterExactFixture));
        int overload = ReturnToSenderSourceProbe.OverloadIndex(
            reader,
            type,
            methodHandle,
            nameof(PrinterExactFixture));
        string signature = ReturnToSenderSourceProbe.UniqueTargetSignature(
            reader,
            type,
            nameof(PrinterExactFixture),
            methodHandle)
            ?? throw new InvalidOperationException("Fixture signature was unavailable.");
        var member = new ReturnToSenderSourceMember(
            typeof(AuthoredSourceOracleManifestTests).FullName!,
            nameof(PrinterExactFixture),
            overload,
            signature,
            "PrinterExactFixture.cs",
            expected,
            MetadataTokens.GetToken(methodHandle),
            reader.GetGuid(reader.GetModuleDefinition().Mvid),
            PrinterBody: printerBody);
        var index = ReturnToSenderSourceIndex.FromCorrelatedMembers([member], reader);

        ReturnToSenderSourceProbeResult result = Assert.Single(
            ReturnToSenderSourceProbe.EvaluateWithIndex(
                assemblyPath,
                [new ReturnToSender.RequestedTarget(
                    member.Type,
                    member.Method,
                    member.Overload,
                    member.Signature)],
                index));

        Assert.Equal(ReturnToSenderSourceOutcome.ValidMatch, result.Outcome);
        Assert.Equal(PrinterExactOutcome.Exact, result.PrinterExact);
    }

    [Fact]
    public void Manifest_RequiresValidAndCorrectForEveryRegisteredFile()
    {
        var exact = Row("Exact.cs", 1, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var notOptedIn = Row("Correct.cs", 2, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Different);
        var manifest = Manifest(
            File(exact.Record, requirePrinterExact: true),
            File(notOptedIn.Record, requirePrinterExact: false));

        var report = AuthoredSourceOracleManifest.Evaluate(
            manifest,
            [exact, notOptedIn]);

        Assert.True(report.Passed);
        Assert.Equal(2, report.FilesValid);
        Assert.Equal(2, report.FilesCorrect);
        Assert.Equal(1, report.PrinterExactRequired);
        Assert.Equal(1, report.PrinterExactPassing);
    }

    [Fact]
    public void Manifest_MissingExpectedMemberFailsInsteadOfShorteningDenominator()
    {
        var first = Row("Oracle.cs", 1, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var second = Row("Oracle.cs", 2, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var file = File(first.Record, requirePrinterExact: true) with
        {
            Members = [Member(first.Record), Member(second.Record)],
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(file),
            [first]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("expected member", StringComparison.Ordinal)
            && failure.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_StaleExpectedSetFailsWhenCorpusAddsMember()
    {
        var first = Row("Oracle.cs", 1, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);
        var second = Row("Oracle.cs", 2, ReturnToSenderSourceOutcome.ValidMatch, PrinterExactOutcome.Exact);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(first.Record, requirePrinterExact: true)),
            [first, second]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("absent from the expected set", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_PrinterExactCannotPassAFormattingOnlyDifference()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Different);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: true)),
            [row]);

        Assert.False(report.Passed);
        Assert.Equal(1, report.FilesValid);
        Assert.Equal(1, report.FilesCorrect);
        Assert.Equal(0, report.PrinterExactPassing);
    }

    [Fact]
    public void Manifest_PrinterExactRequiresVersionedCapturedText()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidMatch,
            PrinterExactOutcome.Exact);
        row = row with
        {
            Record = row.Record with { PrinterBodyVersion = null },
        };

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: true)),
            [row]);

        Assert.False(report.Passed);
        Assert.Contains(report.Failures, failure =>
            failure.Contains("lack Printer body version", StringComparison.Ordinal));
    }

    [Fact]
    public void Manifest_CorrectGateRejectsValidDifferentBeforePrinterJudgment()
    {
        var row = Row(
            "Oracle.cs",
            1,
            ReturnToSenderSourceOutcome.ValidDifferent,
            PrinterExactOutcome.Different);

        var report = AuthoredSourceOracleManifest.Evaluate(
            Manifest(File(row.Record, requirePrinterExact: false)),
            [row]);

        Assert.False(report.Passed);
        Assert.Equal(1, report.FilesValid);
        Assert.Equal(0, report.FilesCorrect);
    }

    [Fact]
    public void Benchmark_TextAndJsonAgreeOnSourceOracleFailure()
    {
        string assembly = typeof(ILInspector.CSharp.CSharpFormatter).Assembly.Location;
        string corpus = AuthoredCorpusTestData.WriteCorrelatedCorpus(assembly);
        string manifestPath = Path.Combine(
            Path.GetTempPath(),
            $"source-oracle-{Guid.NewGuid():N}.json");

        try
        {
            var records = AuthoredCorpusBenchmark.ReadCorpus(corpus, out int malformed);
            Assert.Equal(0, malformed);
            var manifest = Manifest(File(Assert.Single(records), requirePrinterExact: false));
            System.IO.File.WriteAllText(
                manifestPath,
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    }));

            using var text = new StringWriter();
            int textExit = AuthoredCorpusBenchmark.Run(
                [assembly],
                corpus,
                json: false,
                sourceOracleManifestPath: manifestPath,
                output: text);

            using var json = new StringWriter();
            int jsonExit = AuthoredCorpusBenchmark.Run(
                [assembly],
                corpus,
                json: true,
                sourceOracleManifestPath: manifestPath,
                output: json);
            AuthoredCorpusBenchmark.Report payload =
                AuthoredCorpusHistoryStore.ParseBenchmarkReport(json.ToString());

            Assert.Equal(1, textExit);
            Assert.Equal(textExit, jsonExit);
            Assert.Contains("Source-oracle files:", text.ToString());
            Assert.Contains("BLOCKER", text.ToString());
            Assert.NotNull(payload.SourceOracleManifest);
            Assert.Equal(1, payload.SourceOracleManifest.FilesRegistered);
            Assert.False(payload.SourceOracleManifest.Passed);
            Assert.Contains(
                $": {payload.SourceOracleManifest.FilesValid} / "
                    + $"{payload.SourceOracleManifest.FilesRegistered}",
                text.ToString());
            Assert.Contains(
                $": {payload.SourceOracleManifest.FilesCorrect} / "
                    + $"{payload.SourceOracleManifest.FilesRegistered}",
                text.ToString());
        }

        finally
        {
            System.IO.File.Delete(corpus);
            System.IO.File.Delete(manifestPath);
        }
    }

    [Theory]
    [InlineData("""{"version":1,"version":1,"printerComparisonVersion":1,"files":[]}""")]
    [InlineData("""{"version":1,"printerComparisonVersion":1,"unknown":true,"files":[]}""")]
    public void ManifestReader_RejectsUnknownAndDuplicateProperties(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"source-oracle-invalid-{Guid.NewGuid():N}.json");
        try
        {
            System.IO.File.WriteAllText(path, json);

            Assert.False(AuthoredSourceOracleManifest.TryRead(
                path,
                out _,
                out string? error));
            Assert.Contains("not valid JSON", error, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    static AuthoredSourceOracleManifest.Document Manifest(
        params AuthoredSourceOracleManifest.FileEntry[] files)
        => new(
            AuthoredSourceOracleManifest.Version,
            AuthoredSourceOracleManifest.PrinterComparisonVersion,
            files);

    static AuthoredSourceOracleManifest.FileEntry File(
        AuthoredSourceHarvest.CorpusRecord record,
        bool requirePrinterExact)
        => new(
            record.SourceUrl!,
            record.ChecksumAlgorithm!,
            record.Checksum!,
            AuthoredSourceOracleManifest.DefaultPrinterProfile,
            requirePrinterExact,
            [Member(record)]);

    static AuthoredSourceOracleManifest.MemberEntry Member(
        AuthoredSourceHarvest.CorpusRecord record)
        => new(
            record.Assembly,
            record.AssemblyVersion,
            record.ModuleVersionId!.Value,
            record.MetadataToken,
            record.Type,
            record.Method,
            record.Overload);

    static AuthoredSourceOracleManifest.EvaluatedRow Row(
        string path,
        int token,
        ReturnToSenderSourceOutcome outcome,
        PrinterExactOutcome printerExact)
    {
        var record = new AuthoredSourceHarvest.CorpusRecord(
            Assembly: "Fixture",
            AssemblyVersion: "1.0.0.0",
            Tfm: "net10.0",
            Type: "Fixture.Type",
            Method: $"Method{token}",
            Overload: 0,
            Signature: null,
            MetadataToken: token,
            ParameterCount: 0,
            IlSize: 1,
            SourceUrl: $"https://raw.githubusercontent.com/example/repo/0123456789abcdef/{path}",
            ChecksumAlgorithm: "SHA256",
            Checksum: new string('A', 64),
            AuthoredBody: "return;",
            ModuleVersionId: new Guid("11111111-2222-3333-4444-555555555555"),
            PrinterBody: "return;",
            PrinterBodyVersion: AuthoredSourceOracleManifest.PrinterComparisonVersion);
        var result = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget(record.Type, record.Method, record.Overload),
            outcome,
            FidelityCheck.CompileBackStatus.Exact,
            "fixture",
            Detail: null,
            SourcePath: record.SourceUrl,
            ExpectedBody: record.AuthoredBody,
            ActualBody: record.PrinterBody,
            PrinterExact: printerExact);
        return new(record, result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int PrinterExactFixture(int value)
    {
        return value + 1;
    }
}
