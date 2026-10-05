using System.Collections.Immutable;

using Analysis = ILInspector.Analysis;
using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public enum PackagePairDirectUseClusterRequestFailureKind
{
    SamePackage,
    PackageOutsideProjection,
    IncompatibleTargetFrameworks,
}

public sealed class PackagePairDirectUseClusterRequestException
    : ArgumentException
{
    internal PackagePairDirectUseClusterRequestException(
        PackagePairDirectUseClusterRequestFailureKind kind,
        string message,
        string parameterName)
        : base(message, parameterName)
    {
        Kind = kind;
    }

    public PackagePairDirectUseClusterRequestFailureKind Kind { get; }
}

public sealed record PackagePairDirectUseClusterLimits
{
    public PackagePairDirectUseClusterLimits(
        int maximumLibraryPairs = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumLibraryPairs,
            1);
        MaximumLibraryPairs = maximumLibraryPairs;
    }

    public int MaximumLibraryPairs { get; }
}

public sealed record PackagePairPackageDescriptor(
    RealizedMemberCoordinate.Package Coordinate,
    string PackageId,
    string PackageVersion,
    string? RequestedTargetFramework,
    string SelectedTargetFramework,
    string? RuntimeIdentifier);

public sealed record PackagePairLibraryDescriptor(
    PackagePairPackageDescriptor Package,
    PackageCompileAsset Asset,
    AssemblyReferenceIdentity AssemblyIdentity,
    Guid? ModuleVersionId);

public enum PackagePairLibraryFailureKind
{
    CandidateRejected,
    InvalidImage,
    AnalysisIncomplete,
    PairRejected,
}

public sealed record PackagePairLibraryFailure(
    PackagePairLibraryFailureKind Kind,
    PackagePairLibraryDescriptor? Library,
    string Message);

public sealed record PackagePairDirectCall(
    PackagePairLibraryDescriptor Source,
    Analysis.MethodIdentity SourceMethod,
    PackagePairLibraryDescriptor Target,
    Analysis.MethodIdentity TargetMethod,
    Analysis.DirectCall Call);

public sealed record PackagePairDirectUseClusterIdentity(
    PackagePairLibraryDescriptor Source,
    int AnchorSourceMethodToken,
    PackagePairLibraryDescriptor Target,
    int AnchorTargetMethodToken);

public sealed record PackagePairDirectUseCluster(
    PackagePairDirectUseClusterIdentity Identity,
    AssemblyPairDirectUseClusterDerivation Derivation,
    int Ordinal,
    int LibraryPairOrdinal,
    int LibraryPairClusterOrdinal,
    ImmutableArray<Analysis.MethodIdentity> SourceMethods,
    ImmutableArray<Analysis.TypeRef> TargetTypes,
    ImmutableArray<Analysis.MethodIdentity> TargetMethods,
    ImmutableArray<int> OccurrenceIndexes)
{
    public int ExtensionMethodCount =>
        TargetMethods.Count(static method => method.IsExtension);

    public int CallSiteCount => OccurrenceIndexes.Length;
}

public sealed record PackagePairLibraryPair(
    int Ordinal,
    PackagePairLibraryDescriptor First,
    PackagePairLibraryDescriptor Second,
    ImmutableArray<PackagePairLibraryFailure> Failures,
    ImmutableArray<PackagePairDirectCall> Occurrences,
    ImmutableArray<PackagePairDirectUseCluster> Clusters,
    AssemblyPairCallUseDiagnostics Diagnostics,
    bool IsComplete);

public sealed record PackagePairDirectUseClusterDocument(
    PackagePairPackageDescriptor FirstPackage,
    PackagePairPackageDescriptor SecondPackage,
    string TargetFramework,
    ImmutableArray<PackagePairLibraryDescriptor> Libraries,
    ImmutableArray<PackagePairLibraryPair> LibraryPairs)
{
    public ImmutableArray<PackagePairDirectUseCluster> Clusters =>
        [.. LibraryPairs.SelectMany(static pair => pair.Clusters)];

    public bool IsComplete =>
        LibraryPairs.All(static pair => pair.IsComplete);
}

public abstract record PackagePairDirectUseClusterOutcome
{
    public sealed record Available(
        PackagePairDirectUseClusterDocument Document)
        : PackagePairDirectUseClusterOutcome;

    public sealed record EndpointUnavailable(
        ImmutableArray<PackagePairPackageDescriptor> Packages)
        : PackagePairDirectUseClusterOutcome;

    public sealed record PairPopulationRejected(
        int FirstLibraryCount,
        int SecondLibraryCount,
        long RequiredLibraryPairs,
        int MaximumLibraryPairs)
        : PackagePairDirectUseClusterOutcome;
}

/// <summary>
/// Produces the complete canonical Direct Use Cluster matrix between the
/// implementation libraries of two exact Package roots.
/// </summary>
public static class PackagePairDirectUseClusterQuery
{
    public static InspectionQuery<PackagePairDirectUseClusterOutcome>
        Definition { get; } =
            new(
                "Package pair direct use clusters",
                InspectionCost.Unbounded);

    public static PackagePairDirectUseClusterOutcome Execute(
        PackageAssemblyContextProjection projection,
        PackageRootIdentity first,
        RealizedMemberCoordinate.Package firstCoordinate,
        PackageRootIdentity second,
        RealizedMemberCoordinate.Package secondCoordinate,
        PackagePairDirectUseClusterLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(firstCoordinate);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(secondCoordinate);
        limits ??= new();
        RequireCoordinate(first, firstCoordinate, nameof(firstCoordinate));
        RequireCoordinate(second, secondCoordinate, nameof(secondCoordinate));
        string targetFramework = SharedTargetFramework(
            first,
            firstCoordinate,
            second,
            secondCoordinate);

        if (string.Equals(
            first.PackageId,
            second.PackageId,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new PackagePairDirectUseClusterRequestException(
                PackagePairDirectUseClusterRequestFailureKind.SamePackage,
                "Package-pair call use requires two different Package IDs.",
                nameof(second));
        }

        PackageAssemblyContextRoleProjection? role =
            projection.ImplementationRole;
        ImmutableArray<PackageRootIdentity> roots = projection.Roots;
        ImmutableArray<PackageAssemblyRoleParticipant> participants =
            role?.Participants ?? [];
        RequirePackage(roots, first, nameof(first));
        RequirePackage(roots, second, nameof(second));

        if (role is null)
        {
            PackagePairPackageDescriptor firstPackage =
                Describe(
                    first,
                    firstCoordinate,
                    targetFramework,
                    SelectedFramework(first, []));
            PackagePairPackageDescriptor secondPackage =
                Describe(
                    second,
                    secondCoordinate,
                    targetFramework,
                    SelectedFramework(second, []));
            return new PackagePairDirectUseClusterOutcome.EndpointUnavailable(
                ComparePackages(firstPackage, secondPackage) <= 0
                    ? [firstPackage, secondPackage]
                    : [secondPackage, firstPackage]);
        }
        return role.Use(group => Execute(
                group,
                participants,
                first,
                firstCoordinate,
                second,
                secondCoordinate,
                targetFramework,
                limits));
    }

    static PackagePairDirectUseClusterOutcome Execute(
        AssemblyContextGroup group,
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        PackageRootIdentity first,
        RealizedMemberCoordinate.Package firstCoordinate,
        PackageRootIdentity second,
        RealizedMemberCoordinate.Package secondCoordinate,
        string targetFramework,
        PackagePairDirectUseClusterLimits limits)
    {
        ImmutableArray<PackageAssemblyRoleParticipant> firstParticipants =
            ParticipantsFor(participants, first);
        ImmutableArray<PackageAssemblyRoleParticipant> secondParticipants =
            ParticipantsFor(participants, second);
        string firstFramework = SelectedFramework(
            first,
            firstParticipants);
        string secondFramework = SelectedFramework(
            second,
            secondParticipants);

        PackagePairPackageDescriptor firstPackage =
            Describe(
                first,
                firstCoordinate,
                targetFramework,
                firstFramework);
        PackagePairPackageDescriptor secondPackage =
            Describe(
                second,
                secondCoordinate,
                targetFramework,
                secondFramework);
        bool reverse = ComparePackages(
            firstPackage,
            secondPackage) > 0;
        PackagePairPackageDescriptor canonicalFirstPackage =
            reverse ? secondPackage : firstPackage;
        PackagePairPackageDescriptor canonicalSecondPackage =
            reverse ? firstPackage : secondPackage;
        ImmutableArray<PackageAssemblyRoleParticipant>
            canonicalFirstParticipants =
                Order(reverse ? secondParticipants : firstParticipants);
        ImmutableArray<PackageAssemblyRoleParticipant>
            canonicalSecondParticipants =
                Order(reverse ? firstParticipants : secondParticipants);

        if (canonicalFirstParticipants.IsEmpty
            || canonicalSecondParticipants.IsEmpty)
        {
            var unavailable =
                ImmutableArray.CreateBuilder<PackagePairPackageDescriptor>(2);
            if (canonicalFirstParticipants.IsEmpty)
                unavailable.Add(canonicalFirstPackage);
            if (canonicalSecondParticipants.IsEmpty)
                unavailable.Add(canonicalSecondPackage);
            return new PackagePairDirectUseClusterOutcome
                .EndpointUnavailable(unavailable.MoveToImmutable());
        }

        long pairCount =
            (long)canonicalFirstParticipants.Length
            * canonicalSecondParticipants.Length;
        if (pairCount > limits.MaximumLibraryPairs)
        {
            return new PackagePairDirectUseClusterOutcome
                .PairPopulationRejected(
                    canonicalFirstParticipants.Length,
                    canonicalSecondParticipants.Length,
                    pairCount,
                    limits.MaximumLibraryPairs);
        }

        var executions = new List<PairExecution>((int)pairCount);
        var moduleVersions =
            new Dictionary<AssemblyAcquisitionRegistration, Guid>(
                ReferenceEqualityComparer.Instance);
        var analyses =
            new Dictionary<
                AssemblyAcquisitionRegistration,
                AssemblyContextCallGraphAnalysisResult>(
                    ReferenceEqualityComparer.Instance);
        foreach (PackageAssemblyRoleParticipant participant
            in canonicalFirstParticipants.Concat(
                canonicalSecondParticipants))
        {
            analyses.Add(
                participant.Participant.Assembly.Registration,
                AssemblyContextCallGraphAnalysis.Execute(
                    group,
                    participant.Participant));
        }
        foreach (PackageAssemblyRoleParticipant left
            in canonicalFirstParticipants)
        {
            foreach (PackageAssemblyRoleParticipant right
                in canonicalSecondParticipants)
            {
                try
                {
                    AssemblyPairCallUseResult result =
                        AssemblyPairCallUseQuery.ExecuteAnalyzed(
                            group,
                            analyses[
                                left.Participant.Assembly.Registration],
                            analyses[
                                right.Participant.Assembly.Registration]);
                    foreach (AssemblyPairCallUseParticipant participant
                        in result.Participants)
                    {
                        if (moduleVersions.TryGetValue(
                                participant.Subject.Registration,
                                out Guid observed)
                            && observed != participant.ModuleVersionId)
                        {
                            throw new InvalidOperationException(
                                "One Package-pair participant produced inconsistent module identities.");
                        }
                        moduleVersions[
                            participant.Subject.Registration] =
                                participant.ModuleVersionId;
                    }
                    executions.Add(new(left, right, result, null));
                }
                catch (AssemblyPairCallUseRequestException exception)
                {
                    executions.Add(new(left, right, null, exception));
                }
            }
        }

        var librariesByRegistration =
            new Dictionary<
                AssemblyAcquisitionRegistration,
                PackagePairLibraryDescriptor>(
                    ReferenceEqualityComparer.Instance);
        AddLibraries(
            canonicalFirstParticipants,
            canonicalFirstPackage,
            moduleVersions,
            librariesByRegistration);
        AddLibraries(
            canonicalSecondParticipants,
            canonicalSecondPackage,
            moduleVersions,
            librariesByRegistration);

        int clusterOrdinal = 0;
        var pairs =
            ImmutableArray.CreateBuilder<PackagePairLibraryPair>(
                executions.Count);
        for (int index = 0; index < executions.Count; index++)
        {
            PairExecution execution = executions[index];
            PackagePairLibraryDescriptor left =
                librariesByRegistration[
                    execution.Left.Participant.Assembly.Registration];
            PackagePairLibraryDescriptor right =
                librariesByRegistration[
                    execution.Right.Participant.Assembly.Registration];
            if (execution.RequestFailure is not null)
            {
                pairs.Add(
                    new(
                        index + 1,
                        left,
                        right,
                        [
                            new(
                                PackagePairLibraryFailureKind.PairRejected,
                                null,
                                execution.RequestFailure.Message),
                        ],
                        [],
                        [],
                        AssemblyPairCallUseDiagnostics.Empty,
                        IsComplete: false));
                continue;
            }

            AssemblyPairCallUseResult result = execution.Result!;
            ImmutableArray<PackagePairDirectCall> calls =
            [
                .. result.Occurrences.Select(occurrence =>
                    new PackagePairDirectCall(
                        librariesByRegistration[
                            occurrence.Source.Registration],
                        occurrence.SourceMethod,
                        librariesByRegistration[
                            occurrence.Target.Registration],
                        occurrence.TargetMethod,
                        occurrence.Call)),
            ];
            AssemblyPairDirectUseClusterProjection clustered =
                AssemblyPairDirectUseClusterProjection.Create(result);
            ImmutableArray<PackagePairDirectUseCluster> clusters =
            [
                .. clustered.Clusters.Select(cluster =>
                {
                    clusterOrdinal++;
                    return new PackagePairDirectUseCluster(
                        new(
                            librariesByRegistration[
                                cluster.Identity.Source.Registration],
                            cluster.Identity.AnchorSourceMethodToken,
                            librariesByRegistration[
                                cluster.Identity.Target.Registration],
                            cluster.Identity.AnchorTargetMethodToken),
                        cluster.Derivation,
                        clusterOrdinal,
                        index + 1,
                        cluster.Ordinal,
                        cluster.SourceMethods,
                        cluster.TargetTypes,
                        cluster.TargetMethods,
                        cluster.OccurrenceIndexes);
                }),
            ];
            pairs.Add(
                new(
                    index + 1,
                    left,
                    right,
                    DetachFailures(
                        result,
                        librariesByRegistration),
                    calls,
                    clusters,
                    result.Diagnostics,
                    result.IsComplete));
        }

        return new PackagePairDirectUseClusterOutcome.Available(
            new(
                canonicalFirstPackage,
                canonicalSecondPackage,
                targetFramework,
                [
                    .. canonicalFirstParticipants
                        .Concat(canonicalSecondParticipants)
                        .Select(participant =>
                            librariesByRegistration[
                                participant.Participant.Assembly.Registration]),
                ],
                pairs.MoveToImmutable()));
    }

    static void RequirePackage(
        ImmutableArray<PackageRootIdentity> roots,
        PackageRootIdentity package,
        string parameterName)
    {
        if (!roots.Any(root => ReferenceEquals(root, package)))
        {
            throw new PackagePairDirectUseClusterRequestException(
                PackagePairDirectUseClusterRequestFailureKind
                    .PackageOutsideProjection,
                "The Package root does not belong to the implementation projection.",
                parameterName);
        }
    }

    static ImmutableArray<PackageAssemblyRoleParticipant> ParticipantsFor(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        PackageRootIdentity package) =>
        [
            .. participants.Where(participant =>
                ReferenceEquals(participant.Package, package)),
        ];

    static string SelectedFramework(
        PackageRootIdentity package,
        ImmutableArray<PackageAssemblyRoleParticipant> participants)
    {
        string[] frameworks =
        [
            .. participants
                .Select(static participant =>
                    participant.Asset.TargetFramework)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        if (frameworks.Length > 1)
        {
            throw new InvalidOperationException(
                "One Package implementation population cannot span selected target frameworks.");
        }
        return frameworks.SingleOrDefault()
            ?? package.RequestedTargetFramework
            ?? throw new InvalidOperationException(
                "An empty Package implementation population requires an explicit target framework.");
    }

    static PackagePairPackageDescriptor Describe(
        PackageRootIdentity package,
        RealizedMemberCoordinate.Package coordinate,
        string requestedFramework,
        string selectedFramework) =>
        new(
            coordinate,
            package.PackageId,
            package.PackageVersion,
            requestedFramework,
            selectedFramework,
            package.RequestedRuntimeIdentifier);

    static string SharedTargetFramework(
        PackageRootIdentity first,
        RealizedMemberCoordinate.Package firstCoordinate,
        PackageRootIdentity second,
        RealizedMemberCoordinate.Package secondCoordinate)
    {
        string firstFramework =
            firstCoordinate.Framework
            ?? first.RequestedTargetFramework
            ?? throw new InvalidOperationException(
                "Package-pair call use requires an explicit target framework.");
        string secondFramework =
            secondCoordinate.Framework
            ?? second.RequestedTargetFramework
            ?? throw new InvalidOperationException(
                "Package-pair call use requires an explicit target framework.");
        if (!string.Equals(
            firstFramework,
            secondFramework,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new PackagePairDirectUseClusterRequestException(
                PackagePairDirectUseClusterRequestFailureKind
                    .IncompatibleTargetFrameworks,
                "Package-pair call use requires one shared requested target framework.",
                nameof(second));
        }
        return firstFramework;
    }

    static void RequireCoordinate(
        PackageRootIdentity package,
        RealizedMemberCoordinate.Package coordinate,
        string parameterName)
    {
        if (!string.Equals(
                package.PackageId,
                coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                package.PackageVersion,
                coordinate.Version,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                package.RequestedRuntimeIdentifier,
                coordinate.RuntimeIdentifier,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The realized Package coordinate does not describe the exact Package root.",
                parameterName);
        }
    }

    static int ComparePackages(
        PackagePairPackageDescriptor first,
        PackagePairPackageDescriptor second)
    {
        int result = StringComparer.OrdinalIgnoreCase.Compare(
            first.PackageId,
            second.PackageId);
        if (result != 0)
            return result;
        result = StringComparer.Ordinal.Compare(
            first.PackageVersion,
            second.PackageVersion);
        if (result != 0)
            return result;
        result = StringComparer.Ordinal.Compare(
            first.Coordinate.Producer,
            second.Coordinate.Producer);
        if (result != 0)
            return result;
        result = StringComparer.OrdinalIgnoreCase.Compare(
            first.SelectedTargetFramework,
            second.SelectedTargetFramework);
        if (result != 0)
            return result;
        return StringComparer.Ordinal.Compare(
            first.RuntimeIdentifier,
            second.RuntimeIdentifier);
    }

    static ImmutableArray<PackageAssemblyRoleParticipant> Order(
        ImmutableArray<PackageAssemblyRoleParticipant> participants) =>
        [
            .. participants
                .OrderBy(
                    static participant => participant.Asset.Path,
                    StringComparer.Ordinal)
                .ThenBy(
                    static participant =>
                        participant.Participant.Assembly.Identity.Name,
                    StringComparer.Ordinal)
                .ThenBy(
                    static participant =>
                        participant.Participant.Assembly.Identity.Version),
        ];

    static void AddLibraries(
        ImmutableArray<PackageAssemblyRoleParticipant> participants,
        PackagePairPackageDescriptor package,
        Dictionary<AssemblyAcquisitionRegistration, Guid> moduleVersions,
        Dictionary<
            AssemblyAcquisitionRegistration,
            PackagePairLibraryDescriptor> destination)
    {
        foreach (PackageAssemblyRoleParticipant participant in participants)
        {
            ResolvedAssemblyReference assembly =
                participant.Participant.Assembly;
            destination.Add(
                assembly.Registration,
                new(
                    package,
                    participant.Asset,
                    assembly.Identity,
                    moduleVersions.GetValueOrDefault(
                        assembly.Registration) is var moduleVersion
                        && moduleVersion != Guid.Empty
                            ? moduleVersion
                            : null));
        }
    }

    static ImmutableArray<PackagePairLibraryFailure> DetachFailures(
        AssemblyPairCallUseResult result,
        Dictionary<
            AssemblyAcquisitionRegistration,
            PackagePairLibraryDescriptor> libraries)
    {
        var detached =
            ImmutableArray.CreateBuilder<PackagePairLibraryFailure>();
        detached.AddRange(
            result.Failures.Select(failure => failure switch
            {
                AssemblyPairCallUseFailure.Rejected rejected =>
                    new PackagePairLibraryFailure(
                        PackagePairLibraryFailureKind.CandidateRejected,
                        libraries[rejected.Subject.Registration],
                        rejected.Failure.Detail),
                AssemblyPairCallUseFailure.InvalidImage invalid =>
                    new PackagePairLibraryFailure(
                        PackagePairLibraryFailureKind.InvalidImage,
                        libraries[invalid.Subject.Registration],
                        invalid.Error.Message),
                _ => throw new InvalidOperationException(
                    "Unknown pairwise call-use failure."),
            }));
        foreach (AssemblyPairCallUseParticipant participant
            in result.Participants)
        {
            foreach (Analysis.AnalysisDiagnostic diagnostic
                in participant.Diagnostics)
            {
                detached.Add(
                    new(
                        PackagePairLibraryFailureKind.AnalysisIncomplete,
                        libraries[participant.Subject.Registration],
                        $"Method 0x{diagnostic.MethodToken:X8}: "
                            + diagnostic.Message));
            }
        }
        return detached.ToImmutable();
    }

    sealed record PairExecution(
        PackageAssemblyRoleParticipant Left,
        PackageAssemblyRoleParticipant Right,
        AssemblyPairCallUseResult? Result,
        AssemblyPairCallUseRequestException? RequestFailure);
}
