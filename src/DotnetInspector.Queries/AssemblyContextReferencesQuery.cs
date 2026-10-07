using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One detached direct assembly-reference row.</summary>
public sealed record AssemblyReferenceRow(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

/// <summary>Why one participant could not issue detached assembly-reference rows.</summary>
public enum AssemblyReferenceRowsRejectionKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget,
    UnsupportedMetadataFormat,
}

/// <summary>One detached assembly-reference row rejection.</summary>
public readonly record struct AssemblyReferenceRowsRejection(
    AssemblyReferenceRowsRejectionKind Kind,
    string Detail);

/// <summary>
/// Reads direct assembly references from every participant in one binding-consistent context.
/// </summary>
public static class AssemblyContextReferencesQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>>>
        Definition { get; } =
        new("Assembly context references", InspectionCost.Unbounded);

    public static AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>> Execute(
        AssemblyContextGroup group)
        => AssemblyContextQueryExecutor.Execute(
            group,
            AssemblyReferencesQuery.Read);

    /// <summary>Reads one participant without releasing it from a reusable group.</summary>
    public static AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
        => AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            AssemblyReferencesQuery.Read);

    /// <summary>
    /// Reads one participant and detaches its direct references from Metadata identity types.
    /// </summary>
    public static AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>
        ExecuteParticipantRows(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
        => AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            AssemblyReferencesQuery.ReadRows);

    /// <summary>Detaches one participant rejection from Metadata failure types.</summary>
    public static AssemblyReferenceRowsRejection GetRowsRejection(
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>.Rejected rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        return new(
            rejected.Failure.Kind switch
            {
                CandidateOpenFailureKind.Unreadable =>
                    AssemblyReferenceRowsRejectionKind.Unreadable,
                CandidateOpenFailureKind.InvalidImage =>
                    AssemblyReferenceRowsRejectionKind.InvalidImage,
                CandidateOpenFailureKind.ResourceBudget =>
                    AssemblyReferenceRowsRejectionKind.ResourceBudget,
                CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                    AssemblyReferenceRowsRejectionKind.UnsupportedMetadataFormat,
                _ => throw new InvalidOperationException(
                    "Unknown assembly-context candidate failure."),
            },
            rejected.Failure.Detail);
    }
}
