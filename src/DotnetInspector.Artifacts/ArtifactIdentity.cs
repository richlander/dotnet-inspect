namespace DotnetInspector.Artifacts;

/// <summary>Source-specific provenance attached to one registered artifact.</summary>
public interface IArtifactProvenance
{
}

/// <summary>Opaque identity for one artifact-set generation.</summary>
public sealed class ArtifactGenerationIdentity
{
    internal ArtifactGenerationIdentity()
    {
    }
}

/// <summary>
/// Opaque identity for one artifact inside one artifact-set generation.
/// </summary>
public sealed class ArtifactIdentity
{
    internal ArtifactIdentity(
        ArtifactGenerationAuthority authority,
        long ordinal)
    {
        Authority = authority;
        Generation = authority.Generation;
        Ordinal = ordinal;
    }

    internal ArtifactGenerationAuthority Authority { get; }

    public ArtifactGenerationIdentity Generation { get; }

    /// <summary>
    /// Deterministic order inside this generation. It is not a durable or
    /// presentation identity.
    /// </summary>
    public long Ordinal { get; }
}

/// <summary>
/// Owner-issued correspondence between one acquisition and one artifact.
/// </summary>
public sealed class ArtifactAcquisitionRegistration
{
    internal ArtifactAcquisitionRegistration(
        ArtifactIdentity artifact,
        IArtifactProvenance provenance)
    {
        Artifact = artifact;
        Provenance = provenance;
    }

    internal ArtifactGenerationAuthority Authority => Artifact.Authority;

    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public ArtifactIdentity Artifact { get; }
    public IArtifactProvenance Provenance { get; }
}

/// <summary>
/// Catalog-safe artifact metadata. Content access and source interpretation
/// remain owner-mediated.
/// </summary>
public sealed class ArtifactDescriptor
{
    internal ArtifactDescriptor(
        ArtifactIdentity identity,
        string? mediaType,
        string? kind)
    {
        Identity = identity;
        MediaType = NormalizeOptional(mediaType, nameof(mediaType));
        Kind = NormalizeOptional(kind, nameof(kind));
    }

    public ArtifactIdentity Identity { get; }
    public string? MediaType { get; }
    public string? Kind { get; }

    private static string? NormalizeOptional(
        string? value,
        string parameterName)
    {
        if (value is null)
            return null;

        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }
}
