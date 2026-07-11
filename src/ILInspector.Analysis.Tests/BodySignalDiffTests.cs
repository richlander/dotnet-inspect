using System.Collections.Immutable;

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
    public void CompareUnsafe_MethodKeyDistinguishesSameNameDifferentGenericArity()
    {
        var method1 = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var method2 = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use") with { GenericArity = 1 };
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

    [Fact]
    public void CompareUnsafe_DoesNotMatchMemberEvidenceToBodyEvidence()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", null, null)]);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 4, null)]);

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Contains(diff.Rows, row =>
            row.Kind == BodySignalDiffKind.Removed
            && row.ILOffset is null);
        Assert.Contains(diff.Rows, row =>
            row.Kind == BodySignalDiffKind.Added
            && row.ILOffset == 4);
    }

    [Fact]
    public void CompareUnsafe_ReorderedOperationsDoNotBecomeRemoveAddRows()
    {
        var method = DiffMethod(TypeRef.Definition("Asm", "Ns", "UnsafeApi"), "Use");
        var oldIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [],
            unsafetyOccurrences: new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>
            {
                [method.MetadataToken] =
                [
                    new(method, 0, UnsafetyKind.StackAlloc, "byte*"),
                    new(method, 4, UnsafetyKind.Deref, "int"),
                ],
            });
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [],
            unsafetyOccurrences: new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>
            {
                [method.MetadataToken] =
                [
                    new(method, 0, UnsafetyKind.Deref, "int"),
                    new(method, 4, UnsafetyKind.StackAlloc, "byte*"),
                ],
            });

        var diff = BodySignalDiff.CompareUnsafe(oldIndex, newIndex);

        Assert.Empty(diff.Rows);
    }

    static MethodIdentity DiffMethod(TypeRef declaring, string name)
        => new(
            "Asm",
            Guid.Empty,
            declaring,
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
}
