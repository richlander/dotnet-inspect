using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using ILInspector.Decompiler;
using ILInspector.Findings;

namespace DotnetInspector.Tests;

public class FidelityCauseSectionTests
{
    static readonly FindingSubject Subject = new("member:test", "Test.M");

    [Fact]
    public void BuildRows_CompleteEmpty_ReportsFullFidelity()
    {
        var inspection = new FindingInspection<DecompilerFidelityCause>.Complete([]);

        var row = Assert.Single(ApiOutputFormatter.BuildFidelityCauseRows(inspection));

        Assert.Equal("Complete", row.State);
        Assert.Null(row.Code);
        Assert.Contains("Full", row.Reason);
    }

    [Fact]
    public void BuildRows_Complete_PreservesFindingOrderAndTypedLocations()
    {
        var inspection = new FindingInspection<DecompilerFidelityCause>.Complete(
        [
            Finding(
                new DecompilerFidelityCause(
                    "DEC0010",
                    DecompilerFidelityLocation.Signature,
                    "Function",
                    "method signature",
                    "unsupported signature type",
                    "typedref"),
                0),
            Finding(
                new DecompilerFidelityCause(
                    "DEC0004",
                    DecompilerFidelityLocation.AtIlOffset(0x2a),
                    "UnsupportedNode",
                    "mkrefany",
                    "unsupported opcode",
                    "mkrefany"),
                1),
            Finding(
                new DecompilerFidelityCause(
                    "DEC0014",
                    DecompilerFidelityLocation.AtLocal(3),
                    "PinnedLocal",
                    "Pinned local V_3",
                    "unraised pinned local"),
                2),
        ]);

        var rows = ApiOutputFormatter.BuildFidelityCauseRows(inspection);

        Assert.Equal(["DEC0010", "DEC0004", "DEC0014"], rows.Select(row => row.Code));
        Assert.Equal("signature", rows[0].Location);
        Assert.Contains("IL_002A", rows[1].Location);
        Assert.Contains("V_3", rows[2].Location);
    }

    [Fact]
    public void BuildRows_AbsentAndFailed_RemainDistinct()
    {
        var absent = new FindingInspection<DecompilerFidelityCause>.Absent("no body");
        var failed = new FindingInspection<DecompilerFidelityCause>.Failed(
            new InspectionError(
                Subject,
                DecompilerFindings.FidelityInspectionDescriptor,
                "DEC0001: importer failed"));

        var absentRow = Assert.Single(ApiOutputFormatter.BuildFidelityCauseRows(absent));
        var failedRow = Assert.Single(ApiOutputFormatter.BuildFidelityCauseRows(failed));

        Assert.Equal("Absent", absentRow.State);
        Assert.Equal("no body", absentRow.Reason);
        Assert.Equal("Failed", failedRow.State);
        Assert.Equal("DEC0001: importer failed", failedRow.Reason);
    }

    [Fact]
    public void BuildInspection_DistinguishesNoBodyFromImporterFailure()
    {
        var importerFailure = DecompilerResult.Failure(
            DiagnosticIds.InternalError,
            "BadImageFormatException: invalid method body");

        var absent = MemberCodeProvider.BuildFidelityCauseInspection(
            methodHasBody: false,
            raisedFunction: null,
            importerFailure,
            Subject);
        var failed = MemberCodeProvider.BuildFidelityCauseInspection(
            methodHasBody: true,
            raisedFunction: null,
            importerFailure,
            Subject);

        Assert.IsType<FindingInspection<DecompilerFidelityCause>.Absent>(absent.Value);
        var failure = Assert.IsType<FindingInspection<DecompilerFidelityCause>.Failed>(failed.Value);
        Assert.Contains(DiagnosticIds.InternalError, failure.Error.Reason);
        Assert.Contains("BadImageFormatException", failure.Error.Reason);
    }

    static Finding<DecompilerFidelityCause> Finding(
        DecompilerFidelityCause cause,
        int ordinal)
        => new(
            Subject,
            DecompilerFindings.FidelityCauseDescriptor,
            new FindingKey(cause.Code),
            cause,
            ordinal);
}
