using ILInspector.Research;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Runs a bounded body-change census over already-corresponded MethodDefs.
/// </summary>
public static class AssemblyContextImplementationChangeSummaryQuery
{
    public static IReadOnlyList<ImplementationBodyChange> Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        IReadOnlyList<ImplementationBodyPair> pairs)
    {
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0)
            return [];

        var beforeSubject =
            new AssemblyContextSubject(before.Assembly);
        var afterSubject =
            new AssemblyContextSubject(after.Assembly);
        var beforeResolver =
            AssemblyContextAnalysisSource.Resolver(
                beforeGroup,
                beforeSubject);
        var afterResolver =
            AssemblyContextAnalysisSource.Resolver(
                afterGroup,
                afterSubject);
        ResolvedAssemblyReference beforeAssembly =
            Retain(beforeGroup, before);
        ResolvedAssemblyReference afterAssembly =
            Retain(afterGroup, after);
        IReadOnlyList<ImplementationBodyChange> result =
            ImplementationChangeSummary.Compare(
                beforeAssembly,
                beforeResolver,
                afterAssembly,
                afterResolver,
                pairs);
        beforeResolver.ValidateForPublication();
        afterResolver.ValidateForPublication();
        return result;
    }

    static ResolvedAssemblyReference Retain(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
        => group.RetainAssemblyReference(
            participant.Assembly) switch
        {
            AssemblyImageAccessResult<
                ResolvedAssemblyReference>.Available available =>
                    available.Value,
            AssemblyImageAccessResult<
                ResolvedAssemblyReference>.Rejected rejected =>
                    throw new InspectionQueryException(
                        $"Implementation image rejected: "
                            + $"{rejected.Failure}"),
            _ => throw new InvalidOperationException(
                "Unknown image access outcome."),
        };
}
