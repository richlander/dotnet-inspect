using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One detached direct assembly-reference row.</summary>
public sealed record AssemblyReferenceRow(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

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
}
