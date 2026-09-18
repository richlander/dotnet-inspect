using System.Reflection;

namespace ILInspector.Research.Tests;

public sealed class AnnotatedSourceFindingProvenanceTests
{
    const string ResourceName =
        "ILInspector.Research.Tests.AnnotatedSourceFindingProvenance.md";
    const string MatrixStart =
        "<!-- descriptor-matrix:start -->";
    const string MatrixEnd =
        "<!-- descriptor-matrix:end -->";

    [Fact]
    public void DescriptorMatrix_EqualsProductionAnnotatedSourceProfiles()
    {
        string document = LoadDocument();
        int start = document.IndexOf(MatrixStart, StringComparison.Ordinal);
        int end = document.IndexOf(MatrixEnd, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing matrix marker '{MatrixStart}'.");
        Assert.True(end > start, $"Missing matrix marker '{MatrixEnd}'.");

        string[] table = document[(start + MatrixStart.Length)..end]
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith('|'))
            .ToArray();
        Assert.Equal(
            "| Descriptor | Profile | Category | Classification | Producer |",
            table[0]);
        Assert.Equal("| --- | --- | --- | --- | --- |", table[1]);
        string[] rows = table[2..];
        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            row => Assert.StartsWith("| `", row, StringComparison.Ordinal));

        string[] documented =
        [
            .. rows.Select(DescriptorId),
        ];
        Assert.Equal(
            documented.Length,
            documented.Distinct(StringComparer.Ordinal).Count());

        string[] declared =
        [
            .. ResearchFactRegistry.Default.DescriptorIds
                .Concat(ResearchFactRegistry.CallRelationships.DescriptorIds)
                .Order(StringComparer.Ordinal),
        ];
        Assert.NotEmpty(declared);
        Assert.DoesNotContain(
            declared,
            descriptor => descriptor.Contains('*', StringComparison.Ordinal));
        Assert.Equal(
            declared,
            documented.Order(StringComparer.Ordinal));
    }

    static string DescriptorId(string row)
    {
        int end = row.IndexOf('`', 3);
        Assert.True(end > 3, $"Malformed descriptor matrix row: {row}");
        return row[3..end];
    }

    static string LoadDocument()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The provenance matrix resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
