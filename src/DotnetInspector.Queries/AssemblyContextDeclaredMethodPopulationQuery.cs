using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Executes one authenticated TypeDef's declared-MethodDef population while
/// the participant session is alive.
/// </summary>
public static class AssemblyContextDeclaredMethodPopulationQuery
{
    public static AssemblyContextEntry<
        MetadataDeclaredMethodPopulationOutcome> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionBinding type,
            MetadataDeclaredMethodPopulationTerminal terminal,
            int maximumRows = int.MaxValue,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);

        return AssemblyContextQueryExecutor.ExecuteParticipant<
            MetadataDeclaredMethodPopulationOutcome>(
            group,
            participant,
            session =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return session.DeclaredMethods(
                    new(
                        type,
                        terminal,
                        maximumRows));
            });
    }
}
