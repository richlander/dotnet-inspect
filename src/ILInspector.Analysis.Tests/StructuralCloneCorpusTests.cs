using System.Reflection;

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
        Assert.Equal(5, report.Total);
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

    static StructuralCloneCorpusDocument LoadCorpus()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "corpus",
            "structural-clone-relationships.json");
        return StructuralCloneCorpus.Load(File.ReadAllText(path));
    }
}
