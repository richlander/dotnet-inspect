using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins the changed-method fidelity skeleton against whole-module emit hazards.
/// A non-generic explicit interface implementation and a const enum field
/// previously emitted invalid C# (CS0106 / CS0266) into the reconstructed module
/// (#1282); and the bare `using System;` skeleton recompile-failed any body whose
/// short type names needed a wider using set (CS0246, the changed-method
/// missing-symbol bucket). The fidelity check emits the whole module, so a method
/// on the offending type only compiles back when every hazard is handled.
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

    [Theory]
    // Bodies the product printer spells with short names that need a using the
    // bare `using System;` skeleton lacked: System.Collections.Generic
    // (Dictionary) and System.Linq (Enumerable). Before the skeleton's widened
    // using set these recompile-failed with CS0246; now they bind and compare.
    [InlineData("CompoundAssignDictionaryIndexer")]
    [InlineData("CachedStaticMethodGroup")]
    public void SkeletonImportsUsingsForShortNamedFrameworkTypes(string method)
    {
        var result = FidelityCheck.Evaluate(typeof(CfgSampleClass).Assembly.Location)
            .Single(r => r.Type == "ILInspector.Decompiler.Tests.CfgSampleClass" && r.Method == method);

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile CfgSampleClass.{method} (a missing using would surface here): "
            + $"{result.Status} / {result.Detail}");
    }

    [Fact]
    public void SkeletonSkipsCompilerEmbeddedAttributeDefinitions()
    {
        var result = FidelityCheck.Evaluate(typeof(EmbeddedAttributeSkeletonFixture).Assembly.Location)
            .Single(r => r.Type == "ILInspector.Decompiler.Tests.EmbeddedAttributeSkeletonFixture" && r.Method == "Value");

        Assert.False(result.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail,
            $"Skeleton failed to compile with Microsoft.CodeAnalysis.EmbeddedAttribute present: {result.Status} / {result.Detail}");
    }
}
