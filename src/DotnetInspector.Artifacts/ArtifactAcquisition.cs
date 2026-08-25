namespace DotnetInspector.Artifacts;

/// <summary>A typed, source-owned artifact acquisition diagnostic.</summary>
public interface IArtifactAcquisitionDiagnostic
{
    string Code { get; }
    string Summary { get; }
}

/// <summary>
/// Source-owned resource lifetime returned by one successful acquisition.
/// </summary>
/// <remarks>
/// The future artifact-set owner must retain this lease until dependent groups
/// quiesce. That lifetime is unverified until
/// <c>ArtifactSetSession_ReleasesLeasesOnlyAfterDependentGroupsQuiesce</c>.
/// </remarks>
public interface IArtifactAcquisitionLease : IAsyncDisposable
{
}

public static class ArtifactAcquisitionLeases
{
    public static IArtifactAcquisitionLease None { get; } =
        new EmptyArtifactAcquisitionLease();

    private sealed class EmptyArtifactAcquisitionLease :
        IArtifactAcquisitionLease
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>The typed result of one source adapter acquisition.</summary>
/// <remarks>
/// Cancellation is deliberately not an outcome arm; adapters propagate
/// cancellation as cancellation.
/// </remarks>
public abstract record ArtifactAcquisitionOutcome
{
    private protected ArtifactAcquisitionOutcome()
    {
    }

    public sealed record Acquired : ArtifactAcquisitionOutcome
    {
        public Acquired(
            IEnumerable<ArtifactContribution> artifacts,
            IArtifactAcquisitionLease lease)
        {
            ArgumentNullException.ThrowIfNull(artifacts);
            ArgumentNullException.ThrowIfNull(lease);
            ArtifactContribution[] snapshot = [.. artifacts];
            if (snapshot.Any(artifact => artifact is null))
            {
                throw new ArgumentException(
                    "An acquired artifact collection cannot contain null.",
                    nameof(artifacts));
            }

            Artifacts = snapshot;
            Lease = lease;
        }

        public IReadOnlyList<ArtifactContribution> Artifacts { get; }
        public IArtifactAcquisitionLease Lease { get; }
    }

    /// <summary>The source has no content for the requested coordinate.</summary>
    public sealed record Unavailable : ArtifactAcquisitionOutcome
    {
        public Unavailable(IArtifactAcquisitionDiagnostic diagnostic)
        {
            Diagnostic = ValidateDiagnostic(diagnostic);
        }

        public IArtifactAcquisitionDiagnostic Diagnostic { get; }
    }

    /// <summary>Policy rejected the requested acquisition.</summary>
    public sealed record Rejected : ArtifactAcquisitionOutcome
    {
        public Rejected(IArtifactAcquisitionDiagnostic diagnostic)
        {
            Diagnostic = ValidateDiagnostic(diagnostic);
        }

        public IArtifactAcquisitionDiagnostic Diagnostic { get; }
    }

    /// <summary>The source attempted acquisition and failed.</summary>
    public sealed record Failed : ArtifactAcquisitionOutcome
    {
        public Failed(IArtifactAcquisitionDiagnostic diagnostic)
        {
            Diagnostic = ValidateDiagnostic(diagnostic);
        }

        public IArtifactAcquisitionDiagnostic Diagnostic { get; }
    }

    private static IArtifactAcquisitionDiagnostic ValidateDiagnostic(
        IArtifactAcquisitionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic.Summary);
        return diagnostic;
    }
}
