using DotnetInspector.Fixtures;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
[Trait("Area", "Fidelity")]
[Collection(FidelityGateCollection.Name)]
public class DiffFixtureFidelityTests
{
    const string DiffFixtureType = "DiffFixtureSample.DiffSample";

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
    public async Task DiffFocusedFixtures_StayCompileBackCheckable(string fixtureId)
    {
        var fixture = FixtureCatalog.Get(fixtureId);
        var targets = DiffFocusedMethods
            .Select(method => new ReturnToSender.RequestedTarget(DiffFixtureType, method, Overload: 0))
            .ToArray();
        var results = await ReturnToSender.CompileBackTargets(
            fixture.AssemblyPath(),
            targets,
            applyCompileBackFloor: false);

        Assert.Equal(targets.Length, results.Count);
        foreach (string method in DiffFocusedMethods)
        {
            var result = Assert.Single(
                results,
                candidate => candidate.Plan.TargetMethod.Type == DiffFixtureType
                    && candidate.Plan.TargetMethod.Method == method
                    && candidate.Plan.TargetMethod.Overload == 0);
            Assert.False(result.UsedCompileBackFloor);
            Assert.True(
                result.Status is FidelityCheck.CompileBackStatus.Exact
                    or FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff,
                $"{fixture.Id}.{method} regressed to {result.Status}: the paired diff fixture must remain decompiler compile-back checkable.\n"
                    + $"  original : {result.OriginalOpcodes}\n"
                    + $"  recompiled: {result.RecompiledOpcodes}\n"
                    + $"  detail   : {result.Detail}");
        }
    }
}
