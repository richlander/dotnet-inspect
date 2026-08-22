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

    /// <summary>
    /// Inbound call-site table. Multi-package call-graph demos include this as a
    /// companion section: CLI <c>--caller-package</c> / caller-scope encoding
    /// implies <c>-S Callers</c> (see schema-query), and the closed preset names
    /// both sections rather than under-declaring the runtime set.
    /// </summary>
    public const string Callers = "Callers";

    static readonly HashSet<string> s_known = new(StringComparer.Ordinal)
    {
        Methods,
        CallGraph,
        Callers,
    };

    /// <summary>
    /// Sections the CLI/engine run must request for a home demo bound to
    /// <paramref name="boundSection"/>. Call Graph presets expand to Call Graph
    /// + Callers so the closed preset matches the multi-package CLI companion
    /// rule instead of silently gaining a second section at run time.
    /// </summary>
    /// <param name="singleSectionFormat">
    /// When true (table/tsv/jsonl), multi-package Call Graph demos select
    /// <see cref="Callers"/> only. MemberCommand re-adds Callers whenever
    /// caller-scope packages are set; starting from Call Graph alone therefore
    /// becomes {Call Graph, Callers} and the tabular path falls back to a member
    /// inventory. Callers alone survives that re-add as a true one-section
    /// projection. Standalone mermaid is not this path — it keeps Call Graph
    /// only (validated before the Callers inject) and is resolved by the CLI
    /// runner.
    /// </param>
    public static IReadOnlyList<string> ExpandRunSections(
        string boundSection,
        bool singleSectionFormat = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boundSection);
        if (string.Equals(boundSection, CallGraph, StringComparison.Ordinal))
            return singleSectionFormat ? [Callers] : [CallGraph, Callers];
        return [boundSection];
    }

    /// <summary>Section ids home demos may bind today.</summary>
    public static IReadOnlyCollection<string> Known { get; } = Array.AsReadOnly(s_known.ToArray());

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
