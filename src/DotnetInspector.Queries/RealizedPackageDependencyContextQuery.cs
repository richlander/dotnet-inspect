using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Resource-free identity and selection evidence for one realized package
/// participant.
/// </summary>
public sealed class RealizedPackageDependencySubject
{
    internal RealizedPackageDependencySubject(PackageRootBinding binding)
    {
        RootRequest = binding.CreateReacquisitionRequest();
        ContentGeneration = binding.ContentGenerationIdentity;
        Selection = binding.SelectionIdentity;
    }

    public PackageRootReacquisitionRequest RootRequest { get; }

    public PackageContentGenerationIdentity ContentGeneration { get; }

    public PackageRootSelectionIdentity Selection { get; }
}

/// <summary>
/// Dependency evidence projected from one exact realized package participant.
/// </summary>
public sealed class RealizedPackageDependencyContext
{
    internal RealizedPackageDependencyContext(
        RealizedPackageDependencySubject subject,
        PackageDependencyEvidenceRoot evidence)
    {
        Subject = subject;
        Evidence = evidence;
    }

    public RealizedPackageDependencySubject Subject { get; }

    public PackageDependencyEvidenceRoot Evidence { get; }
}

public enum RealizedPackageDependencyUnavailableReason
{
    NoManifest,
}

/// <summary>
/// The closed result of projecting dependency evidence from one realized
/// package participant.
/// </summary>
public abstract class RealizedPackageDependencyContextResult
{
    private protected RealizedPackageDependencyContextResult(
        RealizedPackageDependencySubject subject) =>
        Subject = subject;

    public RealizedPackageDependencySubject Subject { get; }

    public sealed class Available : RealizedPackageDependencyContextResult
    {
        internal Available(RealizedPackageDependencyContext context)
            : base(context.Subject) =>
            Context = context;

        public RealizedPackageDependencyContext Context { get; }
    }

    public sealed class Unavailable : RealizedPackageDependencyContextResult
    {
        internal Unavailable(
            RealizedPackageDependencySubject subject,
            RealizedPackageDependencyUnavailableReason reason)
            : base(subject) =>
            Reason = reason;

        public RealizedPackageDependencyUnavailableReason Reason { get; }
    }

    public sealed class Failed : RealizedPackageDependencyContextResult
    {
        internal Failed(
            RealizedPackageDependencySubject subject,
            PackageDependencyGroupsResult.Failed failure)
            : base(subject) =>
            Failure = failure;

        public PackageDependencyGroupsResult.Failed Failure { get; }
    }
}

/// <summary>
/// Projects dependency evidence from the exact retained content and frozen
/// selection intent of one acquisition-issued package binding.
/// </summary>
public static class RealizedPackageDependencyContextQuery
{
    public static InspectionQuery<RealizedPackageDependencyContextResult>
        Definition { get; } =
        new(
            "Realized package dependency context",
            InspectionCost.NetworkFree);

    public static async ValueTask<RealizedPackageDependencyContextResult>
        ExecuteAsync(
            PackageRootBinding binding,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.ReferencesRetainedContent())
        {
            throw new ArgumentException(
                "The package binding no longer identifies its retained content.",
                nameof(binding));
        }

        var subject = new RealizedPackageDependencySubject(binding);
        PackageDependencyGroupsResult groups =
            await PackageDependencyGroupsQuery.ExecuteAsync(
                binding.Root.Content,
                binding.Coordinate.PackageId,
                binding.Coordinate.Version,
                subject.RootRequest.CompileTargetFramework,
                cancellationToken,
                subject.RootRequest.AllowsCompatibleTargetSelection)
                .ConfigureAwait(false);
        return groups switch
        {
            PackageDependencyGroupsResult.Available available =>
                Available(subject, available),
            PackageDependencyGroupsResult.NoManifest =>
                new RealizedPackageDependencyContextResult.Unavailable(
                    subject,
                    RealizedPackageDependencyUnavailableReason.NoManifest),
            PackageDependencyGroupsResult.Failed failed =>
                new RealizedPackageDependencyContextResult.Failed(
                    subject,
                    failed),
            _ => throw new InvalidOperationException(
                "Unknown package dependency-group result."),
        };
    }

    private static RealizedPackageDependencyContextResult.Available Available(
        RealizedPackageDependencySubject subject,
        PackageDependencyGroupsResult.Available available)
    {
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest(
                    [
                        PackageDependencyEvidenceQuery.CreatePackageInput(
                            available,
                            PackageDependencyEvidenceAcquisitionForm
                                .PackageArchive),
                    ]));
        PackageDependencyEvidenceRoot evidence =
            outcome.Roots.Length == 1 && outcome.FailedRoots.IsEmpty
                ? outcome.Roots[0]
                : throw new InvalidOperationException(
                    "A realized package dependency context requires one admitted package evidence root.");
        PackageSourceCoordinate expected =
            PackageSourceCoordinate.Create(
                subject.RootRequest.Coordinate.PackageId,
                subject.RootRequest.Coordinate.Version);
        if (evidence.Identity
                is not PackageDependencyEvidenceRootIdentity.Package package
            || package.Coordinate != expected)
        {
            throw new InvalidOperationException(
                "The realized package binding and dependency evidence describe different package coordinates.");
        }

        return new RealizedPackageDependencyContextResult.Available(
            new RealizedPackageDependencyContext(subject, evidence));
    }
}
