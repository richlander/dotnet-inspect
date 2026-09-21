using System.Collections.Immutable;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// One normalized dependency declaration selected by a host for PackageHouse
/// pruning evaluation.
/// </summary>
public sealed record PackageDependencyPruningInspectionSubject
{
    public PackageDependencyPruningInspectionSubject(
        PackageDependencyEvidenceRoot root,
        PackageDependencyEvidenceDeclaration declaration)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Declaration = declaration
            ?? throw new ArgumentNullException(nameof(declaration));
    }

    public PackageDependencyEvidenceRoot Root { get; }

    public PackageDependencyEvidenceDeclaration Declaration { get; }
}

/// <summary>
/// One bounded, host-authorized package dependency pruning operation.
/// </summary>
public sealed record PackageDependencyPruningInspectionRequest
{
    public PackageDependencyPruningInspectionRequest(
        IEnumerable<PackageDependencyPruningInspectionSubject> subjects,
        PackageHouseTargetContext target,
        PlatformPruneInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        Subjects = subjects.ToImmutableArray();
        if (Subjects.IsDefault || Subjects.Any(static subject => subject is null))
        {
            throw new ArgumentException(
                "Package dependency pruning subjects must be initialized.",
                nameof(subjects));
        }

        Target = target ?? throw new ArgumentNullException(nameof(target));
        if (Target.Mode != PackageHouseTargetSelectionMode.Exact
            || Target.PlatformTarget is null)
        {
            throw new ArgumentException(
                "Package dependency pruning requires one exact package and platform target.",
                nameof(target));
        }

        Inventory = inventory
            ?? throw new ArgumentNullException(nameof(inventory));
    }

    public ImmutableArray<PackageDependencyPruningInspectionSubject> Subjects
        { get; }

    public PackageHouseTargetContext Target { get; }

    public PlatformPruneInventory Inventory { get; }
}

/// <summary>
/// The ordered outcomes from one package dependency pruning operation.
/// </summary>
public sealed record PackageDependencyPruningInspectionResult
{
    internal PackageDependencyPruningInspectionResult(
        IEnumerable<PackageDependencyPruningInspectionOutcome> outcomes)
    {
        Outcomes = outcomes.ToImmutableArray();
    }

    public ImmutableArray<PackageDependencyPruningInspectionOutcome> Outcomes
        { get; }
}

/// <summary>
/// One declaration's candidate resolution and PackageHouse pruning outcome.
/// </summary>
public abstract record PackageDependencyPruningInspectionOutcome
{
    private protected PackageDependencyPruningInspectionOutcome(
        PackageDependencyPruningInspectionSubject subject,
        PackageHouseDependencyPruningApplicability applicability)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Applicability = applicability
            ?? throw new ArgumentNullException(nameof(applicability));
        if (!ReferenceEquals(Subject.Root, Applicability.Root)
            || !ReferenceEquals(
                Subject.Declaration,
                Applicability.Declaration))
        {
            throw new ArgumentException(
                "Package dependency pruning applicability belongs to another subject.",
                nameof(applicability));
        }
    }

    public PackageDependencyPruningInspectionSubject Subject { get; }

    public PackageHouseDependencyPruningApplicability Applicability { get; }

    public sealed record NotEvaluated
        : PackageDependencyPruningInspectionOutcome
    {
        internal NotEvaluated(
            PackageDependencyPruningInspectionSubject subject,
            PackageHouseDependencyPruningApplicability applicability)
            : base(subject, applicability)
        {
            if (applicability.State
                == PackageHouseDependencyPruningApplicabilityState
                    .CandidateRequired)
            {
                throw new ArgumentException(
                    "A candidate-required pruning subject cannot be reported as not evaluated.",
                    nameof(applicability));
            }
        }
    }

    public sealed record CandidateUnavailable
        : PackageDependencyPruningInspectionOutcome
    {
        internal CandidateUnavailable(
            PackageDependencyPruningInspectionSubject subject,
            PackageHouseDependencyPruningApplicability applicability,
            PackageDependencyCandidateResult candidate)
            : base(subject, applicability)
        {
            if (applicability.State
                    != PackageHouseDependencyPruningApplicabilityState
                        .CandidateRequired
                || candidate
                    is PackageDependencyCandidateResult.Resolved)
            {
                throw new ArgumentException(
                    "A candidate-unavailable pruning outcome requires a non-resolved candidate result.",
                    nameof(candidate));
            }
            if (!ReferenceEquals(subject.Declaration, candidate.Declaration))
            {
                throw new ArgumentException(
                    "The candidate result belongs to another dependency declaration.",
                    nameof(candidate));
            }

            Candidate = candidate;
        }

        public PackageDependencyCandidateResult Candidate { get; }
    }

    public sealed record Evaluated
        : PackageDependencyPruningInspectionOutcome
    {
        internal Evaluated(
            PackageDependencyPruningInspectionSubject subject,
            PackageHouseDependencyPruningApplicability applicability,
            PackageDependencyCandidateResult.Resolved candidate,
            PackageHouseDependencyPruningResult.Evaluated result)
            : base(subject, applicability)
        {
            if (applicability.State
                    != PackageHouseDependencyPruningApplicabilityState
                        .CandidateRequired
                || !ReferenceEquals(
                    subject.Declaration,
                    candidate.Declaration)
                || !ReferenceEquals(
                    candidate,
                    ((PackageHouseDependencySubject.Declaration)
                        result.Input.Subject).Resolution))
            {
                throw new ArgumentException(
                    "The evaluated pruning result belongs to another dependency subject.",
                    nameof(result));
            }

            Candidate = candidate;
            Result = result;
        }

        public PackageDependencyCandidateResult.Resolved Candidate { get; }

        public PackageHouseDependencyPruningResult.Evaluated Result { get; }
    }
}

/// <summary>
/// Resolves candidates and applies PackageHouse pruning for one bounded
/// host-authorized dependency set.
/// </summary>
public static class PackageDependencyPruningInspection
{
    public static async ValueTask<
        InspectionEnvelope<PackageDependencyPruningInspectionResult>>
        ExecuteAsync(
            PackageDependencyPruningInspectionRequest request,
            IPackageDependencyCandidateSource candidateSource,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(candidateSource);

        var outcomes =
            ImmutableArray.CreateBuilder<
                PackageDependencyPruningInspectionOutcome>(
                    request.Subjects.Length);
        foreach (PackageDependencyPruningInspectionSubject subject
                 in request.Subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageHouseDependencyPruningApplicability applicability =
                PackageHouseDependencyPruningApplicabilityQuery.Execute(
                    subject.Root,
                    subject.Declaration,
                    request.Target);
            if (applicability.State
                    != PackageHouseDependencyPruningApplicabilityState
                        .CandidateRequired)
            {
                outcomes.Add(
                    new PackageDependencyPruningInspectionOutcome
                        .NotEvaluated(subject, applicability));
                continue;
            }

            PackageDependencyCandidateResult candidate =
                await PackageDependencyCandidateQuery.ExecuteAsync(
                    new PackageDependencyCandidateRequest.Declared(
                        subject.Declaration),
                    candidateSource,
                    cancellationToken,
                    operationContext).ConfigureAwait(false);
            if (candidate
                is not PackageDependencyCandidateResult.Resolved resolved)
            {
                outcomes.Add(
                    new PackageDependencyPruningInspectionOutcome
                        .CandidateUnavailable(
                            subject,
                            applicability,
                            candidate));
                continue;
            }

            PackageHouseDependencyInput input =
                PackageHouseDependencyInputAdapter.Create(
                    subject.Root,
                    resolved,
                    PackageHouseOperation.Create(
                        PackageHouseOperationProfile.Settle),
                    request.Target);
            PackageHouseDependencyPruningResult.Evaluated evaluated =
                PackageHouseDependencyPruningQuery.Execute(
                    input,
                    request.Inventory)
                as PackageHouseDependencyPruningResult.Evaluated
                ?? throw new InvalidOperationException(
                    "A candidate-required dependency with an exact platform target and inventory did not produce an evaluated PackageHouse pruning result.");
            outcomes.Add(
                new PackageDependencyPruningInspectionOutcome.Evaluated(
                    subject,
                    applicability,
                    resolved,
                    evaluated));
        }

        return new(
            new ResourcePath("package-dependency-pruning"),
            InspectionContentKind.Document,
            new PackageDependencyPruningInspectionResult(outcomes),
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
