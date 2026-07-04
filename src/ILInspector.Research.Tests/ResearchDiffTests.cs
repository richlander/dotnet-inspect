using ILInspector.Analysis;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.Research;
using System.Collections.Immutable;

namespace ILInspector.Research.Tests;

public class ResearchDiffTests
{
    [Fact]
    public void FromApiDiff_PreservesApiChangeMessageAndStructuredSubject()
    {
        var subject = new ApiChangeSubject(
            ApiChangeSubjectKind.Member,
            "Ns.Widget",
            "Parse",
            OldIdentity: "method:Parse(System.String)",
            NewIdentity: "method:Parse(System.ReadOnlySpan<System.Char>)");
        var change = new ApiChange(
            ChangeKind.MemberSignatureChanged,
            ChangeClassification.Breaking,
            "Member 'Parse' signature changed",
            OldValue: subject.OldIdentity,
            NewValue: subject.NewIdentity,
            Subject: subject);
        var api = new ApiDiff { TypeDiffs = [new TypeDiff("Ns.Widget", [change])] };

        var result = ResearchDiff.FromApiDiff(api);

        var row = Assert.Single(result.Rows);
        Assert.Equal("api.member-signature-changed", row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.MetadataApi, row.EvidenceKind);
        Assert.Equal(change.Message, row.Message);
        var apiChange = Assert.IsType<ApiChange>(row.ApiChange);
        Assert.Same(change, apiChange);
        Assert.Equal(ApiChangeSubjectKind.Member, apiChange.Subject?.Kind);
        Assert.Equal("Parse", apiChange.Subject?.MemberName);
    }

    [Fact]
    public void FromIlBodyDiff_PreservesIlRowMessageAndOperationIdentity()
    {
        var operation = new CanonicalIlOperation(
            Offset: 0,
            OpcodeFamily: "ldc.i4",
            Operand: new IlOperandIdentity(IlOperandIdentityKind.Immediate, "2"));
        var ilRow = new IlDiffRow(3, IlDiffKind.Add, operation, "Added IL operation 'ldc.i4 2'");
        var diff = new IlBodyDiffResult(IsExact: false, Failure: null, [ilRow]);

        var result = ResearchDiff.FromIlBodyDiff(diff);

        var row = Assert.Single(result.Rows);
        Assert.Equal("il.operation.added", row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.IlBody, row.EvidenceKind);
        Assert.Equal(ilRow.Message, row.Message);
        var projectedIlRow = Assert.IsType<IlDiffRow>(row.IlRow);
        Assert.Same(ilRow, projectedIlRow);
        Assert.Equal("ldc.i4", projectedIlRow.Operation.OpcodeFamily);
        Assert.Equal("2", projectedIlRow.Operation.Operand?.Value);
    }

    [Fact]
    public void FromIlBodyDiff_PreservesFailureMessageWithoutInventingRowIdentity()
    {
        var diff = new IlBodyDiffResult(IsExact: false, Failure: "old body decode failed", []);

        var result = ResearchDiff.FromIlBodyDiff(diff);

        var row = Assert.Single(result.Rows);
        Assert.Equal("il.diff.failed", row.ChangeId);
        Assert.Equal("old body decode failed", row.Message);
        Assert.Null(row.IlRow);
    }

    [Fact]
    public void FromBodySignalDiff_PreservesSignalRowAsTypedEvidence()
    {
        var signal = new BodySignalDiffRow(
            BodySignalDiffKind.Added,
            Signal: "stackalloc",
            Member: "Asm|Ns.UnsafeApi|Use||System.Void",
            Operation: "byte*",
            ILOffset: 2,
            Evidence: "stackalloc");
        var diff = new BodySignalDiffResult([signal]);

        var result = ResearchDiff.FromBodySignalDiff(diff);

        var row = Assert.Single(result.Rows);
        Assert.Equal("unsafe.stackalloc.added", row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.BodySignal, row.EvidenceKind);
        var signalRow = Assert.IsType<BodySignalDiffRow>(row.BodySignalRow);
        Assert.Same(signal, signalRow);
        Assert.Equal(2, signalRow.ILOffset);
    }

    [Fact]
    public void FromBodySignalDiff_UsesActualUnsafeSignalForChangeId()
    {
        var oldIndex = LibraryBodyIndex.Open(DiffFixturePath("DiffFixtures.V1"));
        var newIndex = LibraryBodyIndex.Open(DiffFixturePath("DiffFixtures.V2"));
        var signalDiff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        var result = ResearchDiff.FromBodySignalDiff(signalDiff);

        Assert.Contains(result.Rows, row =>
            row.ChangeId == "unsafe.stackalloc.added"
            && row.BodySignalRow is { Signal: "stackalloc", Operation: "byte*" });
        Assert.DoesNotContain(result.Rows, row => row.ChangeId == "stackalloc.byte.added");
    }

    [Fact]
    public void Combine_AppendsRowsFromAllLowerLayers()
    {
        var api = ResearchDiff.FromApiDiff(new ApiDiff
        {
            TypeDiffs =
            [
                new TypeDiff("Ns.Widget", [
                    new ApiChange(
                        ChangeKind.TypeAdded,
                        ChangeClassification.Additive,
                        "Type 'Ns.Widget' was added",
                        Subject: new ApiChangeSubject(ApiChangeSubjectKind.Type, "Ns.Widget"))
                ])
            ]
        });
        var il = ResearchDiff.FromIlBodyDiff(new IlBodyDiffResult(IsExact: true, Failure: null, []));

        var combined = ResearchDiff.Combine(api, il);

        Assert.Single(combined.Rows);
        Assert.Equal("api.type-added", combined.Rows[0].ChangeId);
    }

    static string DiffFixturePath(string project)
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName, "..", "..", project, outputDirectory.Name, "DiffFixtureSample.dll"));
        Assert.True(File.Exists(path), $"Expected diff fixture assembly at {path}");
        return path;
    }
}
