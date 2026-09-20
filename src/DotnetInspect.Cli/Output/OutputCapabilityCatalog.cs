using System.Collections.Immutable;

namespace DotnetInspect.Cli.Output;

public enum OutputMode
{
    Markdown,
    PlainText,
    Json,
    Table,
    Tsv,
    Jsonl,
    Tree,
    Mermaid,
}

public sealed record SectionOutputCapabilities(
    ImmutableArray<OutputMode> Formats,
    ImmutableHashSet<OutputMode> ExclusiveFormats)
{
    public static SectionOutputCapabilities Create(
        IEnumerable<OutputMode> formats,
        IEnumerable<OutputMode>? exclusiveFormats = null) =>
        new(
            [.. formats.Distinct()],
            exclusiveFormats?.ToImmutableHashSet()
                ?? ImmutableHashSet<OutputMode>.Empty);
}

public sealed class OutputCapabilityCatalog
{
    internal static ImmutableArray<OutputMode> StandardSectionFormats { get; } =
    [
        OutputMode.Markdown,
        OutputMode.PlainText,
        OutputMode.Json,
        OutputMode.Table,
        OutputMode.Tsv,
        OutputMode.Jsonl,
    ];

    internal static ImmutableArray<OutputMode> FormatOrder { get; } =
    [
        .. StandardSectionFormats,
        OutputMode.Tree,
        OutputMode.Mermaid,
    ];

    private readonly ImmutableDictionary<string, SectionOutputCapabilities>
        _sections;
    private readonly ImmutableArray<ImmutableHashSet<string>>
        _homogeneousRowFamilies;

    public OutputCapabilityCatalog(
        IReadOnlyDictionary<string, SectionOutputCapabilities> sections,
        IEnumerable<IEnumerable<string>>? homogeneousRowFamilies = null)
    {
        _sections = sections.ToImmutableDictionary(
            StringComparer.OrdinalIgnoreCase);
        _homogeneousRowFamilies =
        [
            .. homogeneousRowFamilies?
                .Select(family => family.ToImmutableHashSet(
                    StringComparer.OrdinalIgnoreCase))
                ?? [],
        ];
    }

    public ImmutableArray<OutputMode> FormatsForSection(string section) =>
        _sections.TryGetValue(section, out SectionOutputCapabilities? capability)
            ? [.. FormatOrder.Where(capability.Formats.Contains)]
            : [];

    public ImmutableArray<OutputMode> FormatsForSelection(
        IReadOnlyCollection<string> sections) =>
        [.. FormatOrder.Where(format => Supports(format, sections))];

    public bool Supports(
        OutputMode format,
        IReadOnlyCollection<string>? sections)
    {
        if (sections is not { Count: > 0 })
            return false;

        foreach (string section in sections)
        {
            if (!_sections.TryGetValue(
                    section,
                    out SectionOutputCapabilities? capability)
                || !capability.Formats.Contains(format))
            {
                return false;
            }
        }

        if (sections.Count == 1)
            return true;

        if (sections.Any(section =>
                _sections[section].ExclusiveFormats.Contains(format)))
        {
            return false;
        }

        return format switch
        {
            OutputMode.Tree or OutputMode.Mermaid => false,
            OutputMode.Table or OutputMode.Tsv or OutputMode.Jsonl =>
                _homogeneousRowFamilies.Any(family =>
                    sections.All(family.Contains)),
            _ => true,
        };
    }

    public static string CliOption(OutputMode format) =>
        format switch
        {
            OutputMode.Markdown => "--markdown",
            OutputMode.PlainText => "--plaintext",
            OutputMode.Json => "--json",
            OutputMode.Table => "--table",
            OutputMode.Tsv => "--tsv",
            OutputMode.Jsonl => "--jsonl",
            OutputMode.Tree => "--tree",
            OutputMode.Mermaid => "--mermaid",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Unknown output mode."),
        };
}
