using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;

using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

static class AuthoredCorpusTestData
{
    public static string CorrelatedRow(string assemblyPath)
        => Correlate(ReadFixture("one-row-authored-corpus.jsonl"), assemblyPath);

    public static string CorrelatedRows(string assemblyPath)
        => string.Join(
            Environment.NewLine,
            File.ReadLines(FixturePath("two-row-authored-corpus.jsonl"))
                .Select(row => Correlate(row, assemblyPath)))
            + Environment.NewLine;

    public static string WriteCorrelatedCorpus(string assemblyPath)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"correlated-corpus-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, CorrelatedRow(assemblyPath) + Environment.NewLine);
        return path;
    }

    static string Correlate(string rowJson, string assemblyPath)
    {
        var row = JsonNode.Parse(rowJson)?.AsObject()
            ?? throw new InvalidDataException("The authored-corpus test row is not a JSON object.");
        var identity = AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath);
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var reader = pe.GetMetadataReader();
        row["assembly"] = identity.Name;
        row["assemblyVersion"] = identity.Version;
        row["moduleVersionId"] = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return row.ToJsonString();
    }

    static string ReadFixture(string fileName)
        => File.ReadAllText(FixturePath(fileName)).TrimEnd();

    static string FixturePath(string fileName)
        => Path.Combine(
            AuthoredCorpusRatchetTests.FindRepositoryRoot(),
            "tools",
            "DecompilerHarness",
            "corpus",
            fileName);
}
