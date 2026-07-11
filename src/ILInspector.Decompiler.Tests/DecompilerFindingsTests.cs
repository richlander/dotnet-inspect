using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;

namespace ILInspector.Decompiler.Tests;

public class DecompilerFindingsTests
{
    static readonly FindingSubject Subject = new("M", "M");
    static readonly TypeRef Int32 = TypeRef.CoreLib("System", "Int32");
    static readonly TypeRef Object = TypeRef.CoreLib("System", "Object");

    [Fact]
    public void Inspect_FullFunction_IsCompleteEmptyCensus()
    {
        var inspection = CompleteInspection(
            DecompilerFindings.InspectFidelityCauses(
                Function(new Return(new Constant(0, Int32))),
                Subject));

        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Inspect_MissingFunction_IsAbsent()
    {
        var inspection = DecompilerFindings.InspectFidelityCauses(null, Subject);

        Assert.True(inspection is FindingInspection<DecompilerFidelityCause>.Absent);
    }

    [Theory]
    [InlineData(DiagnosticIds.InternalError)]
    [InlineData(DiagnosticIds.ContextUnavailable)]
    [InlineData(DiagnosticIds.EmptyOutput)]
    public void Inspect_OperationFailure_IsFailedRatherThanFinding(string diagnosticId)
    {
        var function = Function(new Return(new Constant(0, Int32)));
        function.Diagnostics.Add(new DecompilerDiagnostic(diagnosticId, "pipeline failed"));

        var inspection = DecompilerFindings.InspectFidelityCauses(function, Subject);
        var failed = inspection switch
        {
            FindingInspection<DecompilerFidelityCause>.Failed value => value,
            _ => throw new InvalidOperationException("Expected a failed inspection."),
        };

        Assert.Contains(diagnosticId, failed.Error.Reason);
    }

    [Fact]
    public void Inspect_UnsupportedNodes_PreserveEveryOccurrenceAndOrder()
    {
        var function = Function([
            new ExpressionStatement(new UnsupportedNode(0x05, "calli", "unsupported call site")),
            new Return(new UnsupportedNode(0x09, "jmp", "unsupported transfer"))]);

        var inspection = CompleteInspection(
            DecompilerFindings.InspectFidelityCauses(function, Subject));

        Assert.Equal(2, inspection.Findings.Length);
        Assert.Equal<int?>([0, 1], inspection.Findings.Select(static finding => finding.Ordinal));
        Assert.Equal(["calli", "jmp"], inspection.Findings.Select(static finding => finding.Payload.Discriminator));
        Assert.Equal([0x05, 0x09], inspection.Findings.Select(static finding => finding.Payload.Location.ILOffset));
    }

    [Fact]
    public void Inspect_ResidualContinue_HasTypedIlLocation()
    {
        var function = Function(new Continue(), blockOffset: 0x20);

        var cause = Assert.Single(
            CompleteInspection(DecompilerFindings.InspectFidelityCauses(function, Subject))
                .Findings).Payload;

        Assert.Equal(DiagnosticIds.UnverifiedContinue, cause.Code);
        Assert.Equal(DecompilerFidelityLocationKind.IlOffset, cause.Location.Kind);
        Assert.Equal(0x20, cause.Location.ILOffset);
        Assert.Equal(DecompilationFidelity.Partial, function.Fidelity);
    }

    [Fact]
    public void Inspect_UnsupportedTypesAtOneSite_AggregateEveryReason()
    {
        var container = new BlockContainer();
        var block = new Block(0);
        container.Add(block);
        block.Add(new Return(new Constant(0, Int32)));
        var signature = new MethodSignature(
            Int32,
            [
                new Parameter("first", TypeRef.Unsupported("function pointer")),
                new Parameter("second", TypeRef.Unsupported("custom modifier")),
            ],
            HasThis: false,
            GenericParameterCount: 0);
        var function = new IrFunction("M", Object, signature, [], container);

        var cause = Assert.Single(
            CompleteInspection(DecompilerFindings.InspectFidelityCauses(function, Subject))
                .Findings).Payload;

        Assert.Equal(DiagnosticIds.UnsupportedType, cause.Code);
        Assert.Contains("function pointer", cause.Discriminator);
        Assert.Contains("custom modifier", cause.Discriminator);
        Assert.Equal(DecompilerFidelityLocationKind.Signature, cause.Location.Kind);
    }

    [Fact]
    public void Compare_CoordinateOnlyChange_IsExact()
    {
        var oldFunction = Function(
            new Return(new UnsupportedNode(0x05, "calli", "unsupported call site")));
        var newFunction = Function(
            new Return(new UnsupportedNode(0x25, "calli", "unsupported call site")));

        var comparison = CompleteComparison(
            DecompilerFindings.CompareFidelityCauses(oldFunction, newFunction, Subject));

        Assert.True(comparison.IsExact);
        Assert.All(comparison.Pairs, pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public void Compare_CauseDetailChange_IsChanged()
    {
        var oldFunction = Function(
            new Return(new UnsupportedNode(0x05, "calli", "unsupported call site")));
        var newFunction = Function(
            new Return(new UnsupportedNode(0x05, "calli", "different unsupported shape")));

        var comparison = CompleteComparison(
            DecompilerFindings.CompareFidelityCauses(oldFunction, newFunction, Subject));

        var changed = Assert.Single(comparison.Pairs) switch
        {
            PairFinding<DecompilerFidelityCause>.Changed value => value,
            _ => throw new InvalidOperationException("Expected a changed pair."),
        };
        Assert.Contains("reason", changed.Detail);
        Assert.False(comparison.IsExact);
    }

    [Fact]
    public void Compare_AbsentFunctions_IsExactWithoutFindings()
    {
        var comparison = CompleteComparison(
            DecompilerFindings.CompareFidelityCauses(null, null, Subject));

        Assert.True(comparison.IsExact);
        Assert.Empty(comparison.Pairs);
    }

    [Fact]
    public void Compare_OperationFailure_DoesNotRunMatching()
    {
        var failedFunction = Function(new Return(new Constant(0, Int32)));
        failedFunction.Diagnostics.Add(new DecompilerDiagnostic(
            DiagnosticIds.InternalError,
            "importer failed"));

        var comparison = DecompilerFindings.CompareFidelityCauses(
            failedFunction,
            Function(new Return(new Constant(0, Int32))),
            Subject);

        Assert.True(comparison is FindingComparison<DecompilerFidelityCause>.Failed);
        Assert.Contains(DiagnosticIds.InternalError, comparison.Failure);
    }

    static IrFunction Function(IrNode statement, int blockOffset = 0)
        => Function([statement], blockOffset);

    static IrFunction Function(IReadOnlyList<IrNode> statements, int blockOffset = 0)
    {
        var container = new BlockContainer();
        var block = new Block(blockOffset);
        container.Add(block);
        foreach (var statement in statements)
            block.Add(statement);

        var signature = new MethodSignature(
            Int32,
            [],
            HasThis: false,
            GenericParameterCount: 0);
        return new IrFunction("M", Object, signature, [], container);
    }

    static FindingInspection<DecompilerFidelityCause>.Complete CompleteInspection(
        FindingInspection<DecompilerFidelityCause> inspection)
        => inspection switch
        {
            FindingInspection<DecompilerFidelityCause>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete inspection."),
        };

    static FindingComparison<DecompilerFidelityCause>.Complete CompleteComparison(
        FindingComparison<DecompilerFidelityCause> comparison)
        => comparison switch
        {
            FindingComparison<DecompilerFidelityCause>.Complete complete => complete,
            FindingComparison<DecompilerFidelityCause>.Failed failed => throw new InvalidOperationException(
                $"Expected a completed comparison: {failed.Failure}"),
        };
}
