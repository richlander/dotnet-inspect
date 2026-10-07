using DotnetInspect.Cli.Options;
using DotnetInspector.Presentation;

namespace DotnetInspect.Cli.Planning;

internal abstract record TypeCommandPlan
{
    private TypeCommandPlan()
    {
    }

    internal sealed record Standard : TypeCommandPlan;

    internal sealed record ExactTypeOverview(
        TypeOverviewHierarchyPresentationFormat Format)
        : TypeCommandPlan;
}

internal abstract record TypeCommandPlanningResult
{
    private TypeCommandPlanningResult()
    {
    }

    internal sealed record Planned(TypeCommandPlan Plan)
        : TypeCommandPlanningResult;

    internal sealed record Rejected(string Error)
        : TypeCommandPlanningResult;
}

internal static class TypeCommandPlanner
{
    internal const string StandaloneMermaidError =
        "--mermaid requires a standalone exact Type hierarchy without another operation, filter, window, verbosity, or format.";

    internal const string ExactTypeMermaidError =
        "--mermaid requires one exact Type; listing, glob, and namespace/prefix targets are not supported.";

    internal static TypeCommandPlanningResult Plan(
        TypeOptions options,
        ResolvedMemberInspectionPlan inspectionPlan)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inspectionPlan);

        bool mermaid = options.MermaidExplicitlySet;
        bool competing = HasCompetingDemand(options);
        if (mermaid && (options.Tree || competing))
            return new TypeCommandPlanningResult.Rejected(
                StandaloneMermaidError);

        // Exact Type execution selects the member catalog; Type listings
        // select the Type catalog.
        bool exactType =
            inspectionPlan.Selection.Catalog
                == InspectionCatalogIdentity.ApiMember
            && !string.IsNullOrWhiteSpace(options.TypeName);
        if (!exactType)
        {
            return mermaid
                ? new TypeCommandPlanningResult.Rejected(
                    ExactTypeMermaidError)
                : new TypeCommandPlanningResult.Planned(
                    new TypeCommandPlan.Standard());
        }

        if (competing
            || (!mermaid
                && !options.Tree
                && options.FormatExplicitlySet))
        {
            return new TypeCommandPlanningResult.Planned(
                new TypeCommandPlan.Standard());
        }

        return new TypeCommandPlanningResult.Planned(
            new TypeCommandPlan.ExactTypeOverview(
                mermaid
                    ? TypeOverviewHierarchyPresentationFormat.Mermaid
                    : TypeOverviewHierarchyPresentationFormat.Tree));
    }

    private static bool HasCompetingDemand(TypeOptions options) =>
        options.NonHierarchyFormatExplicitlySet
        || options.EnvelopeOutput
        || options.CompactJson
        || options.WorkspacePacket is not null
        || options.ShareFormat is not null
        || options.TypeFilter is not null
        || options.MemberFilter.Count > 0
        || options.KindFilter.Count > 0
        || options.UnsafeOnly
        || options.NoHeader
        || options.Count
        || options.Print
        || options.PrintRow is not null
        || options.Value
        || options.Urls
        || options.Paths
        || options.JsonArray
        || options.Discover is not null
        || options.Schema
        || options.HasSectionQuery
        || options.Rows is not null
        || options.LineWindowExplicitlySet
        || options.ShapeOrDiscoveryControlExplicitlySet
        || options.PerformanceTriage.HasFilters
        || options.PerformanceTriageControlExplicitlySet
        || options.RequestAllTaste
        || options.RequestReadableLocalNames;
}
