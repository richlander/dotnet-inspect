namespace DotnetInspector.Artifacts.Workspaces;

/// <summary>An owner-issued digest of one artifact's retained snapshot.</summary>
public sealed class ArtifactContentDigest
{
    internal ArtifactContentDigest(
        ArtifactIdentity artifact,
        string hexValue)
    {
        Artifact = artifact;
        HexValue = hexValue;
    }

    public ArtifactIdentity Artifact { get; }
    public ArtifactGenerationIdentity Generation => Artifact.Generation;
    public string Algorithm => "SHA-256";
    public string HexValue { get; }
}
