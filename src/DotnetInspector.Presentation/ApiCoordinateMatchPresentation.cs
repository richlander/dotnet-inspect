using Markout;
using Markout.Formatting;

namespace DotnetInspector.Presentation;

/// <summary>Shared Markout lowering of a completed API match; never performs inspection.</summary>
public static class ApiCoordinateMatchPresentation
{
    public static void Render(
        ApiCoordinateMatchContent content,
        TextWriter writer,
        MarkoutWriterOptions? options = null) =>
        Render(content, writer, new MarkdownFormatter(), options);

    public static void Render(
        ApiCoordinateMatchContent content,
        TextWriter writer,
        IMarkoutFormatter formatter,
        MarkoutWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(formatter);
        var document = new MarkoutWriter(writer, formatter, options ?? new());
        document.WriteHeading(1, $"API match: {content.Status}");
        document.WriteParagraph(content.Summary.ToString());
        var coordinates = new List<string[]>();
        Add("Source", content.Source, content.Before);
        if (content.DestinationEntry is { } entry
            && (content.Destination is not { } destination
                || entry.Asset?.ToString() != destination.Asset?.ToString()))
            Add("Entry", entry, content.After);
        if (content.Destination is not null || content.DestinationEntry is null)
            Add("Destination", content.Destination, content.After);
        document.WriteTable(
            ["Role", "Package", "TFM", "Library", "API"],
            ["role", "package", "tfm", "library", "api"],
            coordinates);

        if (!content.ForwardingHops.IsEmpty)
        {
            document.WriteParagraph("Forwarding: "
                + string.Join("; ", content.ForwardingHops.Select(hop =>
                    $"{hop.Source.Assembly.Name} -> {hop.Target.Name}")) + ".");
        }
        if (content.Status is ApiCoordinateMatchStatus.Ambiguous or ApiCoordinateMatchStatus.Refused
            && !content.Candidates.IsEmpty)
        {
            const int candidateLimit = 10;
            document.WriteHeading(2, "Candidates");
            document.WriteTable(
                ["Library", "API", "Signature", "Token"],
                ["library", "api", "signature", "token"],
                content.Candidates.Take(candidateLimit).Select(candidate => new[]
                {
                    candidate.Asset?.ToString() ?? candidate.Assembly.Name.ToString(),
                    Api(candidate),
                    candidate.Signature?.ToString() ?? "",
                    candidate.MetadataToken is { } token ? $"0x{token:x8}" : "",
                }).ToList());
            if (content.Candidates.Length > candidateLimit)
                document.WriteParagraph($"Showing {candidateLimit} of {content.Candidates.Length} candidates; --format json includes all candidate evidence.");
        }
        else if (content.Status == ApiCoordinateMatchStatus.Absent && !content.Candidates.IsEmpty)
        {
            document.WriteParagraph($"Evaluated {content.Candidates.Length} candidate declarations; --format json includes their coordinates.");
        }
        if (content.Status is not (ApiCoordinateMatchStatus.Exact or ApiCoordinateMatchStatus.Absent)
            && !content.Stages.IsEmpty)
        {
            ApiCoordinateMatchStageEvidence last = content.Stages[^1];
            document.WriteParagraph($"Stage: {last.Stage}; outcome: {last.Outcome}"
                + (last.Reason is null ? "." : $"; reason: {last.Reason}."));
            if (last.Target is { } target)
                document.WriteParagraph($"Target: {target.Name} {target.Version}.");
        }
        document.Flush();

        void Add(string role, ApiCoordinateMatchLocation? location, ApiCoordinateMatchEndpoint fallback)
        {
            ApiCoordinateMatchEndpoint package = location?.Package ?? fallback;
            coordinates.Add(
            [
                role,
                $"{package.PackageId}@{package.Version}",
                package.TargetFramework?.ToString() ?? "",
                location?.Asset?.ToString() ?? "",
                location is null ? "" : Api(location),
            ]);
        }
    }

    static string Api(ApiCoordinateMatchLocation location) =>
        location.Member is { } member
            ? $"{location.Type}.{member}"
            : location.Type?.ToString() ?? "";
}
