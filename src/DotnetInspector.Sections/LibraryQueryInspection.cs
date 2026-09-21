using DotnetInspector.Queries;
using DotnetInspector.Services;

namespace DotnetInspector.Sections;

/// <summary>
/// Materializes one resolved Library population and query plan into the shared
/// host-neutral inspection envelope.
/// </summary>
public static class LibraryQueryInspection
{
    public static InspectionEnvelope<LibraryQueryDocument> Execute(
        AssemblySet population,
        LibraryQueryPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(plan);

        LibraryQueryDocument content = LibraryQuery.Execute(
            population.Assemblies,
            population.Diagnostics,
            plan,
            cancellationToken);
        return Complete(plan, content);
    }

    public static InspectionEnvelope<LibraryQueryDocument>
        ExecuteParticipants(
            AssemblyContextGroup? group,
            IReadOnlyList<LibraryQueryParticipant> population,
            LibraryQueryPlan plan,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(plan);

        LibraryQueryDocument content = LibraryQuery.ExecuteParticipants(
            group,
            population,
            plan,
            cancellationToken);
        return Complete(plan, content);
    }

    private static InspectionEnvelope<LibraryQueryDocument> Complete(
        LibraryQueryPlan plan,
        LibraryQueryDocument content)
    {
        ValidateContent(plan, content);

        return new(
            new ResourcePath("library-query"),
            InspectionContentKind.Document,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }

    private static void ValidateContent(
        LibraryQueryPlan plan,
        LibraryQueryDocument content)
    {
        if (content.Results.IsDefault
            || content.Failures.IsDefault)
        {
            throw new ArgumentException(
                "Library Query content must be initialized.",
                nameof(content));
        }
        if (content.Summary.Matches != content.Results.Length
            || content.Summary.Failures != content.Failures.Length)
        {
            throw new ArgumentException(
                "Library Query content does not match its terminal accounting.",
                nameof(content));
        }
        if (content.Summary.CandidateLimit != plan.MaximumCandidates)
        {
            throw new ArgumentException(
                "Library Query content belongs to another query plan.",
                nameof(content));
        }
    }
}
