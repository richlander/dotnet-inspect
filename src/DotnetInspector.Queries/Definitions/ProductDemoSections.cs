namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Closed set of product section display names home demos may select until the
/// view-facet registry mints stable facet ids (see workspace-definitions open
/// question). Values match <c>SectionNames</c> in the CLI section pipeline —
/// the shipping <c>-S</c> token space — and are gated so a demo cannot register
/// an unknown or empty section.
/// </summary>
public static class ProductDemoSections
{
    /// <summary>Type member inventory — the natural browse section for API tours.</summary>
    public const string Methods = "Methods";

    /// <summary>Bidirectional member call graph (canonical product id).</summary>
    public const string CallGraph = "Call Graph";

    static readonly HashSet<string> s_known = new(StringComparer.Ordinal)
    {
        Methods,
        CallGraph,
    };

    /// <summary>Section ids home demos may bind today.</summary>
    public static IReadOnlyCollection<string> Known { get; } = s_known.ToArray();

    /// <summary>Returns whether <paramref name="sectionId"/> is in the home-demo allow list.</summary>
    public static bool IsKnown(string? sectionId) =>
        sectionId is not null && s_known.Contains(sectionId);

    /// <summary>
    /// Fails when a resolved home demo omits <see cref="ViewDefinition.Section"/>
    /// or names a section outside <see cref="Known"/>.
    /// </summary>
    public static void EnsureHomeDemoBinding(ResolvedScenario resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        var section = resolved.View?.Section;
        if (string.IsNullOrWhiteSpace(section))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{resolved.ScenarioId}' must bind View.Section to an existing product section.");
        }

        if (!IsKnown(section))
        {
            throw new InspectionDefinitionException(
                $"Home demo '{resolved.ScenarioId}' binds unknown section '{section}'. "
                + "Home demos may only select ProductDemoSections.Known ids until the view-facet registry lands.");
        }
    }
}
