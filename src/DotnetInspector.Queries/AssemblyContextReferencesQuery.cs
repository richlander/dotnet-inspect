using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

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
}
