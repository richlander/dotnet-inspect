using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

public sealed record SectionOutputCapabilities(
    ImmutableArray<DiscoveryOutputMode> Formats,
    ImmutableHashSet<DiscoveryOutputMode> ExclusiveFormats)
{
    public static SectionOutputCapabilities Create(
        IEnumerable<DiscoveryOutputMode> formats,
        IEnumerable<DiscoveryOutputMode>? exclusiveFormats = null) =>
        new(
            [.. formats.Distinct()],
            exclusiveFormats?.ToImmutableHashSet()
                ?? ImmutableHashSet<DiscoveryOutputMode>.Empty);
}

public sealed class OutputCapabilityCatalog
{
    internal static ImmutableArray<DiscoveryOutputMode>
        StandardSectionFormats { get; } =
    [
        DiscoveryOutputMode.Markdown,
        DiscoveryOutputMode.PlainText,
        DiscoveryOutputMode.Json,
        DiscoveryOutputMode.Table,
        DiscoveryOutputMode.Tsv,
        DiscoveryOutputMode.Jsonl,
    ];

    internal static ImmutableArray<DiscoveryOutputMode> FormatOrder { get; } =
    [
        .. StandardSectionFormats,
        DiscoveryOutputMode.Tree,
        DiscoveryOutputMode.Mermaid,
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

    public ImmutableArray<DiscoveryOutputMode> FormatsForSection(
        string section) =>
        _sections.TryGetValue(section, out SectionOutputCapabilities? capability)
            ? [.. FormatOrder.Where(capability.Formats.Contains)]
            : [];

    public ImmutableArray<DiscoveryOutputMode> FormatsForSelection(
        IReadOnlyCollection<string> sections) =>
        [.. FormatOrder.Where(format => Supports(format, sections))];

    public bool Supports(
        DiscoveryOutputMode format,
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
            DiscoveryOutputMode.Tree or DiscoveryOutputMode.Mermaid => false,
            DiscoveryOutputMode.Table
                or DiscoveryOutputMode.Tsv
                or DiscoveryOutputMode.Jsonl =>
                _homogeneousRowFamilies.Any(family =>
                    sections.All(family.Contains)),
            _ => true,
        };
    }

    public static string CliOption(DiscoveryOutputMode format) =>
        format switch
        {
            DiscoveryOutputMode.Markdown => "--format markdown",
            DiscoveryOutputMode.PlainText => "--format plaintext",
            DiscoveryOutputMode.Json => "--format json",
            DiscoveryOutputMode.Table => "--format table",
            DiscoveryOutputMode.Tsv => "--format tsv",
            DiscoveryOutputMode.Jsonl => "--format jsonl",
            DiscoveryOutputMode.Tree => "--tree",
            DiscoveryOutputMode.Mermaid => "--format mermaid",
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Unknown output mode."),
        };
}
