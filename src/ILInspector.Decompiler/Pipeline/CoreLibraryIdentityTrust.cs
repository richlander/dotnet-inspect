using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Which readers may mint core-library identity for the types they define.
/// <para>
/// <see cref="TypeRefDecoder.CanonicalSelf"/> canonicalizes a reader's own
/// assembly name to <see cref="TypeRef.CoreLibrary"/> when that assembly
/// carries a platform public key. The key is not proof: nothing here verifies
/// a strong-name signature, and the platform public keys are published data, so
/// any assembly can declare one. A file reached through reference resolution is
/// therefore not entitled to corelib identity merely because it says it is
/// — a planted <c>System.Runtime.dll</c> beside an inspected assembly would
/// otherwise mint its own <c>System.Collections.IEnumerable</c> and have it
/// compare equal to the real one.
/// </para>
/// <para>
/// Trust comes from acquisition, not from metadata, and it follows how the
/// caller named the file. A raw path is an explicit designation: the caller
/// chose that exact file, so it is trusted, which is what lets the CLI open
/// <c>System.Private.CoreLib.dll</c> out of a dotnet/runtime build layout. A
/// <see cref="ResolvedAssemblyReference"/> was reached by discovery, so it is
/// trusted only when its acquisition says so — a platform asset
/// (<see cref="AssemblyResolutionProvenance.PlatformAsset"/>), an explicitly
/// enumerated designation
/// (<see cref="AssemblyResolutionProvenance.DesignatedAsset"/>), or any
/// acquisition once a host opts in with
/// <see cref="CoreLibraryTrustPolicy.IncludeDiscovered"/>.
/// </para>
/// <para>
/// This is an <em>allow</em> list, and the polarity is the point. A deny list
/// has to name every site that turns bytes into a reader, so a site nobody
/// remembered fails open and silently restores the vulnerability; review of
/// PR #4428 found exactly that, because <c>MetadataSource.OpenCore</c> creates
/// readers without going through <c>MetadataContext</c>. Failing closed bounds
/// the obligation to the few sites that deliberately <em>grant</em> trust: a
/// new open path that forgets to classify loses corelib identity, which is
/// visible and safe, rather than gaining it, which is neither.
/// </para>
/// <para>
/// Gated by <c>PlantedCoreLibraryIdentityTests</c>, which tamper-verifies both
/// halves — the grant and the check — and covers the resolved, designated,
/// raw-path, and unclassified open paths, and by
/// <c>ReaderConstructionSiteTests</c>, which pins every method in this assembly
/// whose IL obtains a <c>MetadataReader</c> or reaches the trust table, so a new
/// way to obtain a reader cannot be added without saying which half it is on.
/// Identity is exactly membership in <c>s_trusted</c>, and every method able to
/// reach that field is pinned by signature — so a grant cannot hide behind a
/// name, a nested helper, a static constructor, or an overload of a member that
/// does not grant. That
/// pin covers reader creation and granting, not provenance and not receipt:
/// whether a grant is deserved remains <c>PlantedCoreLibraryIdentityTests</c>'s
/// property, and a reader arriving by delegate or reflection is simply
/// unclassified, which is the fail-closed answer.
/// </para>
/// </summary>
static class CoreLibraryIdentityTrust
{
    static readonly ConditionalWeakTable<MetadataReader, object> s_trusted = new();
    static readonly object s_marker = new();

    /// <summary>
    /// Records that <paramref name="reader"/> was acquired from a source that
    /// entitles it to name its own definitions as core-library types.
    /// </summary>
    internal static void GrantCoreLibraryIdentity(MetadataReader reader)
        => s_trusted.AddOrUpdate(reader, s_marker);

    /// <summary>
    /// Grants when <paramref name="provenance"/> entitles a discovered
    /// assembly to core-library identity under <paramref name="policy"/>.
    /// </summary>
    internal static void GrantIfEntitled(
        MetadataReader reader,
        AssemblyResolutionProvenance provenance,
        CoreLibraryTrustPolicy policy)
    {
        if (MayMint(provenance, policy))
            GrantCoreLibraryIdentity(reader);
    }

    /// <summary>
    /// Whether an acquisition entitles the assembly to core-library identity.
    /// A platform acquisition and an explicit caller designation always do; a
    /// discovered sibling does only when the host has opted in.
    /// </summary>
    internal static bool MayMint(
        AssemblyResolutionProvenance provenance,
        CoreLibraryTrustPolicy policy) =>
        provenance is AssemblyResolutionProvenance.PlatformAsset
            or AssemblyResolutionProvenance.DesignatedAsset
        || policy == CoreLibraryTrustPolicy.IncludeDiscovered;

    /// <summary>
    /// Whether <paramref name="reader"/> may canonicalize its own assembly name
    /// to <see cref="TypeRef.CoreLibrary"/>. An unclassified reader may not.
    /// </summary>
    internal static bool MayMintCoreLibraryIdentity(MetadataReader reader)
        => s_trusted.TryGetValue(reader, out _);
}
