using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Inspector.Artifacts;

/// <summary>Owner-attested retained bytes borrowed during admission.</summary>
public readonly ref struct ArtifactAdmissionContentView
{
    internal ArtifactAdmissionContentView(
        ArtifactIdentity artifact,
        ImmutableArray<byte> content)
    {
        Artifact = artifact;
        _content = content;
    }

    private readonly ImmutableArray<byte> _content;

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
    public ReadOnlySpan<byte> Content => _content.AsSpan();

    public Stream OpenRead() =>
        new MemoryStream(
            ImmutableCollectionsMarshal.AsArray(_content)!,
            index: 0,
            count: _content.Length,
            writable: false,
            publiclyVisible: false);
}

/// <summary>Owner-attested retained bytes borrowed during a query.</summary>
public readonly ref struct ArtifactQueryContentView
{
    internal ArtifactQueryContentView(
        ArtifactIdentity artifact,
        ImmutableArray<byte> content)
    {
        Artifact = artifact;
        _content = content;
    }

    private readonly ImmutableArray<byte> _content;

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
    public ReadOnlySpan<byte> Content => _content.AsSpan();

    public Stream OpenRead() =>
        new MemoryStream(
            ImmutableCollectionsMarshal.AsArray(_content)!,
            index: 0,
            count: _content.Length,
            writable: false,
            publiclyVisible: false);
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
