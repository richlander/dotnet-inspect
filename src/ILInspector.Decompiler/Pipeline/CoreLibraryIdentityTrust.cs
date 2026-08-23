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
/// therefore not entitled to corelib identity merely because it says it is. A
/// loose <c>System.Runtime.dll</c> sitting beside an inspected assembly would
/// otherwise mint its own <c>System.Collections.IEnumerable</c> and have it
/// compare equal to the real one, silently conflating two unrelated
/// definitions. That needs no ill intent to go wrong: a directory of loose
/// binaries is rarely a coherent closure, and a stale, mismatched, or
/// reference-only copy confuses types exactly as effectively as a planted one.
/// </para>
/// <para>
/// Trust comes from acquisition, not from metadata, and it follows how the
/// caller named the file. A raw path is an explicit designation: the caller
/// chose that exact file, so it is trusted, which is what lets the CLI open
/// <c>System.Private.CoreLib.dll</c> out of a dotnet/runtime build layout. A
/// <see cref="ResolvedAssemblyReference"/> was reached by discovery, so it is
/// trusted only when its acquisition says so — a platform asset
/// (<see cref="AssemblyResolutionProvenance.PlatformAsset"/>) or an explicitly
/// enumerated designation
/// (<see cref="AssemblyResolutionProvenance.DesignatedAsset"/>).
/// </para>
/// <para>
/// <see cref="AssemblyResolutionProvenance.PlatformAsset"/> means the assembly
/// came from a coherent closure — a dotnet hive, a runtime pack, or a reference
/// pack — and nothing else is promoted to it. Loose binaries stay inspectable
/// and stay loose. Promoting a directory that merely looks like a platform is
/// the type-confusion hazard this type exists to prevent, because such a
/// directory carries no guarantee that its parts agree with one another.
/// Supporting build layouts as a first-class scenario is deferred deliberately:
/// it needs a way to establish that a layout <em>is</em> a coherent whole,
/// which is a feature, not a relaxation of this rule.
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
/// reach that field in IL is pinned by signature — so a grant cannot hide behind
/// a name, a nested helper, a static constructor, or an overload of a member
/// that does not grant. That pin covers reader creation and direct grants, not
/// provenance, not receipt, and not the consumer side: whether a grant is
/// deserved, and whether the check is honoured at all, remain
/// <c>PlantedCoreLibraryIdentityTests</c>'s property, and a reader arriving by
/// delegate or reflection is simply unclassified, which is the fail-closed
/// answer.
/// </para>
/// </summary>
static class CoreLibraryIdentityTrust
{
    static readonly ConditionalWeakTable<MetadataReader, object> s_trusted = new();
    static readonly object s_marker = new();

    /// <summary>
    /// Records that <paramref name="reader"/> was acquired from a source that
    /// entitles it to name its own definitions as core-library types.
    /// <para>
    /// This is <see langword="private"/> so that <see cref="GrantIfEntitled"/>
    /// is the only way to reach it, which makes the rule the single source of
    /// entitlement rather than one of two. It was <c>internal</c> through round
    /// 8, and three of the five grant sites called it directly: two of them
    /// built <see cref="AssemblyResolutionProvenance.Local"/> provenance, which
    /// <see cref="MayMint"/> denies, and granted anyway. The behaviour was
    /// right — each of those sites opens a file the caller named, which is a
    /// designation — but it was right by bypass, so every gate on
    /// <see cref="MayMint"/> proved nothing about them. Rounds 5 through 8 kept
    /// finding that escape one frame further out because the escape was not a
    /// missing gate; it was a second door. Privacy removes the door instead of
    /// gating it, so a future caller cannot reintroduce one without saying
    /// which acquisition entitles it.
    /// </para>
    /// </summary>
    private static void GrantCoreLibraryIdentity(MetadataReader reader)
        => s_trusted.AddOrUpdate(reader, s_marker);

    /// <summary>
    /// Grants when <paramref name="provenance"/> entitles a discovered
    /// assembly to core-library identity.
    /// <para>
    /// This body is gated as tightly as <see cref="MayMint"/> is, by
    /// <c>TheEntitlementDecisionPath_ReadsNoValueOutOfTheAcquisition</c>, which
    /// decodes it and permits it to call only <see cref="MayMint"/> and
    /// <see cref="GrantCoreLibraryIdentity"/>. Round 7 of review moved a
    /// content-keyed rule here, one frame out from the decision, and every gate
    /// stayed green while a package named <c>System.Runtime</c> minted
    /// identity: constraining the method that answers the question while
    /// leaving the method that acts on the answer unconstrained gates nothing.
    /// </para>
    /// </summary>
    internal static void GrantIfEntitled(
        MetadataReader reader,
        AssemblyResolutionProvenance provenance)
    {
        if (MayMint(provenance))
            GrantCoreLibraryIdentity(reader);
    }

    /// <summary>
    /// Whether an acquisition entitles the assembly to core-library identity.
    /// A platform acquisition and an explicit caller designation do; nothing
    /// else does.
    /// <para>
    /// There is deliberately no host opt-in. The removed
    /// <c>IncludeDiscovered</c> policy existed to promote discovered siblings
    /// for a host pointed at a build layout, which is precisely the promotion
    /// the strict model forbids: a loose directory is not a coherent closure,
    /// so entitling what it happens to contain conflates types without anyone
    /// intending it. A host that wants build layouts needs a scenario that
    /// establishes coherence, not a switch that assumes it.
    /// </para>
    /// <para>
    /// Both halves of that are gated, in
    /// <c>PlantedCoreLibraryIdentityTests</c>. Which kinds are entitled is
    /// held by
    /// <c>EveryAcquisitionIsClassified_AndExactlyTwoAreEntitled</c>, which
    /// enumerates every acquisition the product can express and requires this
    /// set exactly. That the answer depends on the kind alone is held by
    /// <c>TheEntitlementDecisionPath_ReadsNoValueOutOfTheAcquisition</c>,
    /// which decodes the emitted IL of this method <em>and</em> of
    /// <see cref="GrantIfEntitled"/>, and requires that neither read a field,
    /// property, or literal — a rule keyed on what an acquisition contains must
    /// load something, so a body that loads nothing cannot be one. Both gates
    /// observe the grant path end to end rather than calling this method, so a
    /// rule added anywhere on the way to the grant is caught wherever it is
    /// spelled. That an
    /// <em>absent</em> acquisition entitles nothing is a third property that
    /// neither of those can see, and
    /// <c>AnAbsentAcquisition_IsNotEntitled</c> holds it.
    /// </para>
    /// </summary>
    internal static bool MayMint(
        AssemblyResolutionProvenance provenance) =>
        provenance is AssemblyResolutionProvenance.PlatformAsset
            or AssemblyResolutionProvenance.DesignatedAsset;

    /// <summary>
    /// Whether <paramref name="reader"/> may canonicalize its own assembly name
    /// to <see cref="TypeRef.CoreLibrary"/>. An unclassified reader may not.
    /// </summary>
    internal static bool MayMintCoreLibraryIdentity(MetadataReader reader)
        => s_trusted.TryGetValue(reader, out _);
}
