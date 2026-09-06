using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// The restored coordinate of one direct project dependency that authored a project document.
/// </summary>
/// <remarks>
/// The coordinate is host-supplied identity for a dependency the host already restored. These
/// queries compare and order it; they never resolve, acquire, or parse it. Record equality is
/// exact, while row identity uses NuGet's case-insensitive package-id rule through
/// <see cref="ProjectDocumentIdentity"/>.
/// </remarks>
public sealed record ProjectDependencyCoordinate
{
    public ProjectDependencyCoordinate(string packageId, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        PackageId = packageId;
        Version = version;
    }

    /// <summary>The package id exactly as the restored project spells it.</summary>
    public string PackageId { get; }

    /// <summary>The restored package version exactly as the project spells it.</summary>
    public string Version { get; }
}

/// <summary>
/// One skill document a direct project dependency ships.
/// </summary>
/// <param name="Package">The dependency that authored the document.</param>
/// <param name="DocumentPath">
/// The package-relative path the dependency spells for the document. It is opaque row identity
/// here: no query parses it, resolves it, or treats it as a host filesystem path.
/// </param>
/// <param name="Name">The declared skill name, or <see langword="null"/> when it is unavailable.</param>
/// <param name="Description">
/// The declared skill description, or <see langword="null"/> when it is unavailable.
/// </param>
/// <param name="Size">The document's byte size, or <see langword="null"/> when it is unknown.</param>
/// <param name="Content">
/// The already-acquired document content, or <see langword="null"/> when the document is missing
/// or could not be read. A missing or unreadable document keeps its row and stays
/// <see langword="null"/>; it never becomes an empty document.
/// </param>
public sealed record ProjectSkillEntry(
    ProjectDependencyCoordinate Package,
    string DocumentPath,
    string? Name = null,
    string? Description = null,
    long? Size = null,
    string? Content = null);

/// <summary>
/// One direct project dependency's optional agent-guidance document.
/// </summary>
/// <param name="Package">The dependency the row reports.</param>
/// <param name="DocumentPath">
/// The package-relative path of the guidance document, or <see langword="null"/> when the
/// dependency ships none. The path is opaque row data, not a host filesystem path.
/// </param>
/// <param name="Name">The declared guidance name, or <see langword="null"/> when it is unavailable.</param>
/// <param name="Description">
/// The declared guidance description, or <see langword="null"/> when it is unavailable.
/// </param>
/// <param name="Size">The document's byte size, or <see langword="null"/> when it is unknown.</param>
/// <param name="Content">
/// The already-acquired document content, or <see langword="null"/> when the dependency ships no
/// guidance or the document could not be read. Every direct dependency keeps a row either way.
/// </param>
public sealed record ProjectAgentGuidanceEntry(
    ProjectDependencyCoordinate Package,
    string? DocumentPath = null,
    string? Name = null,
    string? Description = null,
    long? Size = null,
    string? Content = null);

/// <summary>
/// The package document one direct project dependency ships, such as its readme.
/// </summary>
/// <param name="Package">The dependency that authored the document.</param>
/// <param name="DocumentPath">
/// The package-relative path of the selected document, or <see langword="null"/> when the
/// dependency ships none. The path is opaque row data, not a host filesystem path.
/// </param>
/// <param name="Size">The document's byte size, or <see langword="null"/> when it is unknown.</param>
/// <param name="Content">
/// The already-acquired document content, or <see langword="null"/> when the dependency ships no
/// document or the document could not be acquired or read.
/// </param>
public sealed record ProjectPackageDocumentEntry(
    ProjectDependencyCoordinate Package,
    string? DocumentPath = null,
    long? Size = null,
    string? Content = null);

/// <summary>
/// The stable reason one project document could not be inspected.
/// </summary>
public enum ProjectDocumentFailureReason
{
    /// <summary>The restored project listed the document, but it was not present.</summary>
    Missing,

    /// <summary>The document was present, but its content could not be read.</summary>
    Unreadable,

    /// <summary>The document could not be acquired from the package that declares it.</summary>
    Unacquired,

    /// <summary>The document declares metadata that does not satisfy its contract.</summary>
    InvalidMetadata,
}

/// <summary>
/// Whether a project-document failure may name the package-authored subject that produced it.
/// </summary>
public enum ProjectDocumentSubjectDisposition
{
    /// <summary>The failure names the dependency coordinate and document path it came from.</summary>
    Named,

    /// <summary>
    /// The failure withholds package-authored identity because that identity cannot safely be
    /// echoed, such as when a document's own declared metadata is what failed validation.
    /// </summary>
    Redacted,
}

/// <summary>
/// One visible project-document acquisition or read failure.
/// </summary>
/// <remarks>
/// <see cref="Message"/> is product-authored and content-free: it is derived from
/// <see cref="Reason"/> and never quotes artifact text. A
/// <see cref="ProjectDocumentSubjectDisposition.Redacted"/> failure carries no package-authored
/// identity at all, because <see cref="Named"/> and <see cref="Redacted"/> are the only ways to
/// construct one. <c>RedactedFailure_CarriesNoPackageAuthoredIdentity</c>,
/// <c>Failure_HasNoConstructionPathBesideItsFactories</c>,
/// <c>FailureMessage_IsStableForEveryReason</c>, and
/// <c>FailureMessage_IsSafeForUnknownFutureReason</c> gate this contract.
/// </remarks>
public sealed record ProjectDocumentFailure
{
    private ProjectDocumentFailure(
        ProjectDocumentSubjectDisposition disposition,
        ProjectDependencyCoordinate? package,
        string? documentPath,
        ProjectDocumentFailureReason reason)
    {
        Disposition = disposition;
        Package = package;
        DocumentPath = documentPath;
        Reason = reason;
    }

    /// <summary>Reports a failure whose package-authored subject is safe to name.</summary>
    /// <param name="package">The dependency whose document failed.</param>
    /// <param name="documentPath">
    /// The package-relative path of the document, or <see langword="null"/> when the failure has
    /// no single document path, such as a package whose documents were never acquired.
    /// </param>
    /// <param name="reason">The stable reason the document could not be inspected.</param>
    public static ProjectDocumentFailure Named(
        ProjectDependencyCoordinate package,
        string? documentPath,
        ProjectDocumentFailureReason reason)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new(ProjectDocumentSubjectDisposition.Named, package, documentPath, reason);
    }

    /// <summary>
    /// Reports a failure whose package-authored subject is withheld. The result carries the
    /// reason alone, so no artifact-authored identity reaches a consumer.
    /// </summary>
    /// <param name="reason">The stable reason the document could not be inspected.</param>
    public static ProjectDocumentFailure Redacted(ProjectDocumentFailureReason reason)
        => new(
            ProjectDocumentSubjectDisposition.Redacted,
            package: null,
            documentPath: null,
            reason);

    /// <summary>Whether this failure names or withholds its package-authored subject.</summary>
    public ProjectDocumentSubjectDisposition Disposition { get; }

    /// <summary>
    /// The dependency whose document failed, or <see langword="null"/> when the subject is
    /// redacted.
    /// </summary>
    public ProjectDependencyCoordinate? Package { get; }

    /// <summary>
    /// The package-relative path of the failed document, or <see langword="null"/> when the
    /// subject is redacted or the failure names no single document.
    /// </summary>
    public string? DocumentPath { get; }

    /// <summary>The stable reason the document could not be inspected.</summary>
    public ProjectDocumentFailureReason Reason { get; }

    /// <summary>A stable, product-authored description of <see cref="Reason"/>.</summary>
    public string Message => Reason switch
    {
        ProjectDocumentFailureReason.Missing =>
            "A document the restored project lists is missing from the package.",
        ProjectDocumentFailureReason.Unreadable =>
            "A project document could not be read.",
        ProjectDocumentFailureReason.Unacquired =>
            "A project document could not be acquired from the package that declares it.",
        ProjectDocumentFailureReason.InvalidMetadata =>
            "A project document declares metadata that does not satisfy its contract.",
        _ => "A project document could not be inspected.",
    };
}

/// <summary>The already-acquired dependency skill documents one query projects.</summary>
public sealed record ProjectSkillsRequest
{
    public ProjectSkillsRequest(
        IEnumerable<ProjectSkillEntry> skills,
        IEnumerable<ProjectDocumentFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(skills);
        Skills = ProjectDocumentIdentity.Materialize(skills, nameof(skills));
        Failures = ProjectDocumentIdentity.Materialize(failures, nameof(failures));
    }

    /// <summary>One entry per skill document the host acquired, in any order.</summary>
    public ImmutableArray<ProjectSkillEntry> Skills { get; }

    /// <summary>Every skill document the host could not acquire or read.</summary>
    public ImmutableArray<ProjectDocumentFailure> Failures { get; }
}

/// <summary>The already-acquired agent-guidance documents one query projects.</summary>
public sealed record ProjectAgentGuidanceRequest
{
    public ProjectAgentGuidanceRequest(
        IEnumerable<ProjectAgentGuidanceEntry> guidance,
        IEnumerable<ProjectDocumentFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(guidance);
        Guidance = ProjectDocumentIdentity.Materialize(guidance, nameof(guidance));
        Failures = ProjectDocumentIdentity.Materialize(failures, nameof(failures));
    }

    /// <summary>One entry per direct dependency, whether or not it ships guidance.</summary>
    public ImmutableArray<ProjectAgentGuidanceEntry> Guidance { get; }

    /// <summary>Every guidance document the host could not read.</summary>
    public ImmutableArray<ProjectDocumentFailure> Failures { get; }
}

/// <summary>The already-acquired dependency package documents one query projects.</summary>
public sealed record ProjectPackageDocumentsRequest
{
    public ProjectPackageDocumentsRequest(
        IEnumerable<ProjectPackageDocumentEntry> documents,
        IEnumerable<ProjectDocumentFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        Documents = ProjectDocumentIdentity.Materialize(documents, nameof(documents));
        Failures = ProjectDocumentIdentity.Materialize(failures, nameof(failures));
    }

    /// <summary>At most one entry per direct dependency, in any order.</summary>
    public ImmutableArray<ProjectPackageDocumentEntry> Documents { get; }

    /// <summary>Every package document the host could not acquire or read.</summary>
    public ImmutableArray<ProjectDocumentFailure> Failures { get; }
}

/// <summary>The deterministic result of inspecting dependency skill documents.</summary>
public sealed record ProjectSkillsResult(
    ImmutableArray<ProjectSkillEntry> Skills,
    ImmutableArray<ProjectDocumentFailure> Failures);

/// <summary>The deterministic result of inspecting dependency agent guidance.</summary>
public sealed record ProjectAgentGuidanceResult(
    ImmutableArray<ProjectAgentGuidanceEntry> Guidance,
    ImmutableArray<ProjectDocumentFailure> Failures);

/// <summary>The deterministic result of inspecting dependency package documents.</summary>
public sealed record ProjectPackageDocumentsResult(
    ImmutableArray<ProjectPackageDocumentEntry> Documents,
    ImmutableArray<ProjectDocumentFailure> Failures);

/// <summary>
/// Projects already-acquired dependency skill documents into a deterministic result.
/// </summary>
/// <remarks>
/// Every skill document the host acquired keeps a row, including a missing or unreadable one,
/// so a consumer can address rows by ordinal. The declared cost is
/// <see cref="InspectionCost.NetworkFree"/> because the host acquired and authorized the
/// documents before the request was built; this query performs no acquisition.
/// <c>SkillsQuery_OrdersEntriesByPackageThenDocumentPath</c>,
/// <c>SkillsQuery_KeepsMissingAndUnreadableRowsWithNullContent</c>, and
/// <c>SkillsQuery_RejectsDuplicateRowIdentity</c> gate that contract.
/// </remarks>
public static class ProjectSkillsQuery
{
    public static InspectionQuery<ProjectSkillsResult> Definition { get; } =
        new("Project skills", InspectionCost.NetworkFree);

    public static ProjectSkillsResult Execute(ProjectSkillsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImmutableArray<ProjectSkillEntry> skills = ProjectDocumentIdentity.Order(
            request.Skills,
            static entry => entry.Package,
            static entry => entry.DocumentPath);
        ProjectDocumentIdentity.RequireDistinctRows(
            skills,
            static entry => entry.Package,
            static entry => entry.DocumentPath,
            "skill");
        return new ProjectSkillsResult(
            skills,
            ProjectDocumentIdentity.OrderFailures(request.Failures));
    }
}

/// <summary>
/// Projects already-acquired agent-guidance documents into a deterministic result.
/// </summary>
/// <remarks>
/// The result keeps one row per direct dependency, so a dependency that ships no guidance stays
/// visible with a <see langword="null"/> document path and content. The declared cost is
/// <see cref="InspectionCost.NetworkFree"/> because the host acquired and authorized the
/// documents before the request was built; this query performs no acquisition.
/// <c>AgentGuidanceQuery_KeepsARowForADependencyWithoutGuidance</c> and
/// <c>AgentGuidanceQuery_RejectsDuplicateRowIdentity</c> gate that contract.
/// </remarks>
public static class ProjectAgentGuidanceQuery
{
    public static InspectionQuery<ProjectAgentGuidanceResult> Definition { get; } =
        new("Project agent guidance", InspectionCost.NetworkFree);

    public static ProjectAgentGuidanceResult Execute(ProjectAgentGuidanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImmutableArray<ProjectAgentGuidanceEntry> guidance = ProjectDocumentIdentity.Order(
            request.Guidance,
            static entry => entry.Package,
            static entry => entry.DocumentPath);
        ProjectDocumentIdentity.RequireDistinctCoordinates(
            guidance,
            static entry => entry.Package,
            "agent guidance");
        return new ProjectAgentGuidanceResult(
            guidance,
            ProjectDocumentIdentity.OrderFailures(request.Failures));
    }
}

/// <summary>
/// Projects already-acquired dependency package documents into a deterministic result.
/// </summary>
/// <remarks>
/// The result keeps at most one row per direct dependency. The declared cost is
/// <see cref="InspectionCost.Unbounded"/>: unlike skills and guidance, a host builds this request
/// by reaching past restored content to every dependency's package, so the demand that produces
/// the input is network-bound and fans out with the dependency count. The query itself performs
/// no acquisition. <c>PackageDocumentsQuery_OrdersDocumentsAndKeepsNullContent</c>,
/// <c>PackageDocumentsQuery_RejectsDuplicateRowIdentity</c>, and
/// <c>Definitions_DeclareTruthfulCostsUnderDemand</c> gate that contract.
/// </remarks>
public static class ProjectPackageDocumentsQuery
{
    public static InspectionQuery<ProjectPackageDocumentsResult> Definition { get; } =
        new("Project package documents", InspectionCost.Unbounded);

    public static ProjectPackageDocumentsResult Execute(ProjectPackageDocumentsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ImmutableArray<ProjectPackageDocumentEntry> documents = ProjectDocumentIdentity.Order(
            request.Documents,
            static entry => entry.Package,
            static entry => entry.DocumentPath);
        ProjectDocumentIdentity.RequireDistinctCoordinates(
            documents,
            static entry => entry.Package,
            "package document");
        return new ProjectPackageDocumentsResult(
            documents,
            ProjectDocumentIdentity.OrderFailures(request.Failures));
    }
}

/// <summary>
/// The shared ordering and row-identity rules of the project document queries.
/// </summary>
/// <remarks>
/// Ordering is total over row identity, so a result does not depend on the order in which a host
/// enumerated its documents. Rows that tie on every ordering key are duplicate identities and are
/// rejected rather than ordered arbitrarily.
/// </remarks>
internal static class ProjectDocumentIdentity
{
    /// <summary>Orders optional document paths exactly, with an absent path first.</summary>
    private static readonly IComparer<string?> OptionalOrdinal =
        Comparer<string?>.Create(static (left, right) => string.CompareOrdinal(left, right));

    /// <summary>Orders optional package ids the way NuGet package identity compares them.</summary>
    private static readonly IComparer<string?> OptionalOrdinalIgnoreCase =
        Comparer<string?>.Create(
            static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left, right));

    internal static ImmutableArray<TItem> Materialize<TItem>(
        IEnumerable<TItem>? items,
        string parameterName)
        where TItem : class
    {
        if (items is null)
            return [];

        ImmutableArray<TItem> materialized = [.. items];
        if (materialized.Any(static item => item is null))
            throw new ArgumentException("A project document row cannot be null.", parameterName);

        return materialized;
    }

    internal static ImmutableArray<TEntry> Order<TEntry>(
        ImmutableArray<TEntry> entries,
        Func<TEntry, ProjectDependencyCoordinate> coordinate,
        Func<TEntry, string?> documentPath)
        =>
        [
            .. entries
                .OrderBy(entry => coordinate(entry).PackageId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => coordinate(entry).PackageId, StringComparer.Ordinal)
                .ThenBy(entry => coordinate(entry).Version, StringComparer.Ordinal)
                .ThenBy(documentPath, OptionalOrdinal),
        ];

    /// <summary>
    /// Rejects rows that share one dependency coordinate and document path, so every row keeps a
    /// distinct identity a consumer can address.
    /// </summary>
    internal static void RequireDistinctRows<TEntry>(
        ImmutableArray<TEntry> entries,
        Func<TEntry, ProjectDependencyCoordinate> coordinate,
        Func<TEntry, string?> rowIdentityPath,
        string subject)
    {
        var seen = new HashSet<RowIdentity>();
        foreach (TEntry entry in entries)
        {
            ProjectDependencyCoordinate package = coordinate(entry);
            if (!seen.Add(new RowIdentity(
                    package.PackageId,
                    package.Version,
                    rowIdentityPath(entry))))
            {
                throw new InspectionQueryException(
                    $"Project {subject} rows must have distinct package and document identity.");
            }
        }
    }

    /// <summary>
    /// Rejects rows that share one dependency coordinate, for a result that reports at most one
    /// document per direct dependency.
    /// </summary>
    internal static void RequireDistinctCoordinates<TEntry>(
        ImmutableArray<TEntry> entries,
        Func<TEntry, ProjectDependencyCoordinate> coordinate,
        string subject)
        => RequireDistinctRows(entries, coordinate, static _ => null, subject);

    internal static ImmutableArray<ProjectDocumentFailure> OrderFailures(
        ImmutableArray<ProjectDocumentFailure> failures)
        =>
        [
            .. failures
                .OrderBy(static failure =>
                    failure.Disposition == ProjectDocumentSubjectDisposition.Redacted ? 1 : 0)
                .ThenBy(
                    static failure => failure.Package?.PackageId,
                    OptionalOrdinalIgnoreCase)
                .ThenBy(static failure => failure.Package?.PackageId, OptionalOrdinal)
                .ThenBy(static failure => failure.Package?.Version, OptionalOrdinal)
                .ThenBy(static failure => failure.DocumentPath, OptionalOrdinal)
                .ThenBy(static failure => failure.Reason),
        ];

    /// <summary>
    /// One row's identity. Package ids compare case-insensitively because NuGet package identity
    /// does; versions and document paths compare exactly.
    /// </summary>
    private readonly record struct RowIdentity(
        string PackageId,
        string Version,
        string? DocumentPath)
    {
        public bool Equals(RowIdentity other)
            => StringComparer.OrdinalIgnoreCase.Equals(PackageId, other.PackageId)
                && StringComparer.Ordinal.Equals(Version, other.Version)
                && StringComparer.Ordinal.Equals(DocumentPath, other.DocumentPath);

        public override int GetHashCode()
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(PackageId),
                StringComparer.Ordinal.GetHashCode(Version),
                DocumentPath is null ? 0 : StringComparer.Ordinal.GetHashCode(DocumentPath));
    }
}
