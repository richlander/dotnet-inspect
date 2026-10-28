using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;

namespace DotnetInspector.Diff;

/// <summary>
/// An exact-pair request over the analysis catalog whose subject is one
/// Library, or Types or Members within it: the single-Library API Diff
/// terminal (diff-system.md#single-library-api-diff-terminal). The terminal
/// populates <see cref="DiffAnalysisDocument.LibraryApi"/>.
/// </summary>
public sealed class LibraryPairDiffRequest : DiffRequest
{
    public LibraryPairDiffRequest(
        string name,
        string beforeVersion,
        string afterVersion,
        InspectionCapabilityCatalog catalog,
        AnalysisSetValidationResult.Accepted selection,
        DiffAnalysisDocumentViews views,
        DiffTargets targets,
        ApiSurfaceScope view,
        ApiSurfaceProjectionLimits bounds,
        StringLiteralComparisonQueryPlan? stringLiteralQuery = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(beforeVersion);
        ArgumentNullException.ThrowIfNull(afterVersion);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(bounds);
        if (targets.Surface != selection.Surface)
        {
            throw new ArgumentException(
                $"{targets.Surface} targets do not match the selected "
                    + $"{selection.Surface} report surface.",
                nameof(targets));
        }

        Name = name;
        BeforeVersion = beforeVersion;
        AfterVersion = afterVersion;
        Catalog = catalog;
        Selection = selection;
        Views = views;
        Targets = targets;
        View = view;
        Bounds = bounds;
        StringLiteralQuery = stringLiteralQuery;
    }

    /// <summary>The Library's display name, which labels the result.</summary>
    public string Name { get; }

    /// <summary>The Before endpoint's version label.</summary>
    public string BeforeVersion { get; }

    /// <summary>The After endpoint's version label.</summary>
    public string AfterVersion { get; }

    /// <summary>The capability catalog the selection was validated against.</summary>
    public InspectionCapabilityCatalog Catalog { get; }

    /// <summary>The validated analysis set: the request's content.</summary>
    public AnalysisSetValidationResult.Accepted Selection { get; }

    /// <summary>The document views to project.</summary>
    public DiffAnalysisDocumentViews Views { get; }

    /// <summary>The Library, or the Types or Members within it, to compare.</summary>
    public DiffTargets Targets { get; }

    /// <summary>The API surface view both endpoints are projected at.</summary>
    public ApiSurfaceScope View { get; }

    /// <summary>The per-endpoint projection bounds.</summary>
    public ApiSurfaceProjectionLimits Bounds { get; }

    /// <summary>The owner-resolved string-literal predicate, when selected.</summary>
    public StringLiteralComparisonQueryPlan? StringLiteralQuery { get; }
}

/// <summary>
/// The targets a <see cref="LibraryPairDiffRequest"/> states within its one
/// Library. Names and identities are exact.
/// </summary>
public abstract record DiffTargets
{
    private DiffTargets()
    {
    }

    /// <summary>The report surface these targets address.</summary>
    public abstract AnalysisReportSurfaceKind Surface { get; }

    /// <summary>The whole Library.</summary>
    public sealed record Library : DiffTargets
    {
        public override AnalysisReportSurfaceKind Surface =>
            AnalysisReportSurfaceKind.Library;
    }

    /// <summary>Named Types within the Library.</summary>
    public sealed record Types : DiffTargets
    {
        public Types(IReadOnlyList<string> names)
        {
            Names = names ?? throw new ArgumentNullException(nameof(names));
        }

        public IReadOnlyList<string> Names { get; }

        public override AnalysisReportSurfaceKind Surface =>
            AnalysisReportSurfaceKind.Type;
    }

    /// <summary>
    /// Named Members within the Library, with the Types that own them.
    /// </summary>
    public sealed record Members : DiffTargets
    {
        public Members(
            IReadOnlyList<string> ownerTypeNames,
            IReadOnlyList<string> identities)
        {
            OwnerTypeNames = ownerTypeNames
                ?? throw new ArgumentNullException(nameof(ownerTypeNames));
            Identities = identities
                ?? throw new ArgumentNullException(nameof(identities));
        }

        public IReadOnlyList<string> OwnerTypeNames { get; }

        public IReadOnlyList<string> Identities { get; }

        public override AnalysisReportSurfaceKind Surface =>
            AnalysisReportSurfaceKind.Member;
    }
}

/// <summary>
/// One Library per side that the host has already opened and paired. This is
/// the transitional input that adoption steps 3 and 5 retire by opening and
/// pairing inside the operation
/// (diff-system.md#single-library-api-diff-terminal).
/// </summary>
public sealed class DiffLibraryPair
{
    public DiffLibraryPair(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after)
    {
        BeforeGroup = beforeGroup
            ?? throw new ArgumentNullException(nameof(beforeGroup));
        Before = before ?? throw new ArgumentNullException(nameof(before));
        AfterGroup = afterGroup
            ?? throw new ArgumentNullException(nameof(afterGroup));
        After = after ?? throw new ArgumentNullException(nameof(after));
    }

    public AssemblyContextGroup BeforeGroup { get; }

    public AssemblyContextParticipant Before { get; }

    public AssemblyContextGroup AfterGroup { get; }

    public AssemblyContextParticipant After { get; }
}

/// <summary>
/// What the host supplies for the pair's content beyond the request: the
/// analyses the host cannot run.
/// </summary>
public sealed class DiffPairContent
{
    public DiffPairContent(
        IReadOnlyList<DiffAnalysisHostUnavailability> hostUnavailability)
    {
        HostUnavailability = hostUnavailability
            ?? throw new ArgumentNullException(nameof(hostUnavailability));
    }

    /// <summary>Selected analyses the host reports as unavailable.</summary>
    public IReadOnlyList<DiffAnalysisHostUnavailability> HostUnavailability { get; }
}

/// <summary>
/// The whole-Library API comparison of one endpoint pair and its portable
/// presentation. Both are independent of the request's targets.
/// </summary>
public sealed record LibraryApiComparison(
    AssemblyContextApiComparisonResult Comparison,
    LibraryApiDiffOutcome LibraryApi);

/// <summary>
/// A host-owned reuse hook: returns a previously completed comparison of the
/// same endpoint pair, view, and bounds, or runs <paramref name="compute"/>.
/// </summary>
public delegate LibraryApiComparison DiffComparisonReuse(
    Func<LibraryApiComparison> compute);
