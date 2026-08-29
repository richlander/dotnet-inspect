namespace ILInspector.Metadata;

/// <summary>
/// Where a caller allows an assembly identity to resolve from.
/// </summary>
/// <remarks>
/// <see cref="Platform"/> asserts the reference is a runtime/framework
/// assembly, so policy may resolve it only from platform/framework sources,
/// never a confusable local copy. <see cref="Any"/> places no source
/// constraint.
/// </remarks>
public enum AssemblyResolutionScope
{
    Any,
    Platform,
}

internal static class AssemblyResolutionScopes
{
    internal static AssemblyResolutionScope Tighten(
        AssemblyResolutionScope current,
        AssemblyReferenceIdentity target) =>
        Tighten(
            current,
            PlatformKeys.IsPlatform(target.PublicKeyToken)
                ? AssemblyResolutionScope.Platform
                : AssemblyResolutionScope.Any);

    /// <summary>
    /// Combines two scopes so the result is never looser than either input.
    /// <see cref="AssemblyResolutionScope.Platform"/> is the tighter arm, so a
    /// caller can only add the platform constraint, never remove one already
    /// authorized by the source candidate.
    /// </summary>
    internal static AssemblyResolutionScope Tighten(
        AssemblyResolutionScope current,
        AssemblyResolutionScope requested) =>
        current == AssemblyResolutionScope.Platform
            || requested == AssemblyResolutionScope.Platform
                ? AssemblyResolutionScope.Platform
                : AssemblyResolutionScope.Any;

    /// <summary>
    /// The tightest scope a source candidate is already authorized under,
    /// derived from its acquisition provenance and the verified
    /// <c>AssemblyDef</c> identity of its retained image rather than from a
    /// caller-supplied scope.
    /// </summary>
    /// <remarks>
    /// Gated by
    /// <c>SignatureSpellability_RetainsAuthorizedPlatformScope</c>.
    /// </remarks>
    internal static AssemblyResolutionScope Authorized(
        ResolvedAssemblyReference source,
        AssemblyInventorySnapshot inventory) =>
        source.Provenance is AssemblyResolutionProvenance.PlatformAsset
            || PlatformKeys.IsPlatform(inventory.Identity.PublicKeyToken)
                ? AssemblyResolutionScope.Platform
                : AssemblyResolutionScope.Any;
}
