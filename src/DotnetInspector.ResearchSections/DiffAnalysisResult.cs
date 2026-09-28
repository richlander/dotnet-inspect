using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.ResearchSections;

/// <summary>
/// The closed owner-issued keyed Finding comparison a Compare participation
/// produces: Metadata's <see cref="ApiFindingComparison"/>, or Research's
/// descriptor-keyed <see cref="RetainedFindingComparisonSet"/>. No other
/// comparison shape can be constructed, so an analysis without keyed
/// Finding correspondence cannot be registered for Compare.
/// </summary>
public abstract class KeyedFindingComparison
{
    private KeyedFindingComparison()
    {
    }

    public static KeyedFindingComparison Of(ApiFindingComparison comparison)
        => new Api(comparison);

    public static KeyedFindingComparison Of(RetainedFindingComparisonSet comparisons)
        => new Retained(comparisons);

    public sealed class Api : KeyedFindingComparison
    {
        internal Api(ApiFindingComparison comparison)
        {
            Comparison = comparison
                ?? throw new ArgumentNullException(nameof(comparison));
        }

        public ApiFindingComparison Comparison { get; }
    }

    public sealed class Retained : KeyedFindingComparison
    {
        internal Retained(RetainedFindingComparisonSet comparisons)
        {
            Comparisons = comparisons
                ?? throw new ArgumentNullException(nameof(comparisons));
        }

        public RetainedFindingComparisonSet Comparisons { get; }
    }
}

/// <summary>What one Compare producer returned for one surface.</summary>
public abstract class DiffAnalysisProduction
{
    private DiffAnalysisProduction()
    {
    }

    public static DiffAnalysisProduction Compared(KeyedFindingComparison comparison)
        => new ComparedProduction(comparison);

    public static DiffAnalysisProduction Unavailable(string reason)
        => new UnavailableProduction(reason);

    public sealed class ComparedProduction : DiffAnalysisProduction
    {
        internal ComparedProduction(KeyedFindingComparison comparison)
        {
            Comparison = comparison
                ?? throw new ArgumentNullException(nameof(comparison));
        }

        public KeyedFindingComparison Comparison { get; }
    }

    public sealed class UnavailableProduction : DiffAnalysisProduction
    {
        internal UnavailableProduction(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);
            Reason = reason;
        }

        public string Reason { get; }
    }
}

/// <summary>
/// Host-resolved, typed Diff inputs every Compare producer may read. Member
/// and type targets are resolved before any producer runs; a resolution
/// failure is a request failure, never an analysis outcome.
/// </summary>
public sealed class DiffAnalysisInput
{
    public DiffAnalysisInput(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        IReadOnlyList<string> fromPaths,
        IReadOnlyList<string> toPaths,
        IReadOnlySet<string> typeFilters,
        IEnumerable<string> typeNames,
        IReadOnlySet<string>? memberTargetIdentities,
        Func<IReadOnlyList<FindingDescriptor>, ResearchComparison>? prepareBodySignals)
    {
        FromSurface = fromSurface ?? throw new ArgumentNullException(nameof(fromSurface));
        ToSurface = toSurface ?? throw new ArgumentNullException(nameof(toSurface));
        FromPaths = fromPaths ?? throw new ArgumentNullException(nameof(fromPaths));
        ToPaths = toPaths ?? throw new ArgumentNullException(nameof(toPaths));
        TypeFilters = typeFilters ?? throw new ArgumentNullException(nameof(typeFilters));
        TypeNames = [.. typeNames ?? throw new ArgumentNullException(nameof(typeNames))];
        MemberTargetIdentities = memberTargetIdentities;
        PrepareBodySignals = prepareBodySignals;
    }

    public ApiSurface FromSurface { get; }
    public ApiSurface ToSurface { get; }
    public IReadOnlyList<string> FromPaths { get; }
    public IReadOnlyList<string> ToPaths { get; }
    public IReadOnlySet<string> TypeFilters { get; }

    /// <summary>Exact Type names resolved from the request's type filters.</summary>
    public ImmutableArray<string> TypeNames { get; }

    /// <summary>Resolved member identities of a Member-surface request.</summary>
    public IReadOnlySet<string>? MemberTargetIdentities { get; }

    /// <summary>
    /// Resolves member targets and runs the one shared body-signal comparison
    /// retaining the given descriptors. It throws for a target failure, which
    /// is a request failure raised before any producer executes.
    /// </summary>
    public Func<IReadOnlyList<FindingDescriptor>, ResearchComparison>?
        PrepareBodySignals { get; }
}

/// <summary>The input one producer receives for one selected analysis.</summary>
public sealed class DiffAnalysisProducerContext
{
    internal DiffAnalysisProducerContext(
        DiffAnalysisInput input,
        AnalysisSurfaceParticipation participation,
        ResearchComparison? bodySignals)
    {
        Input = input;
        Participation = participation;
        BodySignals = bodySignals;
    }

    public DiffAnalysisInput Input { get; }

    public AnalysisSurfaceParticipation Participation { get; }

    /// <summary>The shared body-signal comparison, when a body analysis was selected.</summary>
    public ResearchComparison? BodySignals { get; }
}

/// <summary>One selected analysis's outcome, in selection order.</summary>
public abstract class DiffAnalysisOutcome
{
    private DiffAnalysisOutcome(
        AnalysisDescriptor analysis,
        AnalysisSurfaceParticipation participation)
    {
        Analysis = analysis;
        Participation = participation;
    }

    public AnalysisDescriptor Analysis { get; }

    /// <summary>The surface declaration, including its ordered Finding descriptors.</summary>
    public AnalysisSurfaceParticipation Participation { get; }

    public string Identity => Analysis.Id.Value;

    public sealed class Compared : DiffAnalysisOutcome
    {
        internal Compared(
            AnalysisDescriptor analysis,
            AnalysisSurfaceParticipation participation,
            KeyedFindingComparison comparison)
            : base(analysis, participation)
        {
            Comparison = comparison;
        }

        public KeyedFindingComparison Comparison { get; }
    }

    public sealed class Unavailable : DiffAnalysisOutcome
    {
        internal Unavailable(
            AnalysisDescriptor analysis,
            AnalysisSurfaceParticipation participation,
            string reason)
            : base(analysis, participation)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    public sealed class Failed : DiffAnalysisOutcome
    {
        internal Failed(
            AnalysisDescriptor analysis,
            AnalysisSurfaceParticipation participation,
            string diagnostic)
            : base(analysis, participation)
        {
            Diagnostic = diagnostic;
        }

        public string Diagnostic { get; }
    }
}

/// <summary>
/// Diff's own result for an analysis-selected request: one outcome per
/// selected analysis, in selection order. It is not a universal diff type.
/// </summary>
public sealed class DiffAnalysisResult
{
    internal DiffAnalysisResult(
        AnalysisReportSurfaceKind surface,
        ImmutableArray<DiffAnalysisOutcome> outcomes)
    {
        Surface = surface;
        Outcomes = outcomes;
    }

    public AnalysisReportSurfaceKind Surface { get; }

    public ImmutableArray<DiffAnalysisOutcome> Outcomes { get; }
}

/// <summary>
/// Executes one validated Diff analysis set by dispatching each selected
/// analysis's registered Compare producer exactly once.
/// </summary>
public static class DiffAnalysisOperation
{
    public static DiffAnalysisResult Execute(
        InspectionCapabilityCatalog catalog,
        AnalysisSetValidationResult.Accepted selection,
        DiffAnalysisInput input)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(input);
        if (selection.Operation != AnalysisOperationKind.Compare)
        {
            throw new ArgumentException(
                "Diff executes only Compare analysis sets.",
                nameof(selection));
        }

        var bindings = selection.Analyses
            .Select(analysis =>
            {
                InspectionAnalysisRegistration registration =
                    catalog.FindAnalysis(analysis)
                    ?? throw new ArgumentException(
                        $"Analysis '{analysis.Id.Value}' is not registered.",
                        nameof(selection));
                var producer = registration.ProducerFor(
                        AnalysisOperationKind.Compare,
                        selection.Surface)
                    as InspectionAnalysisProducerBinding<
                        DiffAnalysisProducerContext,
                        DiffAnalysisProduction>
                    ?? throw new InvalidOperationException(
                        $"Analysis '{analysis.Id.Value}' has no Diff Compare "
                        + $"producer at the {selection.Surface} surface.");
                return (Analysis: analysis, Producer: producer);
            })
            .ToArray();

        FindingDescriptor[] bodyDescriptors =
        [
            .. bindings
                .Where(binding => binding.Producer.Participation.ProducerRoute
                    == DiffAnalysisCatalog.BodySignalRoute)
                .SelectMany(binding => binding.Producer.Participation.Descriptors),
        ];
        ResearchComparison? bodySignals = null;
        string? bodyPreparationFailure = null;
        if (bodyDescriptors.Length > 0)
        {
            var prepare = input.PrepareBodySignals
                ?? throw new InvalidOperationException(
                    "Body analyses require body-signal preparation.");
            try
            {
                bodySignals = prepare(bodyDescriptors);
            }
            catch (DiffAnalysisTargetException)
            {
                // A member-target resolution failure fails the request.
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Any other shared preparation failure is each body
                // analysis's own producer failure.
                bodyPreparationFailure = exception.Message;
            }
        }

        var outcomes = ImmutableArray.CreateBuilder<DiffAnalysisOutcome>(bindings.Length);
        foreach (var (analysis, producer) in bindings)
        {
            AnalysisSurfaceParticipation participation = producer.Participation;
            DiffAnalysisOutcome outcome;
            if (bodyPreparationFailure is not null
                && participation.ProducerRoute == DiffAnalysisCatalog.BodySignalRoute)
            {
                outcomes.Add(new DiffAnalysisOutcome.Failed(
                    analysis,
                    participation,
                    bodyPreparationFailure));
                continue;
            }
            try
            {
                DiffAnalysisProduction production = producer.Produce(
                    new DiffAnalysisProducerContext(input, participation, bodySignals));
                outcome = production switch
                {
                    DiffAnalysisProduction.ComparedProduction compared =>
                        new DiffAnalysisOutcome.Compared(
                            analysis,
                            participation,
                            RequireDeclared(compared.Comparison, participation)),
                    DiffAnalysisProduction.UnavailableProduction unavailable =>
                        new DiffAnalysisOutcome.Unavailable(
                            analysis,
                            participation,
                            unavailable.Reason),
                    _ => throw new InvalidOperationException(
                        "Unknown Diff analysis production."),
                };
            }
            catch (DiffAnalysisTargetException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                outcome = new DiffAnalysisOutcome.Failed(
                    analysis,
                    participation,
                    exception.Message);
            }
            outcomes.Add(outcome);
        }

        return new DiffAnalysisResult(selection.Surface, outcomes.MoveToImmutable());
    }

    static KeyedFindingComparison RequireDeclared(
        KeyedFindingComparison comparison,
        AnalysisSurfaceParticipation participation)
    {
        if (comparison is KeyedFindingComparison.Retained retained)
        {
            HashSet<string> declared =
            [
                .. participation.Descriptors.Select(descriptor => descriptor.Id),
            ];
            string? undeclared = retained.Comparisons.DescriptorIds
                .FirstOrDefault(id => !declared.Contains(id));
            if (undeclared is not null)
            {
                throw new InvalidOperationException(
                    $"The producer returned undeclared Finding descriptor '{undeclared}'.");
            }
        }
        return comparison;
    }
}

/// <summary>
/// A member-target resolution failure. It fails the whole Diff request
/// instead of becoming one analysis's outcome.
/// </summary>
public sealed class DiffAnalysisTargetException : InvalidOperationException
{
    public DiffAnalysisTargetException(string message)
        : base(message)
    {
    }
}
