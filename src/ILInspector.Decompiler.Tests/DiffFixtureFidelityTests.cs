using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
public class DiffFixtureFidelityTests
{
    static readonly string[] DiffFocusedMethods =
    [
        "ConstantValue",
        "MultipleHunks",
        "StringToken",
        "CallToken",
        "BranchTargetOffsetShift",
        "BranchRetarget",
        "AddsUnsafe",
    ];

    [Theory]
    [InlineData(FixtureIds.DiffV1)]
    [InlineData(FixtureIds.DiffV2)]
    public void DiffFocusedFixtures_StayCompileBackCheckable(string fixtureId)
    {
        var fixture = FixtureCatalog.Get(fixtureId);
        var results = FidelityCheck.Evaluate(fixture.AssemblyPath());
        foreach (string method in DiffFocusedMethods)
        {
            var matches = results.Where(result => result.Method == method).ToArray();
            Assert.True(matches.Length > 0, $"Expected fidelity check to evaluate {fixture.Id}.{method}.");
            Assert.All(matches, result =>
                Assert.True(
                    result.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff,
                    $"{fixture.Id}.{method} regressed to {result.Status}: the paired diff fixture must remain decompiler compile-back checkable.\n"
                        + $"  original : {result.OriginalOpcodes}\n"
                        + $"  recompiled: {result.RecompiledOpcodes}\n"
                        + $"  detail   : {result.Detail}"));
        }
    }
}
