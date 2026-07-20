using ILInspector.DecompilerHarness;
using ILInspector.Instructions;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

namespace ILInspector.Decompiler.Tests;

[Trait("Speed", "Slow")]
public class ReturnToSenderEvidenceTests
{
    [Fact]
    public void FromCatalog_PreservesExactRtsStatusAndMemberAnchor()
    {
        Assert.True(ResearchChangeMechanism.AllAvailable.HasFlag(ResearchChangeMechanism.ReturnToSender));

        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            [GeneratedFixtureCatalog.MinimalPropertyLiteral]);

        var evidence = ReturnToSenderEvidence.FromCatalog(run);
        var row = Assert.Single(evidence, row => row.Method == "get_Method1");

        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, row.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, row.CompileBackStatus);
        Assert.NotNull(row.Anchor);
        Assert.StartsWith("Method1~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.Null(row.IlDiffDiagnostic);
        Assert.NotNull(row.IlDiff);
        Assert.True(row.IlDiff.Diff.IsExact);
        Assert.Equal(row.IlDiff.Old.Identity, row.IlDiff.New.Identity);

        var research = ReturnToSenderEvidence.ToResearchComparison(evidence);
        var subject = Assert.Single(research.MembersWhere(member => member.Subject.Id == row.Anchor.StableSelector));
        Assert.Contains(subject.Changes, item =>
            item.Mechanism == ResearchChangeMechanism.ReturnToSender
            && item.Descriptor.Id == "rts.status.pass"
            && item.Category == ResearchChangeCategory.RoundTrip);
        Assert.DoesNotContain(subject.Changes, item => item.Mechanism == ResearchChangeMechanism.IlBody);
    }

    [Fact]
    public void ToResearchComparison_ProjectsStructuredIlDiffRowsWithoutReportTextParsing()
    {
        var anchor = new MemberAnchor(
            "Method1~abcdef1234",
            "M:TestType.Method1()",
            "abcdef1234",
            "TestType",
            "Method1");
        var typedDiff = new IlMemberDiffResult(
            new IlMemberDiffSubject("old-id", "old-label"),
            new IlMemberDiffSubject("new-id", "new-label"),
            new IlBodyDiffResult(
                IlBodyDiffOutcome.OperandDiff,
                Failure: null,
                Rows:
                [
                    new IlDiffRow(
                        0,
                        IlDiffKind.Context,
                        new CanonicalIlOperation(0, "nop", Operand: null),
                        "Unchanged IL operation 'nop'"),
                    new IlDiffRow(
                        1,
                        IlDiffKind.Remove,
                        new CanonicalIlOperation(1, "ldc.i4", new IlOperandIdentity(IlOperandIdentityKind.Immediate, "1")),
                        "Removed IL operation 'ldc.i4 1'"),
                    new IlDiffRow(
                        1,
                        IlDiffKind.Add,
                        new CanonicalIlOperation(1, "ldc.i4", new IlOperandIdentity(IlOperandIdentityKind.Immediate, "2")),
                        "Added IL operation 'ldc.i4 2'"),
                ]));
        var evidence = new ReturnToSenderEvidenceRow(
            anchor,
            "TestType",
            "Method1",
            Overload: 0,
            GeneratedFixtureReturnToSenderStatus.Fail,
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            "opcode-diff",
            Detail: null,
            IlDiffDiagnostic: null,
            IlDiff: typedDiff);

        var research = ReturnToSenderEvidence.ToResearchComparison([evidence]);

        var subject = Assert.Single(research.BySubject());
        Assert.Equal(anchor.StableSelector, subject.Subject.Id);
        Assert.Equal("TestType.Method1", subject.Subject.Display);
        Assert.Equal("TestType", subject.Subject.TypeName);
        Assert.Equal("Method1", subject.Subject.MemberName);
        Assert.Contains(subject.Changes, item =>
            item.Mechanism == ResearchChangeMechanism.ReturnToSender
            && item.Descriptor.Id == "rts.status.fail");
        Assert.Contains(subject.Changes, item =>
            item.Mechanism == ResearchChangeMechanism.IlBody
            && item.Descriptor.Id == "il.operation.removed"
            && item.OldValue == "ldc.i4 1"
            && item.OldIlOffset == 1
            && item.IlMemberDiff == typedDiff
            && item.IlDisplayRows.Length == 1
            && item.IlDisplayRows[0].UnifiedLine == "h1 - IL_0001 ldc.i4 1");
        Assert.Contains(subject.Changes, item =>
            item.Mechanism == ResearchChangeMechanism.IlBody
            && item.Descriptor.Id == "il.operation.added"
            && item.NewValue == "ldc.i4 2"
            && item.NewIlOffset == 1
            && item.IlMemberDiff == typedDiff
            && item.IlDisplayRows.Length == 1
            && item.IlDisplayRows[0].UnifiedLine == "h1 + IL_0001 ldc.i4 2");
        Assert.DoesNotContain(subject.Changes, item => item.Descriptor.Id == "il.operation.context");
    }

    [Fact]
    public void ToResearchComparison_ProjectsStructuredIlDiffFailureRows()
    {
        var anchor = new MemberAnchor(
            "Method1~abcdef1234",
            "M:TestType.Method1()",
            "abcdef1234",
            "TestType",
            "Method1");
        var failure = new IlDiffDisplayFailureRow(
            IlDiffFailureKind.NewBodyMissing,
            "new body missing",
            Side: "new",
            Detail: "method has no body");
        var evidence = new ReturnToSenderEvidenceRow(
            anchor,
            "TestType",
            "Method1",
            Overload: 0,
            GeneratedFixtureReturnToSenderStatus.Fail,
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            "opcode-diff",
            Detail: null,
            IlDiffDiagnostic: new IlDiffDisplayResult(
                Failure: failure.UnifiedLine,
                Rows: [],
                FailureRows: [failure]),
            IlDiff: null);

        var research = ReturnToSenderEvidence.ToResearchComparison([evidence]);

        var subject = Assert.Single(research.BySubject());
        var ilFailure = Assert.Single(
            subject.Changes,
            item => item.Descriptor.Id == "il.diff.new-body-missing");
        Assert.Equal(ResearchChangeMechanism.IlBody, ilFailure.Mechanism);
        Assert.Equal("method has no body", ilFailure.Detail);
        Assert.Same(failure, ilFailure.IlDisplayFailureRow);
    }
}
