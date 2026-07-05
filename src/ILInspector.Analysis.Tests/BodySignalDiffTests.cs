using ILInspector.Analysis;
using DotnetInspector.Fixtures;

namespace ILInspector.Analysis.Tests;

public class BodySignalDiffTests
{
    [Fact]
    public void CompareUnsafe_SurfacesAddedUnsafeOperation()
    {
        var oldIndex = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.OldAssemblyPath());
        var newIndex = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.NewAssemblyPath());

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row =>
            row.Kind == BodySignalDiffKind.Added
            && row.Signal == "stackalloc"
            && row.Member.Contains("AddsUnsafe", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareUnsafe_SelfDiffHasNoRows()
    {
        var index = LibraryBodyIndex.Open(FixtureCatalog.DiffPair.NewAssemblyPath());

        var diff = BodySignalDiff.CompareUnsafe(index, index);

        Assert.Empty(diff.Rows);
    }

    [Fact]
    public void CompareUnsafe_MethodKeyDistinguishesSameNameDifferentArityDeclaringTypes()
    {
        var method1 = DiffMethod(TypeRef.Definition("Asm", "Ns", "Box`1"), "Use");
        var method2 = DiffMethod(TypeRef.Definition("Asm", "Ns", "Box`2"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method1],
            [new UnsafeEvidence(method1, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method2],
            [new UnsafeEvidence(method2, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Removed);
        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Added);
    }

    [Fact]
    public void CompareUnsafe_MethodKeyDistinguishesSameSignatureDifferentGenericArity()
    {
        var arity1 = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use", genericArity: 1);
        var arity2 = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use", genericArity: 2);
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [arity1],
            [new UnsafeEvidence(arity1, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [arity2],
            [new UnsafeEvidence(arity2, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Removed && row.Member.Contains("|1|False|", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Added && row.Member.Contains("|2|False|", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareUnsafe_MethodKeyDistinguishesSameSignatureExtensionMarker()
    {
        var normal = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use", isExtension: false);
        var extension = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use", isExtension: true);
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [normal],
            [new UnsafeEvidence(normal, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [extension],
            [new UnsafeEvidence(extension, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Removed && row.Member.Contains("|0|False|", StringComparison.Ordinal));
        Assert.Contains(diff.Rows, row => row.Kind == BodySignalDiffKind.Added && row.Member.Contains("|0|True|", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareUnsafe_PreservesCountOfRepeatedUnsafeOperations()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 4, null),
            ]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        var added = Assert.Single(diff.Rows);
        Assert.Equal(BodySignalDiffKind.Added, added.Kind);
        Assert.Equal("stackalloc", added.Operation);
    }

    [Fact]
    public void CompareUnsafe_AttributesPrependedOffsetAdditionToNewOffset()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 10, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 20, null),
            ]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 5, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 10, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 20, null),
            ]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        var added = Assert.Single(diff.Rows);
        Assert.Equal(BodySignalDiffKind.Added, added.Kind);
        Assert.Equal(5, added.ILOffset);
    }

    [Fact]
    public void CompareUnsafe_OffsetShiftWithAddedOperation_DoesNotEmitRemoveAddFlood()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 10, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 20, null),
            ]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 5, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 15, null),
                new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 25, null),
            ]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        var added = Assert.Single(diff.Rows);
        Assert.Equal(BodySignalDiffKind.Added, added.Kind);
        Assert.Null(added.ILOffset);
    }

    static MethodIdentity DiffMethod(TypeRef declaring, string name, int genericArity = 0, bool isExtension = false)
        => new(
            "Asm",
            Guid.Empty,
            declaring,
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true,
            IsExtension: isExtension,
            GenericArity: genericArity);
}
