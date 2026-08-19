using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

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
/// Trust comes from acquisition, not from metadata. The assembly the caller
/// explicitly opened is trusted by designation, and so is anything resolution
/// acquired from a platform source
/// (<see cref="ILInspector.Metadata.AssemblyResolutionProvenance.PlatformAsset"/>).
/// Everything else resolution opens is marked here and loses only the ability
/// to claim the corelib name for its own definitions.
/// </para>
/// <para>
/// Marking is deliberately a deny list applied at the single point where
/// resolution turns a selected assembly into a reader
/// (<c>MetadataContext.Open(ResolvedAssemblyReference)</c>), so an unregistered
/// reader keeps the historical behaviour and a bypass is one search away rather
/// than an invisible default. <c>PlantedCoreLibraryIdentityTests</c> gates it.
/// </para>
/// </summary>
static class CoreLibraryIdentityTrust
{
    static readonly ConditionalWeakTable<MetadataReader, object> s_untrusted = new();
    static readonly object s_marker = new();

    /// <summary>
    /// Records that <paramref name="reader"/> was acquired from a source that
    /// does not entitle it to name its own definitions as core-library types.
    /// </summary>
    internal static void DenyCoreLibraryIdentity(MetadataReader reader)
        => s_untrusted.AddOrUpdate(reader, s_marker);

    /// <summary>
    /// Whether <paramref name="reader"/> may canonicalize its own assembly name
    /// to <see cref="TypeRef.CoreLibrary"/>.
    /// </summary>
    internal static bool MayMintCoreLibraryIdentity(MetadataReader reader)
        => !s_untrusted.TryGetValue(reader, out _);
}
