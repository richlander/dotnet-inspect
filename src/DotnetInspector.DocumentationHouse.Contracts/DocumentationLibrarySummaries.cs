namespace DotnetInspector.DocumentationHouse;

/// <summary>
/// A whole-Library compiled-XML summary read
/// (docs/design/documentation-house.md#whole-library-compiled-summaries).
/// The borrowed operation lease names the Library; the contributions are the
/// source adapter's Library-level companion selection evidence for it.
/// </summary>
public sealed class DocumentationLibrarySummaryRequest
{
    public DocumentationLibrarySummaryRequest(
        IReadOnlyList<CompiledXmlContribution> contributions,
        DocumentationHouseLimits limits,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(contributions);
        ArgumentNullException.ThrowIfNull(limits);
        if (contributions.Any(static contribution => contribution is null))
        {
            throw new ArgumentException(
                "Compiled XML contributions cannot contain null.",
                nameof(contributions));
        }

        Contributions = Array.AsReadOnly(contributions.ToArray());
        Limits = limits;
        Deadline = deadline;
    }

    public IReadOnlyList<CompiledXmlContribution> Contributions { get; }
    public DocumentationHouseLimits Limits { get; }
    public DateTimeOffset Deadline { get; }
}

/// <summary>
/// The settled result of a whole-Library summary read. Only
/// <see cref="Available"/> carries summaries; every other outcome says why
/// none are available, and none of them becomes an empty success.
/// </summary>
public abstract record DocumentationLibrarySummaryOutcome
{
    private protected DocumentationLibrarySummaryOutcome()
    {
    }

    /// <summary>
    /// The selected companion was read whole. <see cref="Summaries"/> maps
    /// each compiler XML ID in it to its summary, or <c>""</c> for an entry
    /// without one; an ID it lacks has no entry in the companion.
    /// </summary>
    public sealed record Available(
        CompiledXmlContribution Selected,
        IReadOnlyDictionary<string, string> Summaries,
        int CompiledXmlBytes)
        : DocumentationLibrarySummaryOutcome;

    /// <summary>Complete selection evidence proves the Library has no companion.</summary>
    public sealed record Absent(
        IReadOnlyList<CompiledXmlContribution> Contributions)
        : DocumentationLibrarySummaryOutcome;

    /// <summary>No authorized companion exists, without proof of absence.</summary>
    public sealed record Unavailable(
        IReadOnlyList<CompiledXmlContribution> Contributions)
        : DocumentationLibrarySummaryOutcome;

    /// <summary>Several eligible companions without explicit precedence.</summary>
    public sealed record Ambiguous(
        IReadOnlyList<CompiledXmlContribution> Candidates)
        : DocumentationLibrarySummaryOutcome;

    /// <summary>A contribution does not describe the leased Library.</summary>
    public sealed record Rejected(
        IReadOnlyList<DocumentationCompiledXmlRejection> Rejections)
        : DocumentationLibrarySummaryOutcome;

    public sealed record Incomplete(
        DocumentationIncompleteBoundary Boundary)
        : DocumentationLibrarySummaryOutcome;

    public sealed record Failed(
        DocumentationCompiledXmlFailureKind Kind)
        : DocumentationLibrarySummaryOutcome;
}
