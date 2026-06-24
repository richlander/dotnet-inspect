using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the changed-method fidelity skeleton against two whole-module emit
/// hazards (#1282). Both a non-generic explicit interface implementation and a
/// const enum field previously emitted invalid C# (CS0106 / CS0266) into the
/// reconstructed module, poisoning the single compilation so every changed
/// method in that assembly recompiled-failed. The fidelity check emits the whole
/// module, so a method on the offending type only compiles back when both hazards
/// are handled.
/// </summary>
public class SkeletonEmitTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.SkeletonEmitFixture";

    [Fact]
    public void SkeletonCompilesPastExplicitImplAndConstEnum()
    {
        var sum = FidelityCheck.Evaluate(typeof(SkeletonEmitFixture).Assembly.Location)
            .Single(r => r.Type == FixtureType && r.Method == "Sum");

        // The point is that the whole-module skeleton compiles: an unhandled
        // explicit impl (CS0106) or const enum (CS0266) would surface here as a
        // RecompileFail/ContextFail, not as the clean opcode comparison below.
        Assert.False(sum.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile for {FixtureType}.Sum: {sum.Status} / {sum.Detail}");
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, sum.Status);
    }
}
