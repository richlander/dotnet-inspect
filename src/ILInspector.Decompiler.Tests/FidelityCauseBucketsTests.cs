using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class FidelityCauseBucketsTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void PrimaryBucket_UsesStructuredOpcodeDiscriminator()
    {
        var function = Function(
            new Return(new UnsupportedNode(0x05, "calli", "unsupported call site")));

        Assert.Equal("calli", FidelityCauseBuckets.PrimaryBucket(function, "M"));
    }

    [Fact]
    public void PrimaryBucket_UsesStableCodeWhenCauseHasNoDiscriminator()
    {
        var function = Function(new Continue());

        Assert.Equal(
            DiagnosticIds.UnverifiedContinue,
            FidelityCauseBuckets.PrimaryBucket(function, "M"));
    }

    [Fact]
    public void Inspect_OperationFailure_IsSurfaced()
    {
        var function = Function(new Return(new Constant(0, Int32)));
        function.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.InternalError,
            "importer failed"));

        var census = FidelityCauseBuckets.Inspect(function, "M");

        Assert.False(census.Succeeded);
        Assert.Contains(DiagnosticIds.InternalError, census.Failure);
        Assert.Empty(census.Causes);
    }

    static IrFunction Function(IrNode statement)
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(statement);
        return new IrFunction(
            "M",
            TypeRef.CoreLib("System", "Object"),
            new MethodSignature(
                Int32,
                [],
                HasThis: false,
                GenericParameterCount: 0),
            [],
            container);
    }
}
