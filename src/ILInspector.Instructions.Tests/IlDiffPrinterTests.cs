using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

public class IlDiffPrinterTests
{
    [Fact]
    public void ToDisplayRow_ProjectsStructuredFieldsWithoutParsingMessage()
    {
        var operation = new CanonicalIlOperation(
            Offset: 10,
            OpcodeFamily: "call",
            Operand: new IlOperandIdentity(IlOperandIdentityKind.Token, "int32 [System.Runtime]System.Math::Abs(int32)"));
        var row = new IlDiffRow(7, IlDiffKind.Add, operation, "Added IL operation 'call int32 [System.Runtime]System.Math::Abs(int32)'");

        var display = IlDiffPrinter.ToDisplayRow(row);

        Assert.Equal(7, display.HunkId);
        Assert.Equal(IlDiffKind.Add, display.Kind);
        Assert.Equal("+", display.Marker);
        Assert.Equal(10, display.RawOffset);
        Assert.Equal("IL_000A", display.Offset);
        Assert.Equal("call", display.OpcodeFamily);
        Assert.Equal(IlOperandIdentityKind.Token, display.OperandKind);
        Assert.Equal("int32 [System.Runtime]System.Math::Abs(int32)", display.OperandValue);
        Assert.Equal("call int32 [System.Runtime]System.Math::Abs(int32)", display.Operation);
        Assert.Equal(row.Message, display.Message);
        Assert.Equal("h7 + IL_000A call int32 [System.Runtime]System.Math::Abs(int32)", display.UnifiedLine);
    }

    [Theory]
    [InlineData(IlDiffKind.Add, "+")]
    [InlineData(IlDiffKind.Remove, "-")]
    [InlineData(IlDiffKind.Context, " ")]
    public void ToDisplayRow_UsesUnifiedDiffMarkers(IlDiffKind kind, string marker)
    {
        var row = new IlDiffRow(
            0,
            kind,
            new CanonicalIlOperation(0, "nop", Operand: null),
            "row message");

        var display = IlDiffPrinter.ToDisplayRow(row);

        Assert.Equal(marker, display.Marker);
    }

    [Fact]
    public void RenderUnified_RendersRowsInProducerFormat()
    {
        var left = MethodInstructions.Decode([0x16, 0x2a], 2, []);
        var right = MethodInstructions.Decode([0x17, 0x2a], 2, []);
        var diff = IlBodyDiff.Compare(left, right);

        var text = IlDiffPrinter.RenderUnified(diff);

        Assert.Equal(
            """
            h0 - IL_0000 ldc.i4 0
            h0 + IL_0000 ldc.i4 1
            """,
            text);
    }

    [Fact]
    public void ToDisplayResult_PreservesFailureAsProducerOwnedDisplay()
    {
        var diff = IlBodyDiffResult.Failed(
            IlDiffFailureKind.DecodeFailure,
            "old body decode failed",
            side: "old");

        var display = IlDiffPrinter.ToDisplayResult(diff);

        Assert.Equal("IL diff failed: old body decode failed", display.Failure);
        var failure = Assert.Single(display.FailureRows);
        Assert.Equal(IlDiffFailureKind.DecodeFailure, failure.Kind);
        Assert.Equal("old body decode failed", failure.Message);
        Assert.Equal("old", failure.Side);
        Assert.Empty(display.Rows);
        Assert.Equal(["IL diff failed: old body decode failed"], IlDiffPrinter.ToUnifiedLines(diff));
        Assert.Equal(["IL diff failed: old body decode failed"], IlDiffPrinter.ToUnifiedLines(display));
    }

    [Fact]
    public void ToDisplayResult_ProjectsAvailabilityFailureRows()
    {
        var display = IlDiffPrinter.ToDisplayResult(IlBodyDiffResult.OldBodyMissing("no RVA"));

        var failure = Assert.Single(display.FailureRows);
        Assert.Equal(IlDiffFailureKind.OldBodyMissing, failure.Kind);
        Assert.Equal("old body missing", failure.Message);
        Assert.Equal("old", failure.Side);
        Assert.Equal("no RVA", failure.Detail);
        Assert.Equal(["IL diff failed: old body missing"], IlDiffPrinter.ToUnifiedLines(display));
    }

    [Fact]
    public void ToDisplayResult_ProjectsUnsupportedBoundaryFailureRows()
    {
        var display = IlDiffPrinter.ToDisplayResult(
            IlBodyDiffResult.UnsupportedBoundary("unsupported canonicalization boundary", "slot identity"));

        var failure = Assert.Single(display.FailureRows);
        Assert.Equal(IlDiffFailureKind.UnsupportedBoundary, failure.Kind);
        Assert.Equal("unsupported canonicalization boundary", failure.Message);
        Assert.Null(failure.Side);
        Assert.Equal("slot identity", failure.Detail);
    }

    [Fact]
    public void ToDisplayResult_TreatsDefaultResultRowsAsEmpty()
    {
        var diff = new IlBodyDiffResult(IsExact: false, Failure: "legacy failure", Rows: default);

        var display = IlDiffPrinter.ToDisplayResult(diff);

        Assert.Equal("IL diff failed: legacy failure", display.Failure);
        Assert.Empty(display.Rows);
        Assert.Equal(["IL diff failed: legacy failure"], IlDiffPrinter.ToUnifiedLines(display));
    }

    [Fact]
    public void ToUnifiedLines_PreservesRowsWhenFailureCarriesPartialEvidence()
    {
        var row = new IlDiffRow(
            0,
            IlDiffKind.Remove,
            new CanonicalIlOperation(0, "nop", Operand: null),
            "Removed IL operation 'nop'");
        var diff = new IlBodyDiffResult(
            IsExact: false,
            Failure: "partial decode failed",
            Rows: [row]);

        var lines = IlDiffPrinter.ToUnifiedLines(diff);

        Assert.Equal(
            [
                "IL diff failed: partial decode failed",
                "h0 - IL_0000 nop",
            ],
            lines);
    }

    [Fact]
    public void ToUnifiedLines_PreservesRowsWhenTypedFailureCarriesPartialEvidence()
    {
        var row = new IlDiffRow(
            0,
            IlDiffKind.Remove,
            new CanonicalIlOperation(0, "nop", Operand: null),
            "Removed IL operation 'nop'");
        var diff = IlBodyDiffResult.UnsupportedBoundary(
            "unsupported canonicalization boundary",
            [row],
            detail: "slot identity");

        var lines = IlDiffPrinter.ToUnifiedLines(diff);

        Assert.Equal(
            [
                "IL diff failed: unsupported canonicalization boundary",
                "h0 - IL_0000 nop",
            ],
            lines);
    }

    [Fact]
    public void ToUnifiedLines_TreatsDefaultRowsAsEmpty()
    {
        var display = new IlDiffDisplayResult(Failure: null, Rows: default);

        Assert.True(display.IsEmpty);
        Assert.Empty(IlDiffPrinter.ToUnifiedLines(display));
        Assert.Equal("", IlDiffPrinter.RenderUnified(display));
    }

    [Fact]
    public void ToUnifiedLines_PreservesFailureWhenRowsAreDefault()
    {
        var display = new IlDiffDisplayResult(Failure: "IL diff failed: old body decode failed", Rows: default);

        Assert.False(display.IsEmpty);
        Assert.Equal(["IL diff failed: old body decode failed"], IlDiffPrinter.ToUnifiedLines(display));
    }
}
