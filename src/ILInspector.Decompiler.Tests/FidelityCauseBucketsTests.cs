using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;
using ILInspector.Findings;

namespace ILInspector.Decompiler.Tests;

public class FidelityCauseBucketsTests
{
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");

    [Fact]
    public void PrimaryBucket_UsesStructuredOpcodeDiscriminator()
    {
        var function = Function(
            new Return(new UnsupportedNode(0x05, "calli", "unsupported call site")));

        var census = FidelityCauseBuckets.Inspect(function, "M");

        Assert.Equal("calli", FidelityCauseBuckets.PrimaryBucket(census));
    }

    [Fact]
    public void PrimaryBucket_UsesStableCodeWhenCauseHasNoDiscriminator()
    {
        var function = Function(new Continue());

        var census = FidelityCauseBuckets.Inspect(function, "M");

        Assert.Equal(DiagnosticIds.UnverifiedContinue, FidelityCauseBuckets.PrimaryBucket(census));
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
        Assert.Equal(FidelityCauseBuckets.CensusState.Failed, census.State);
        Assert.Equal("fidelity-inspection-failed", census.ErrorCode);
        Assert.Contains(DiagnosticIds.InternalError, census.Detail);
        Assert.Empty(census.Causes);
    }

    [Fact]
    public void FromInspection_Absent_RemainsDistinctFromFailure()
    {
        var census = FidelityCauseBuckets.FromInspection(
            new FindingInspection<DecompilerFidelityCause>.Absent("no method body"));

        Assert.False(census.Succeeded);
        Assert.Equal(FidelityCauseBuckets.CensusState.Absent, census.State);
        Assert.Equal("fidelity-inspection-absent", census.ErrorCode);
        Assert.Equal("no method body", census.Detail);
        Assert.Empty(census.Causes);
    }

    [Fact]
    public void PrimaryBucket_RejectsNonCompleteInspection()
    {
        var census = FidelityCauseBuckets.FromInspection(
            new FindingInspection<DecompilerFidelityCause>.Absent());

        Assert.Throws<InvalidOperationException>(() => FidelityCauseBuckets.PrimaryBucket(census));
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
