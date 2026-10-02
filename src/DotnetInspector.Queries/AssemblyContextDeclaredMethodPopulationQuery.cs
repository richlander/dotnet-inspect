using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public abstract record AssemblyContextDeclaredMethodPopulationResult
{
    private AssemblyContextDeclaredMethodPopulationResult()
    {
    }

    public sealed record Available(
        MetadataTypeDefinitionBinding Binding,
        MetadataDeclaredMethodPopulationOutcome Population)
        : AssemblyContextDeclaredMethodPopulationResult;

    public sealed record Missing
        : AssemblyContextDeclaredMethodPopulationResult;

    public sealed record Ambiguous(int CandidateCount)
        : AssemblyContextDeclaredMethodPopulationResult;

    public sealed record NotLocalDefinition
        : AssemblyContextDeclaredMethodPopulationResult;

    public sealed record Incomplete(long Budget)
        : AssemblyContextDeclaredMethodPopulationResult;

    public sealed record Rejected
        : AssemblyContextDeclaredMethodPopulationResult;
}

/// <summary>
/// Resolves one exact TypeDef and executes its declared-MethodDef population
/// while the participant session is alive.
/// </summary>
public static class AssemblyContextDeclaredMethodPopulationQuery
{
    public static AssemblyContextEntry<
        AssemblyContextDeclaredMethodPopulationResult> ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionName type,
            MetadataDeclaredMethodPopulationTerminal terminal,
            int maximumRows = int.MaxValue,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);

        return AssemblyContextQueryExecutor.ExecuteParticipant<
            AssemblyContextDeclaredMethodPopulationResult>(
            group,
            participant,
            session =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                TypeDeclarationResult declaration =
                    session.ProbeDeclaration(type);
                TypeDefinitionToken? definition =
                    declaration switch
                    {
                        TypeDeclarationResult.Defined defined =>
                            defined.Definition,
                        TypeDeclarationResult.DefinitionKindUnavailable
                            unavailable =>
                            unavailable.Definition,
                        _ => null,
                    };
                if (definition is { } token)
                {
                    var binding = new MetadataTypeDefinitionBinding(
                        session.ModuleVersionId(),
                        token);
                    return new AssemblyContextDeclaredMethodPopulationResult
                        .Available(
                            binding,
                            session.DeclaredMethods(
                                new(
                                    binding,
                                    terminal,
                                    maximumRows)));
                }

                return declaration switch
                {
                    TypeDeclarationResult.Missing =>
                        new AssemblyContextDeclaredMethodPopulationResult
                            .Missing(),
                    TypeDeclarationResult.Ambiguous ambiguous =>
                        new AssemblyContextDeclaredMethodPopulationResult
                            .Ambiguous(ambiguous.Candidates.Length),
                    TypeDeclarationResult.Forwarded
                        or TypeDeclarationResult.ExportedFromModule =>
                        new AssemblyContextDeclaredMethodPopulationResult
                            .NotLocalDefinition(),
                    TypeDeclarationResult.BudgetExceeded exceeded =>
                        new AssemblyContextDeclaredMethodPopulationResult
                            .Incomplete(exceeded.Budget),
                    TypeDeclarationResult.Rejected =>
                        new AssemblyContextDeclaredMethodPopulationResult
                            .Rejected(),
                    _ => throw new InvalidOperationException(
                        "Unknown TypeDef declaration result."),
                };
            });
    }
}
