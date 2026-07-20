using System.Text;
using System.Text.Json;

using DotnetInspector.Fixtures;

namespace ILInspector.DecompilerHarness;

internal enum FixtureSourcePopulation
{
    Built,
    Generated,
}

internal sealed record DecompilerFixtureSourceRow(
    FixtureSourcePopulation Population,
    string Id,
    FixtureSourceApplicability Applicability,
    FixtureSourceInventoryStatus Status,
    int DocumentCount,
    int TargetCount,
    string? Reason);

internal sealed record DecompilerFixtureSourceReport(
    IReadOnlyList<DecompilerFixtureSourceRow> Rows)
{
    public int SourceDiscovered => Rows.Count(row =>
        row.Status == FixtureSourceInventoryStatus.SourceDiscovered);

    public int Unresolved => Rows.Count(row =>
        row.Status is FixtureSourceInventoryStatus.Unclassified
            or FixtureSourceInventoryStatus.SourceMissing);
}

internal static class DecompilerFixtureSourceInventory
{
    public static DecompilerFixtureSourceReport Create()
    {
        var built = FixtureSourceInventory
            .Create(FixtureCatalog.SelectByTag("decompiler"))
            .Fixtures
            .Select(row => new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Built,
                row.FixtureId,
                row.Applicability,
                row.Status,
                row.DiscoveredDocumentCount,
                0,
                row.Reason));

        var generated = GeneratedFixtureCatalog.Catalog.Select(fixture =>
            new DecompilerFixtureSourceRow(
                FixtureSourcePopulation.Generated,
                fixture.Id,
                FixtureSourceApplicability.Required,
                string.IsNullOrWhiteSpace(fixture.Source)
                    ? FixtureSourceInventoryStatus.SourceMissing
                    : FixtureSourceInventoryStatus.SourceDiscovered,
                string.IsNullOrWhiteSpace(fixture.Source) ? 0 : 1,
                fixture.Targets.Count,
                string.IsNullOrWhiteSpace(fixture.Source)
                    ? "The generated fixture has no retained source."
                    : null));

        return new([.. built, .. generated]);
    }

    public static string Format(DecompilerFixtureSourceReport report, bool json)
    {
        if (json)
        {
            return JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });
        }

        var output = new StringBuilder();
        output.AppendLine("# DECOMPILER FIXTURE SOURCE INVENTORY");
        output.AppendLine();
        output.AppendLine("| Population | Entries | Source discovered | Unresolved |");
        output.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var population in Enum.GetValues<FixtureSourcePopulation>())
        {
            var rows = report.Rows.Where(row => row.Population == population).ToArray();
            output.AppendLine($"| {population} | {rows.Length} | "
                + $"{rows.Count(row => row.Status == FixtureSourceInventoryStatus.SourceDiscovered)} | "
                + $"{rows.Count(row => row.Status is FixtureSourceInventoryStatus.Unclassified or FixtureSourceInventoryStatus.SourceMissing)} |");
        }
        return output.ToString();
    }

}
