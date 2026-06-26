using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the changed-method fidelity path's identity matching for nested types
/// (#1274). The corpus delta names nested types through their declaring type
/// (<c>Outer.Inner</c>); before the fix the targeted path collected entries only
/// for top-level types, so every changed method on a nested type landed in the
/// <c>target-method-not-found</c> bucket and the check silently stopped measuring
/// that part of the changed population.
/// </summary>
public class NestedTargetLookupTests
{
    const string Outer = "ILInspector.Decompiler.Tests.NestedTargetFixture";
    const string Inner = "ILInspector.Decompiler.Tests.NestedTargetFixture.Inner";

    static string FixtureAssembly => typeof(NestedTargetFixture).Assembly.Location;

    [Fact]
    public void TargetedPath_ReachesNestedTypeIdentity()
    {
        var collectible = FidelityCheck.CollectibleFullTypeNames(FixtureAssembly);

        Assert.Contains(Outer, collectible);
        // The fix: the nested type is now part of the identity surface a delta
        // row is matched against, threaded through its declaring type.
        Assert.Contains(Inner, collectible);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void CorpusSweep_StillSkipsNestedTypes()
    {
        // Preserve existing --fidelity-check behavior: the non-targeted corpus
        // sweep roots at top-level types only, so a nested type's methods never
        // enter the broad sweep even though the targeted path can now reach them.
        var swept = FidelityCheck.Evaluate(FixtureAssembly);

        Assert.Contains(swept, r => r.Type == Outer && r.Method == "OuterAdd");
        Assert.DoesNotContain(swept, r => r.Type == Inner);
    }

    [Theory]
    [InlineData("ILInspector.Analysis.LibraryBodyIndex.IndexBuilder", "Build", false)]
    [InlineData("System.Text.RegularExpressions.Generated.<RegexGenerator_g>X.RunnerFactory.Runner", "Scan", true)]
    [InlineData("Some.Normal.Type", "<TryMatch>g__UncaptureUntil|2_0", true)]
    [InlineData("<Module>", ".cctor", true)]
    [InlineData("Some.Normal.Type", "Method", false)]
    public void SynthesizedTargets_AreClassifiedSeparately(string type, string method, bool synthesized)
        => Assert.Equal(synthesized, FidelityCheck.IsSynthesizedMember(type, method));
}
