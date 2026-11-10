using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Formats;
using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Packages;

internal sealed class PackageImplementationArtifactIdentity :
    PlatformImplementationArtifactIdentity
{
    readonly ConfiguredPackageAuthority _authority;
    readonly PlatformFrameworkName _framework;
    readonly PlatformFamily _family;
    readonly PlatformVersion _version;
    readonly string _packageId;
    readonly string _runtimeIdentifier;
    readonly PlatformManifestAssetCoordinate _member;
    readonly PackagePlatformContentDigest _digest;

    internal PackageImplementationArtifactIdentity(
        PackageImplementationLibrary library)
        : base(library.Identity)
    {
        PackageImplementationFramework framework = library.Framework;
        _authority = framework.Authority;
        _framework = framework.Name;
        _family = framework.Family;
        _version = framework.Version;
        _packageId = framework.PackageId;
        _runtimeIdentifier = framework.RuntimeIdentifier;
        _member = library.ManifestCoordinate;
        _digest = library.ContentDigest;
    }

    public override bool Equals(PlatformImplementationArtifactIdentity? other) =>
        other is PackageImplementationArtifactIdentity identity
        && ReferenceEquals(_authority, identity._authority)
        && _framework.Equals(identity._framework)
        && _family == identity._family
        && _version.Equals(identity._version)
        && StringComparer.OrdinalIgnoreCase.Equals(
            _packageId,
            identity._packageId)
        && StringComparer.Ordinal.Equals(
            _runtimeIdentifier,
            identity._runtimeIdentifier)
        && _member.Equals(identity._member)
        && _digest.Equals(identity._digest)
        && AssemblyReferenceIdentity.EquivalentComparer.Equals(
            AssemblyIdentity,
            identity.AssemblyIdentity);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_authority);
        hash.Add(_framework);
        hash.Add(_family);
        hash.Add(_version);
        hash.Add(_packageId, StringComparer.OrdinalIgnoreCase);
        hash.Add(_runtimeIdentifier, StringComparer.Ordinal);
        hash.Add(_member);
        hash.Add(_digest);
        hash.Add(
            AssemblyIdentity,
            AssemblyReferenceIdentity.EquivalentComparer);
        return hash.ToHashCode();
    }
}
