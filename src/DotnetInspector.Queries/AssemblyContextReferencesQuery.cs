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
    {
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>> result =
            ExecuteParticipant(group, participant);
        return result switch
        {
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Available available =>
                new AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceRow>>.Available(
                        available.Subject,
                        [
                            .. available.Value.Select(static reference =>
                                new AssemblyReferenceRow(
                                    reference.Name,
                                    reference.Version?.ToString() ?? "",
                                    reference.Culture,
                                    reference.PublicKeyToken)),
                        ]),
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Rejected rejected =>
                new AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceRow>>.Rejected(
                        rejected.Subject,
                        rejected.Failure),
            AssemblyContextEntry<
                ImmutableArray<AssemblyReferenceIdentity>>.Failed failed =>
                new AssemblyContextEntry<
                    ImmutableArray<AssemblyReferenceRow>>.Failed(
                        failed.Subject,
                        failed.Error),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context reference query result."),
        };
    }
}
