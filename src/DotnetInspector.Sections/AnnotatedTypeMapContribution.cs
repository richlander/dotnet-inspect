namespace DotnetInspector.Sections;

/// <summary>
/// The concrete result type of every Annotated Type Map adapter query: one
/// family's contribution for one planned request. Planning binds adapter
/// queries of this type; general query infrastructure never interprets it.
/// The contribution's settled content (Member-keyed facts or findings and
/// their outcomes) belongs to the map's focused generation owner, adoption
/// step 3 of docs/design/annotated-type-map-planning.md.
/// </summary>
public sealed class AnnotatedTypeMapContribution
{
    public AnnotatedTypeMapContribution(AnnotatedTypeMapFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);
        Family = family;
    }

    /// <summary>The exact family this contribution is for: the trail from a result back to its plan.</summary>
    public AnnotatedTypeMapFamily Family { get; }
}
