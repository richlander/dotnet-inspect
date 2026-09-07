using ILInspector.Decompiler;

namespace DotnetInspector.Output;

public sealed record BodyShapeSummary(string Kind, string Match, int Count)
{
    internal static List<BodyShapeSummary> FromMatches(IEnumerable<BodyShapeMatch> matches)
        => matches
            .GroupBy(match => (match.Kind, match.Text))
            .Select(group => new BodyShapeSummary(group.Key.Kind, group.Key.Text, group.Count()))
            .ToList();
}
