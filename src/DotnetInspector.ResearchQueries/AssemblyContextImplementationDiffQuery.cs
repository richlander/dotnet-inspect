using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>Runs selected implementation work over borrowed workspace images.</summary>
public static class AssemblyContextImplementationDiffQuery
{
    public static T Execute<T>(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        IReadOnlySet<int> beforeTokens, IReadOnlySet<int> afterTokens,
        Func<ImplementationComparisonBinding, ImplementationComparisonBinding, T> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeTokens);
        ArgumentNullException.ThrowIfNull(afterTokens);
        ArgumentNullException.ThrowIfNull(query);
        return Borrow(beforeGroup, before, beforeTokens, oldBinding =>
            Borrow(afterGroup, after, afterTokens, newBinding => query(oldBinding, newBinding),
                cancellationToken), cancellationToken);
    }

    static T Borrow<T>(AssemblyContextGroup group,
        AssemblyContextParticipant participant, IReadOnlySet<int> bodyTokens,
        Func<ImplementationComparisonBinding, T> query,
        CancellationToken cancellationToken)
    {
        AssemblyImageAccessResult<T> result = group.UseSnapshot(
            participant, cancellationToken, snapshot =>
            {
                var subject = new AssemblyContextSubject(participant.Assembly);
                var resolver = AssemblyContextAnalysisSource.Resolver(group, subject);
                LibraryCallGraphAnalysisResult? population = null;
                try
                {
                    population = LibraryBodyAnalysisService.ExecuteImage(
                        AssemblyContextAnalysisSource.Name(subject), snapshot.Content,
                        LibraryBodyAnalysisRequest.Create(
                            LibraryBodyAnalysisFeatures.MethodEvidence, bodyTokens), resolver).CallGraph;
                    T value = query(new(snapshot.RetainAssemblyReference(participant.Assembly),
                        resolver, population));
                    resolver.ValidateForPublication();
                    return value;
                }
                finally { population?.ReleaseCaches(); }
            });
        return result switch
        {
            AssemblyImageAccessResult<T>.Available available => available.Value,
            AssemblyImageAccessResult<T>.Rejected rejected =>
                throw new InspectionQueryException($"Implementation image rejected: {rejected.Failure}"),
            _ => throw new InvalidOperationException("Unknown image access outcome."),
        };
    }
}
