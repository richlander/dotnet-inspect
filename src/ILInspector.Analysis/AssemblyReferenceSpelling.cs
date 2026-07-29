using System.Collections.Immutable;
using System.Reflection;

namespace ILInspector.Analysis;

/// <summary>
/// One <c>AssemblyRef</c> row as the referencing image spells it: the simple name, the
/// <c>PublicKeyOrToken</c> blob exactly as stored, and the flags that say how to read it.
///
/// <para>The blob is kept unreduced because <see cref="AssemblyFlags.PublicKey"/> decides whether
/// it is a full public key or an already-reduced token (ECMA-335 II.22.5), and
/// <see cref="AssemblyFlags.Retargetable"/> decides whether the identity binds at all. A type that
/// eagerly reduced to a token would discard both distinctions, which is why this is a verbatim
/// snapshot rather than <see cref="ILInspector.Metadata.AssemblyReferenceIdentity"/>.</para>
///
/// <para>Version is not captured. It is part of ECMA identity but not usable for checking a
/// reference against a definition: binding rolls forward and reference assemblies routinely record
/// <c>0.0.0.0</c>. See <c>ForwardedTypeAliases.EvidenceIdentity</c> for the measurement.</para>
///
/// <para>This exists so identity questions about an already-indexed image can be answered from the
/// bytes that were indexed. Re-reading the file to ask them again would lose genuine callers when
/// the second read fails, and would answer from whatever is on disk at the later moment rather
/// than from the image the answer is about (found in review of <c>7181e795</c>).</para>
/// </summary>
public readonly record struct AssemblyReferenceSpelling(
    string Name,
    ImmutableArray<byte> PublicKeyOrToken,
    AssemblyFlags Flags,
    string Culture);
