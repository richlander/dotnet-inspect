using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Output;

internal enum SectionNativeOutput
{
    TabularRows,
    HierarchyTree,
    TextPayload,
}

internal static class SectionShapeOutputPolicy
{
    public static SectionNativeOutput? ResolveNativeOutput(
        bool selectionExplicit,
        IReadOnlyCollection<string>? sections,
        IReadOnlyDictionary<string, SectionShape> shapes,
        bool hasExplicitOutputIntent)
    {
        if (!selectionExplicit
            || hasExplicitOutputIntent
            || sections is not { Count: 1 })
        {
            return null;
        }

        string section = sections.Single();
        if (!shapes.TryGetValue(section, out SectionShape shape))
            return null;

        return shape switch
        {
            SectionShape.Table => SectionNativeOutput.TabularRows,
            SectionShape.Hierarchy => SectionNativeOutput.HierarchyTree,
            SectionShape.Text => SectionNativeOutput.TextPayload,
            _ => throw new ArgumentOutOfRangeException(
                nameof(shapes),
                shape,
                "Unknown section shape."),
        };
    }

    public static string? ValidateScalarTerminal(
        IReadOnlyCollection<string>? sections,
        IReadOnlyDictionary<string, SectionCardinalityDeclaration> cardinalities,
        SectionTerminalCapability? terminal,
        bool discovery)
    {
        if (terminal is null
            || discovery
            || sections is not { Count: 1 })
        {
            return null;
        }

        string section = sections.Single();
        if (!cardinalities.TryGetValue(
                section,
                out SectionCardinalityDeclaration? declaration)
            || declaration.Kind != SectionCardinalityKind.Scalar)
        {
            return null;
        }

        string option = terminal switch
        {
            SectionTerminalCapability.Count => "--count",
            SectionTerminalCapability.Rows => "--rows",
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "Unknown section terminal."),
        };
        return $"Section '{section}' is scalar and does not support "
            + $"{option}. Select an inventory section.";
    }

    public static string DescribeSection(
        string section,
        IReadOnlyDictionary<string, SectionShape> shapes) =>
        shapes.TryGetValue(section, out SectionShape shape)
            ? $"'{section}' ({shape})"
            : $"'{section}'";

    public static string DescribeFormats(
        string section,
        OutputCapabilityCatalog capabilities)
    {
        var formats = capabilities.FormatsForSection(section);
        return formats.IsEmpty
            ? "--markdown, --json"
            : string.Join(
                ", ",
                formats.Select(OutputCapabilityCatalog.CliOption));
    }
}
