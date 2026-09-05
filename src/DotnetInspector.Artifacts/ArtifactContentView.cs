namespace DotnetInspector.Artifacts;

/// <summary>Owner-attested retained bytes borrowed during admission.</summary>
public readonly ref struct ArtifactAdmissionContentView
{
    internal ArtifactAdmissionContentView(
        ArtifactIdentity artifact,
        ReadOnlySpan<byte> content)
    {
        Artifact = artifact;
        Content = content;
    }

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
    public ReadOnlySpan<byte> Content { get; }
}

/// <summary>Owner-attested retained bytes borrowed during a query.</summary>
public readonly ref struct ArtifactQueryContentView
{
    internal ArtifactQueryContentView(
        ArtifactIdentity artifact,
        ReadOnlySpan<byte> content)
    {
        Artifact = artifact;
        Content = content;
    }

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
    public ReadOnlySpan<byte> Content { get; }
}

public delegate TResult ArtifactAdmissionContentCallback<TResult>(
    scoped ArtifactAdmissionContentView view,
    CancellationToken cancellationToken);

public delegate TResult ArtifactQueryContentCallback<TResult>(
    scoped ArtifactQueryContentView view,
    CancellationToken cancellationToken);

/// <summary>
/// Separates owner authorization rejection from a consumer result or exception.
/// </summary>
public abstract class ArtifactContentAccessOutcome<TResult>
{
    private protected ArtifactContentAccessOutcome()
    {
    }

    public sealed class Accessed : ArtifactContentAccessOutcome<TResult>
    {
        internal Accessed(TResult value) => Value = value;

        public TResult Value { get; }
    }

    public sealed class Unauthorized : ArtifactContentAccessOutcome<TResult>
    {
        public Unauthorized()
        {
        }
    }
}
