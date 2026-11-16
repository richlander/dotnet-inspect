using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Formats;
using DotnetInspector.Platforms.Local;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Local;

internal sealed class InstalledImplementationArtifactIdentity :
    PlatformImplementationArtifactIdentity
{
    readonly InstalledDotnetHiveIdentity _hive;
    readonly PlatformFrameworkName _framework;
    readonly PlatformVersion _version;
    readonly PlatformManifestAssetCoordinate _member;
    readonly InstalledPlatformContentDigest _digest;

    internal InstalledImplementationArtifactIdentity(
        InstalledDotnetHiveIdentity hive,
        InstalledImplementationLibrary library)
        : base(library.Identity)
    {
        _hive = hive;
        _framework = library.FrameworkName;
        _version = library.FrameworkVersion;
        _member = library.ManifestCoordinate;
        _digest = library.ContentDigest;
    }

    public override bool Equals(PlatformImplementationArtifactIdentity? other) =>
        other is InstalledImplementationArtifactIdentity identity
        && ReferenceEquals(_hive, identity._hive)
        && _framework.Equals(identity._framework)
        && _version.Equals(identity._version)
        && _member.Equals(identity._member)
        && _digest.Equals(identity._digest)
        && AssemblyReferenceIdentity.EquivalentComparer.Equals(
            AssemblyIdentity,
            identity.AssemblyIdentity);

    public override int GetHashCode() =>
        HashCode.Combine(
            _hive,
            _framework,
            _version,
            _member,
            _digest,
            AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
                AssemblyIdentity));
}
