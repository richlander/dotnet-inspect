using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

using ILInspector.Analysis.StructuralCloneFixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class StructuralCloneCorpusTests
{
    [Fact]
    public void CommittedRelationshipCorpus_GradesPublicProductComparator()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();

        StructuralCloneCorpusReport report = StructuralCloneCorpus.Run(
            typeof(StructuralCloneFixture).Assembly.Location,
            corpus);

        Assert.True(
            report.Success,
            StructuralCloneCorpus.Format(report));
        Assert.Equal(6, report.Total);
    }

    [Fact]
    public void CommittedRelationshipCorpus_CoversFixtureInventory()
    {
        StructuralCloneCorpusDocument corpus = LoadCorpus();
        string[] corpusMethods =
        [
            .. corpus.Cases
                .SelectMany(static item =>
                    new[] { item.Left.Method, item.Right.Method })
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        string[] fixtureMethods =
        [
            .. typeof(StructuralCloneFixture)
                .GetMethods(
                    BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly)
                .Select(static method => method.Name)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(fixtureMethods, corpusMethods);
    }

    [Fact]
    public void Load_RejectsRelationOnUnsupportedCase()
    {
        const string Invalid =
            """
            {
              "schemaVersion": 1,
              "cases": [{
                "id": "invalid",
                "left": { "type": "T", "method": "A" },
                "right": { "type": "T", "method": "B" },
                "expectedDisposition": "Unsupported",
                "expectedRelation": "Different",
                "difficulty": "banal",
                "intent": "unsupported-boundary",
                "actionability": "none",
                "tags": []
              }]
            }
            """;

        Assert.Throws<InvalidDataException>(
            () => StructuralCloneCorpus.Load(Invalid));
    }

    [Theory]
    [MemberData(nameof(MalformedLedgers))]
    public void Load_RejectsIncompleteOrPermissiveJson(string json)
        => Assert.Throws<JsonException>(
            () => StructuralCloneCorpus.Load(json));

    public static TheoryData<string> MalformedLedgers =>
        new()
        {
            {
                """
                {
                  "schemaVersion": 1,
                  "cases": [{
                    "id": "missing-disposition",
                    "left": { "type": "T", "method": "A" },
                    "right": { "type": "T", "method": "B" },
                    "expectedRelation": "Exact",
                    "difficulty": "banal",
                    "intent": "authored-duplicate",
                    "actionability": "actionable",
                    "tags": []
                  }]
                }
                """
            },
            {
                """
                {
                  "schemaVersion": 1,
                  "unknown": true,
                  "cases": [{
                    "id": "unknown-property",
                    "left": { "type": "T", "method": "A" },
                    "right": { "type": "T", "method": "B" },
                    "expectedDisposition": "Completed",
                    "expectedRelation": "Exact",
                    "difficulty": "banal",
                    "intent": "authored-duplicate",
                    "actionability": "actionable",
                    "tags": []
                  }]
                }
                """
            },
            {
                """
                {
                  "schemaVersion": 1,
                  "cases": [{
                    "id": "integer-enum",
                    "left": { "type": "T", "method": "A" },
                    "right": { "type": "T", "method": "B" },
                    "expectedDisposition": 0,
                    "expectedRelation": "Exact",
                    "difficulty": "banal",
                    "intent": "authored-duplicate",
                    "actionability": "actionable",
                    "tags": []
                  }]
                }
                """
            },
        };

    [Fact]
    public async Task Command_RejectsMissingRelationshipLedgerValue()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(
            typeof(StructuralCloneCorpus).Assembly.Location);
        start.ArgumentList.Add("--clone-corpus");
        start.ArgumentList.Add(
            typeof(StructuralCloneFixture).Assembly.Location);
        start.ArgumentList.Add("--relationship-ledger");
        start.ArgumentList.Add("--json");

        using Process process = Process.Start(start)!;
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        Task<string> standardError =
            process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> standardOutput =
            process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        Assert.Equal(2, process.ExitCode);
        Assert.Contains(
            "--relationship-ledger requires a file path.",
            await standardError);
        Assert.Equal("", await standardOutput);
    }

    static StructuralCloneCorpusDocument LoadCorpus()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "corpus",
            "structural-clone-relationships.json");
        return StructuralCloneCorpus.Load(File.ReadAllText(path));
    }
}
