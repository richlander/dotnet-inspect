using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Content-shaped inputs for comparing C# and IL implementation evidence
/// across two already-acquired assembly versions.
/// </summary>
public sealed record ImplementationComparisonInput(
    IReadOnlyList<ImplementationAssemblyInput> OldAssemblies,
    IReadOnlyList<ImplementationAssemblyInput> NewAssemblies,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlyList<ComparisonMemberSelection>? MemberSelections = null);

public abstract class ImplementationComparisonResult
{
    private protected ImplementationComparisonResult()
    {
    }

    public sealed class Compared : ImplementationComparisonResult
    {
        internal Compared(
            ImplementationDiffResult comparison,
            ResearchTargetResolution? resolution,
            ResearchProducerCompletion? producerCompletion)
        {
            Comparison = comparison;
            Resolution = resolution;
            ProducerCompletion = producerCompletion;
        }

        public ImplementationDiffResult Comparison { get; }
        public ResearchTargetResolution? Resolution { get; }
        public ResearchProducerCompletion? ProducerCompletion { get; }
    }

    public sealed class PopulationRejected : ImplementationComparisonResult
    {
        internal PopulationRejected(QueryPopulationRejection rejection)
            => Rejection = rejection;

        public QueryPopulationRejection Rejection { get; }
    }

    public sealed class ProjectionRejected : ImplementationComparisonResult
    {
        internal ProjectionRejected(QueryPopulationProjectionRejection reason)
            => Reason = reason;

        public QueryPopulationProjectionRejection Reason { get; }
    }

    public sealed class AdmissionRejected : ImplementationComparisonResult
    {
        internal AdmissionRejected(ResearchAdmissionRejection rejection)
            => Rejection = rejection;

        public ResearchAdmissionRejection Rejection { get; }
    }

    public sealed class PlanningRejected : ImplementationComparisonResult
    {
        internal PlanningRejected(ResearchTargetPlanningRejection rejection)
            => Rejection = rejection;

        public ResearchTargetPlanningRejection Rejection { get; }
    }

    public sealed class TargetFailed : ImplementationComparisonResult
    {
        internal TargetFailed(
            ResearchTargetResolution resolution,
            string summary)
        {
            Resolution = resolution;
            Summary = summary;
        }

        public ResearchTargetResolution Resolution { get; }
        public string Summary { get; }
    }

    public sealed class ProducerRejected : ImplementationComparisonResult
    {
        internal ProducerRejected(ResearchProducerRejection rejection)
            => Rejection = rejection;

        public ResearchProducerRejection Rejection { get; }
    }

    public sealed class ProducerFailed : ImplementationComparisonResult
    {
        internal ProducerFailed(ResearchProducerDiagnostic diagnostic)
            => Diagnostic = diagnostic;

        public ResearchProducerDiagnostic Diagnostic { get; }
    }

    public sealed class Cancelled : ImplementationComparisonResult
    {
        internal Cancelled()
        {
        }
    }
}

/// <summary>
/// Compares implementation evidence while retaining the Research-owned result,
/// target resolution, and native producer completion.
/// </summary>
public static class ImplementationComparisonQuery
{
    public static InspectionQuery<ImplementationComparisonResult> Definition
    { get; } =
        new("Implementation comparison", InspectionCost.Unbounded);

    public static ImplementationComparisonResult Execute(
        ImplementationComparisonInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.OldAssemblies);
        ArgumentNullException.ThrowIfNull(input.NewAssemblies);
        cancellationToken.ThrowIfCancellationRequested();

        QueryPopulationSealingOutcome sealedOutcome =
            QueryComparisonPopulationSealer.Seal(
                input.OldAssemblies.Select(ToBinding).ToArray(),
                input.NewAssemblies.Select(ToBinding).ToArray(),
                input.TypeFilters,
                QueryComparisonProfile.ImplementationComparison,
                static binding => binding.Assembly is null
                    ? QueryPopulationRejectionKind.MissingAssembly
                    : binding.Resolver is null
                        ? QueryPopulationRejectionKind.MissingResolver
                        : binding.MethodPopulation is null
                            ? QueryPopulationRejectionKind
                                .MissingMethodPopulation
                            : null);
        if (sealedOutcome is QueryPopulationSealingOutcome.Rejected rejected)
        {
            return new ImplementationComparisonResult.PopulationRejected(
                rejected.Rejection);
        }

        var population =
            (QueryComparisonPopulation<ImplementationComparisonBinding>)
            ((QueryPopulationSealingOutcome.Sealed)sealedOutcome).Population;
        return input.MemberSelections is { Count: > 0 } selections
            ? Targeted(input, population, selections, cancellationToken)
            : new ImplementationComparisonResult.Compared(
                ImplementationDiff.Compare(
                    input.OldAssemblies,
                    input.NewAssemblies,
                    new ImplementationDiffOptions(
                        TypeFilters: input.TypeFilters)),
                resolution: null,
                producerCompletion: null);
    }

    static ImplementationComparisonBinding ToBinding(
        ImplementationAssemblyInput input)
        => new(input.Assembly, input.Resolver, input.MethodPopulation);

    static ImplementationComparisonResult Targeted(
        ImplementationComparisonInput input,
        QueryComparisonPopulation<ImplementationComparisonBinding> population,
        IReadOnlyList<ComparisonMemberSelection> selections,
        CancellationToken cancellationToken)
    {
        QueryResearchTargetPlanningStep step = QueryResearchTargetPlanner.Plan(
            population,
            selections,
            cancellationToken);
        switch (step)
        {
            case QueryResearchTargetPlanningStep.ProjectionRejected rejected:
                return new ImplementationComparisonResult.ProjectionRejected(
                    rejected.Reason);
            case QueryResearchTargetPlanningStep.AdmissionRejected rejected:
                return new ImplementationComparisonResult.AdmissionRejected(
                    rejected.Rejection);
            case QueryResearchTargetPlanningStep.PlanningRejected rejected:
                return new ImplementationComparisonResult.PlanningRejected(
                    rejected.Rejection);
        }

        var resolved = (QueryResearchTargetPlanningStep.Resolved)step;
        ResearchTargetScope[] unresolvedScopes =
        [
            .. resolved.Resolution.Scopes.Where(scope =>
                !resolved.Resolution.Attempts.Any(attempt =>
                    attempt.Request.Scope == scope.Id
                    && attempt.Outcome is ResearchTargetOutcome.Resolved
                    {
                        BodyIdentity: not null,
                    })),
        ];
        if (unresolvedScopes.Length > 0)
        {
            return new ImplementationComparisonResult.TargetFailed(
                resolved.Resolution,
                $"{unresolvedScopes.Length} selected member scope(s) did not "
                    + "resolve to an Analysis method. "
                    + string.Join(
                        "; ",
                        resolved.Resolution.Attempts
                            .Where(attempt => unresolvedScopes.Any(
                                scope => scope.Id == attempt.Request.Scope))
                            .Select(attempt =>
                            attempt.Outcome switch
                            {
                                ResearchTargetOutcome.Resolved target =>
                                    $"{attempt.Request.Side}: resolved "
                                        + $"{target.Anchor.StableSelector}, "
                                        + $"role {target.Role}, "
                                        + $"address "
                                        + $"{target.Address?.Token.ToString("X8")
                                            ?? "none"}, "
                                        + $"Analysis identity "
                                        + $"{(target.BodyIdentity is null
                                            ? "unavailable"
                                            : "available")}",
                                _ => $"{attempt.Request.Side}: "
                                    + attempt.Outcome.Kind,
                            })));
        }

        ResearchProducerSessionOutcome session =
            ResearchProducerSession.Run(
                new ResearchProducerSessionRequest(
                    resolved.Projected.Admission,
                    resolved.Resolution,
                    ResearchProducerCatalog.Kinds),
                cancellationToken);
        if (session is ResearchProducerSessionOutcome.Rejected producerRejected)
        {
            return new ImplementationComparisonResult.ProducerRejected(
                producerRejected.Rejection);
        }
        if (session is ResearchProducerSessionOutcome.Failed failed)
        {
            return new ImplementationComparisonResult.ProducerFailed(
                failed.Diagnostic);
        }
        if (session is ResearchProducerSessionOutcome.Cancelled)
            return new ImplementationComparisonResult.Cancelled();

        ResearchProducerCompletion completion =
            ((ResearchProducerSessionOutcome.Completed)session).Completion;
        ResearchComparison research = ResearchDiff.FromProducerCompletion(
            completion,
            resolved.Resolution,
            resolved.Projected.Admission);
        ImplementationDiffResult comparison =
            ImplementationDiff.FromResearchComparison(
                research,
                new ImplementationDiffOptions(
                    TypeFilters: input.TypeFilters));
        comparison = comparison with
        {
            Complexity = ImplementationComplexityService.Execute(
                new ImplementationComplexityComparisonRequest(
                    [.. input.OldAssemblies.Select(
                        static assembly => assembly.ProfileAnalysis)],
                    [.. input.NewAssemblies.Select(
                        static assembly => assembly.ProfileAnalysis)],
                    input.TypeFilters,
                    TargetResolution: resolved.Resolution)),
        };
        return new ImplementationComparisonResult.Compared(
            comparison,
            resolved.Resolution,
            completion);
    }
}
