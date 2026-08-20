namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// How much a host trusts assemblies that reference resolution DISCOVERED,
/// rather than ones the caller designated or acquired from a platform source.
/// <para>
/// The two workflows this separates look identical in metadata. A developer
/// inspecting a dotnet/runtime build layout has a real core library sitting
/// beside the assembly under inspection; an attacker shipping a malicious
/// package has a planted <c>System.Runtime.dll</c> sitting beside its own
/// library. Nothing in either file distinguishes them — only the caller's
/// intent does — so this is a host policy rather than an inference.
/// </para>
/// </summary>
enum CoreLibraryTrustPolicy
{
    /// <summary>
    /// Only the designated target, caller-designated assemblies, and platform
    /// acquisitions may claim core-library identity. The safe default, and the
    /// right setting for any host that inspects untrusted uploads.
    /// </summary>
    DesignatedAndPlatform,

    /// <summary>
    /// Additionally let discovered assemblies — siblings of the inspected
    /// artifact — claim core-library identity. Appropriate only where the
    /// surrounding directory is as trusted as the target itself, such as a
    /// local tool pointed at a build layout the user controls. This restores
    /// the pre-<c>#4411</c> behaviour and with it the planted-sibling exposure.
    /// It reaches siblings only: a package payload or an embedded upload stays
    /// denied, so a host that trusts its working directory does not thereby
    /// trust content it merely downloaded or was handed.
    /// </summary>
    IncludeDiscovered,
}
