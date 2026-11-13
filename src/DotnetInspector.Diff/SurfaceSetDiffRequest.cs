using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Diff;

/// <summary>
/// An exact-pair analysis-set request over a <see cref="DiffSurfaceSet"/>:
/// a subject whose API surfaces the host already projected, one Library or a
/// multi-Library Package or Platform assembly set
/// (diff-system.md#single-library-api-diff-terminal). Its input is built by
/// the same builder as <see cref="LibraryPairDiffRequest"/>'s. It returns no
/// Library API presentation, which needs a Library pair.
/// </summary>
public sealed class SurfaceSetDiffRequest : DiffRequest
{
    public SurfaceSetDiffRequest(
        string name,
        string beforeVersion,
        string afterVersion,
        InspectionCapabilityCatalog catalog,
        AnalysisSetValidationResult.Accepted selection,
        DiffAnalysisDocumentViews views,
        DiffTargetPatterns targets,
        StringLiteralComparisonQueryPlan? stringLiteralQuery = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(beforeVersion);
        ArgumentNullException.ThrowIfNull(afterVersion);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(targets);
        Name = name;
        BeforeVersion = beforeVersion;
        AfterVersion = afterVersion;
        Catalog = catalog;
        Selection = selection;
        Views = views;
        Targets = targets;
        StringLiteralQuery = stringLiteralQuery;
    }

    /// <summary>The subject's display name, which labels the result.</summary>
    public string Name { get; }

    /// <summary>The Before endpoint's version label.</summary>
    public string BeforeVersion { get; }

    /// <summary>The After endpoint's version label.</summary>
    public string AfterVersion { get; }

    /// <summary>The capability catalog the selection was validated against.</summary>
    public InspectionCapabilityCatalog Catalog { get; }

    /// <summary>The validated analysis set.</summary>
    public AnalysisSetValidationResult.Accepted Selection { get; }

    /// <summary>The document views to project.</summary>
    public DiffAnalysisDocumentViews Views { get; }

    /// <summary>
    /// The target patterns, resolved against both endpoints' surfaces at the
    /// selection's report surface.
    /// </summary>
    public DiffTargetPatterns Targets { get; }

    /// <summary>The owner-resolved string-literal predicate, when selected.</summary>
    public StringLiteralComparisonQueryPlan? StringLiteralQuery { get; }
}

/// <summary>
/// A host's Type and member target patterns, such as the CLI's <c>-t</c> and
/// <c>--member</c>. The operation resolves them against both endpoints'
/// surfaces before any producer runs; a resolution failure fails the request
/// with <see cref="DiffAnalysisTargetException"/>.
/// </summary>
public sealed class DiffTargetPatterns
{
    public DiffTargetPatterns(
        IReadOnlySet<string> typeFilters,
        IReadOnlyCollection<string> memberTargets)
    {
        TypeFilters = typeFilters
            ?? throw new ArgumentNullException(nameof(typeFilters));
        MemberTargets = memberTargets
            ?? throw new ArgumentNullException(nameof(memberTargets));
    }

    /// <summary>Type filters: exact, wildcard, or namespace.</summary>
    public IReadOnlySet<string> TypeFilters { get; }

    /// <summary>Member targets, Type-qualified or in one Type filter's context.</summary>
    public IReadOnlyCollection<string> MemberTargets { get; }
}

/// <summary>
/// Each endpoint's API surface that the host has already opened and
/// projected, possibly merged across the endpoint's Libraries, with the files
/// it came from. This is the transitional input for host-opened single-Library,
/// multi-Library Package and Platform assembly sets, which adoption step 3
/// retires with the endpoint routes
/// (diff-system.md#single-library-api-diff-terminal).
/// </summary>
public sealed class DiffSurfaceSet
{
    public DiffSurfaceSet(
        ApiSurface before,
        ApiSurface after,
        IReadOnlyList<string> beforePaths,
        IReadOnlyList<string> afterPaths)
    {
        Before = before ?? throw new ArgumentNullException(nameof(before));
        After = after ?? throw new ArgumentNullException(nameof(after));
        BeforePaths = beforePaths
            ?? throw new ArgumentNullException(nameof(beforePaths));
        AfterPaths = afterPaths
            ?? throw new ArgumentNullException(nameof(afterPaths));
    }

    public ApiSurface Before { get; }

    public ApiSurface After { get; }

    /// <summary>The Before endpoint's extracted files; empty for a package endpoint scope.</summary>
    public IReadOnlyList<string> BeforePaths { get; }

    /// <summary>The After endpoint's extracted files; empty for a package endpoint scope.</summary>
    public IReadOnlyList<string> AfterPaths { get; }
}
