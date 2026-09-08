using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

public sealed class PackageAssemblyQueryPlan
{
    internal PackageAssemblyQueryPlan(
        PackageAssemblyPatternRequest pattern,
        ImmutableArray<PackageSourceCoordinate> coordinates,
        string targetFramework,
        string? runtimeIdentifier,
        PackageAssemblyEvaluationBudget budget)
    {
        Pattern = pattern;
        Coordinates = coordinates;
        TargetFramework = targetFramework;
        RuntimeIdentifier = runtimeIdentifier;
        Budget = budget;
    }

    public PackageAssemblyPatternRequest Pattern { get; }
    public ImmutableArray<PackageSourceCoordinate> Coordinates { get; }
    public string TargetFramework { get; }
    public string? RuntimeIdentifier { get; }
    public PackageAssemblyEvaluationBudget Budget { get; }
}

public sealed record PackageAssemblyQueryAcquisitionFailure(
    PackageSourceCoordinate Coordinate,
    InertString Producer,
    InertString Message,
    PackageSourceFailureKind? SourceFailureKind);

public sealed record PackageAssemblyQuerySummary(
    int Candidates,
    int Matches,
    int SemanticMisses,
    int NotApplicable,
    int Failures);

public abstract record PackageAssemblyQueryEvent
{
    private protected PackageAssemblyQueryEvent() { }

    public sealed record Progress(int CompletedCandidates, int Limit)
        : PackageAssemblyQueryEvent;

    public sealed record Evaluated(PackageAssemblyEvaluationOutcome Value)
        : PackageAssemblyQueryEvent;

    public sealed record AcquisitionFailed(PackageAssemblyQueryAcquisitionFailure Value)
        : PackageAssemblyQueryEvent;

    public sealed record Completed(PackageAssemblyQuerySummary Value)
        : PackageAssemblyQueryEvent;
}

/// <summary>Serial, disposable-candidate composition over a finite explicit package selection.</summary>
public static class PackageAssemblyQuery
{
    public const int MaximumPackages = 5;

    static readonly PackagePayloadLimits PayloadLimits = new()
    {
        MaxArchiveBytes = 32L * 1024 * 1024,
        MaxExpandedBytes = 256L * 1024 * 1024,
        MaxEntryCount = 4_096,
        MaxUniqueDirectories = 8_192,
    };

    public static PackageAssemblyQueryPlan Plan(
        string patternId,
        string operand,
        IReadOnlyList<string> packageCoordinates,
        string targetFramework,
        string? runtimeIdentifier = null,
        PackageAssemblyEvaluationBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(packageCoordinates);
        if (packageCoordinates.Count is < 1 or > MaximumPackages)
        {
            throw new ArgumentException(
                $"An assembly query requires between 1 and {MaximumPackages} explicit ID@VERSION packages.",
                nameof(packageCoordinates));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(targetFramework);
        PackageAssemblyPatternRequest pattern =
            PackageAssemblyPatterns.CreateRequest(patternId, operand);
        var coordinates = ImmutableArray.CreateBuilder<PackageSourceCoordinate>(
            packageCoordinates.Count);
        var distinct = new HashSet<PackageSourceCoordinate>();
        foreach (string text in packageCoordinates)
        {
            ArgumentNullException.ThrowIfNull(text);
            int separator = text.IndexOf('@');
            if (separator <= 0 || separator == text.Length - 1)
            {
                throw new ArgumentException(
                    "Each assembly-query package must be an exact ID@VERSION coordinate.",
                    nameof(packageCoordinates));
            }

            var requested = new PackageCoordinate(
                text[..separator],
                text[(separator + 1)..],
                targetFramework,
                runtimeIdentifier);
            if (PackageCoordinateResolver.Validate(requested) is { } invalid)
            {
                throw new ArgumentException(
                    invalid.Message,
                    nameof(packageCoordinates));
            }

            PackageSourceCoordinate coordinate = PackageSourceCoordinate.Create(
                requested.PackageId,
                requested.Version!);
            if (!distinct.Add(coordinate))
            {
                throw new ArgumentException(
                    "An assembly query cannot contain duplicate package coordinates.",
                    nameof(packageCoordinates));
            }
            coordinates.Add(coordinate);
        }

        return new PackageAssemblyQueryPlan(
            pattern,
            coordinates.MoveToImmutable(),
            targetFramework,
            runtimeIdentifier,
            budget ?? PackageAssemblyEvaluationBudget.Default);
    }

    public static async IAsyncEnumerable<PackageAssemblyQueryEvent> ExecuteAsync(
        IPackageRootPayloadProvider payloadProvider,
        PackageAssemblyQueryPlan plan,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payloadProvider);
        ArgumentNullException.ThrowIfNull(plan);

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        operation.CancelAfter(plan.Budget.MaximumDuration);
        long started = Stopwatch.GetTimestamp();
        int candidates = 0;
        int matches = 0;
        int misses = 0;
        int notApplicable = 0;
        int failures = 0;

        ObserveCancellation();
        yield return new PackageAssemblyQueryEvent.Progress(
            candidates, plan.Coordinates.Length);
        foreach (PackageSourceCoordinate coordinate in plan.Coordinates)
        {
            ObserveCancellation();
            PackageAssemblyQueryEvent item = await AcquireAndEvaluateAsync(
                payloadProvider,
                coordinate,
                plan,
                operation.Token).ConfigureAwait(false);
            ObserveCancellation();
            candidates++;
            switch (item)
            {
                case PackageAssemblyQueryEvent.Evaluated
                    { Value: PackageAssemblyEvaluationOutcome.Matched }:
                    matches++;
                    break;
                case PackageAssemblyQueryEvent.Evaluated
                    { Value: PackageAssemblyEvaluationOutcome.NoMatch }:
                    misses++;
                    break;
                case PackageAssemblyQueryEvent.Evaluated
                    { Value: PackageAssemblyEvaluationOutcome.NotApplicable }:
                    notApplicable++;
                    break;
                case PackageAssemblyQueryEvent.Evaluated
                    { Value: PackageAssemblyEvaluationOutcome.Failure }:
                case PackageAssemblyQueryEvent.AcquisitionFailed:
                    failures++;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown package assembly query outcome.");
            }

            yield return item;
            ObserveCancellation();
            yield return new PackageAssemblyQueryEvent.Progress(
                candidates, plan.Coordinates.Length);
        }

        ObserveCancellation();
        yield return new PackageAssemblyQueryEvent.Completed(
            new(candidates, matches, misses, notApplicable, failures));

        void ObserveCancellation()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= plan.Budget.MaximumDuration)
            {
                operation.Cancel();
                throw new OperationCanceledException(
                    "The package assembly query exceeded its operation deadline.",
                    operation.Token);
            }
            operation.Token.ThrowIfCancellationRequested();
        }
    }

    static async Task<PackageAssemblyQueryEvent> AcquireAndEvaluateAsync(
        IPackageRootPayloadProvider payloadProvider,
        PackageSourceCoordinate coordinate,
        PackageAssemblyQueryPlan plan,
        CancellationToken cancellationToken)
    {
        PackageRootPayloadResult acquired =
            await payloadProvider.GetPayloadAsync(
            coordinate,
            requiredProducerKey: null,
            PayloadLimits,
            cancellationToken).ConfigureAwait(false);
        switch (acquired)
        {
            case PackageRootPayloadResult.Available available:
                PackageRootBinding binding = PackageRootBinding.CreateFromSource(
                    available.Payload,
                    plan.TargetFramework,
                    plan.RuntimeIdentifier);
                return new PackageAssemblyQueryEvent.Evaluated(
                    await PackageAssemblyEvaluator.EvaluateAsync(
                        binding,
                        plan.Pattern,
                        plan.Budget,
                        cancellationToken).ConfigureAwait(false));
            case PackageRootPayloadResult.Unavailable unavailable:
                return new PackageAssemblyQueryEvent.AcquisitionFailed(
                    new(
                        coordinate,
                        unavailable.Producer,
                        new InertString(TextPolicy.Prose, unavailable.Message),
                        unavailable.SourceFailureKind));
            default:
                throw new InvalidOperationException(
                    "Unknown package assembly-query payload outcome.");
        }
    }
}
