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
        var run = GeneratedFixtureRunner.RunReturnToSenderCatalog(
            [GeneratedFixtureCatalog.MinimalPropertyLiteral]);

        var evidence = ReturnToSenderEvidence.FromCatalog(run);
        var row = Assert.Single(evidence, row => row.Method == "get_Method1");

        Assert.Equal(GeneratedFixtureReturnToSenderStatus.Pass, row.Status);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, row.CompileBackStatus);
        Assert.NotNull(row.Anchor);
        Assert.StartsWith("Method1~", row.Anchor.StableSelector, StringComparison.Ordinal);
        Assert.Null(row.IlDiffDiagnostic);

        var research = ReturnToSenderEvidence.ToResearchDiff(evidence);
        var subject = Assert.Single(research.MembersWhere(member => member.Subject.Id == row.Anchor.StableSelector));
        Assert.Contains(subject.Evidence, item =>
            item.Mechanism == ResearchDiffMechanism.ReturnToSender
            && item.ChangeId == "rts.status.pass"
            && item.Category == ResearchDiffChangeCategory.RoundTrip);
        Assert.DoesNotContain(subject.Evidence, item => item.Mechanism == ResearchDiffMechanism.IlBody);
    }

    [Fact]
    public void ToResearchDiff_ProjectsStructuredIlDiffRowsWithoutReportTextParsing()
    {
        var anchor = new MemberAnchor(
            "Method1~abcdef1234",
            "M:TestType.Method1()",
            "abcdef1234",
            "TestType",
            "Method1");
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
                Failure: null,
                Rows:
                [
                    new IlDiffDisplayRow(
                        0,
                        IlDiffKind.Remove,
                        "-",
                        0,
                        "IL_0000",
                        "ldc.i4",
                        IlOperandIdentityKind.Immediate,
                        "1",
                        "ldc.i4 1",
                        "Removed IL operation 'ldc.i4 1'"),
                    new IlDiffDisplayRow(
                        0,
                        IlDiffKind.Add,
                        "+",
                        0,
                        "IL_0000",
                        "ldc.i4",
                        IlOperandIdentityKind.Immediate,
                        "2",
                        "ldc.i4 2",
                        "Added IL operation 'ldc.i4 2'"),
                ]));

        var research = ReturnToSenderEvidence.ToResearchDiff([evidence]);

        var subject = Assert.Single(research.Subjects);
        Assert.Equal(anchor.StableSelector, subject.Subject.Id);
        Assert.Contains(subject.Evidence, item =>
            item.Mechanism == ResearchDiffMechanism.ReturnToSender
            && item.ChangeId == "rts.status.fail");
        Assert.Contains(subject.Evidence, item =>
            item.Mechanism == ResearchDiffMechanism.IlBody
            && item.ChangeId == "il.operation.removed"
            && item.OldValue == "ldc.i4 1"
            && item.OldIlOffset == 0);
        Assert.Contains(subject.Evidence, item =>
            item.Mechanism == ResearchDiffMechanism.IlBody
            && item.ChangeId == "il.operation.added"
            && item.NewValue == "ldc.i4 2"
            && item.NewIlOffset == 0);
    }
}
