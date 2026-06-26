using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The whole-type binding gate (issue #1137): the breadth guard for the source
/// the product <see cref="ILInspector.Decompiler.TypeSourceComposer"/> emits.
/// It composes every public type in the running runtime's CoreLib, stubs bodies,
/// and binds each listing against the platform reference set, failing on any
/// <c>CS0104</c> ambiguous-reference collision outside
/// <see cref="TypeBindCheck.KnownArtifacts"/>.
///
/// <para>
/// A new collision is always a real, compile-breaking defect — the composed
/// source would not bind — so it is gated absolutely. The single known artifact
/// (<c>System.AppDomain.ExecuteAssembly</c>'s <c>AssemblyHashAlgorithm</c>
/// parameter colliding with <c>System.Reflection.AssemblyHashAlgorithm</c>) is
/// allowlisted: it cannot be fixed on the product path without enumerating an
/// external namespace's contents, which the SRM-only, Roslyn-free product path
/// must not do. The gate fails if a composer change introduces a new collision,
/// and (because it is allowlisted, not pinned) stays green if a runtime shift
/// removes the known one.
/// </para>
/// </summary>
[Trait("Speed", "Slow")]
public class TypeBindGateTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;

    [Fact]
    public void CoreLib_HasNoNewTypeSourceBindingCollisions()
    {
        var (typesChecked, collisions) = TypeBindCheck.EvaluateDetailed(CoreLibPath);

        // Non-vacuous: the sweep must actually compose and bind a broad population,
        // or a zero-collision result would prove nothing (a refactor that silently
        // stops composing types must not pass this gate).
        Assert.True(typesChecked > 500,
            $"Expected a large CoreLib corpus; composed and bound only {typesChecked} types. "
                + "A composer or extractor change may have silently stopped producing listings.");

        Assert.True(collisions.Count == 0,
            $"The composed source must bind without ambiguous-reference (CS0104) collisions, but "
                + $"{collisions.Count} new one(s) surfaced — the listing would not compile. If a case is "
                + "genuinely unfixable on the product path without external namespace enumeration, add it to "
                + "TypeBindCheck.KnownArtifacts with justification:\n  "
                + string.Join("\n  ", collisions.Select(c => $"{c.FullType} — {c.Detail}")));
    }
}
